using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Analog
{
    /// <summary>How a failed part behaves electrically.</summary>
    public enum FailureMode
    {
        None,
        Open,
        Short
    }

    /// <summary>Receives named measurements from a device after an accepted time step.</summary>
    public interface ISignalSink
    {
        void Write(string deviceId, string key, double value);
    }

    /// <summary>
    /// Base class for every element of the analog netlist. A device contributes
    /// to the MNA system through <see cref="Stamp"/>, which is called once per
    /// Newton iteration, and updates its history in <see cref="Accept"/> once the
    /// time step has converged.
    /// </summary>
    public abstract class AnalogDevice
    {
        protected AnalogDevice(string id)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
        }

        public string Id { get; }

        /// <summary>Number of extra MNA unknowns (branch currents etc.).</summary>
        public virtual int BranchCount => 0;

        /// <summary>Index of the first branch unknown inside the solution vector.</summary>
        public int BranchBase { get; internal set; } = -1;

        /// <summary>True if the stamp depends on the current solution estimate.</summary>
        public virtual bool IsNonlinear => false;

        /// <summary>
        /// Set by a device when an external change (switch flipped, pin driven)
        /// makes the previous solution history invalid.
        /// </summary>
        public bool PendingDiscontinuity { get; protected set; }

        internal bool ConsumeDiscontinuity()
        {
            bool pending = PendingDiscontinuity;
            PendingDiscontinuity = false;
            return pending;
        }

        /// <summary>Failure state. A failed part stays failed until <see cref="Repair"/>.</summary>
        public FailureMode Failure { get; protected set; } = FailureMode.None;

        public bool IsFailed => Failure != FailureMode.None;

        public string FailureReason { get; protected set; } = string.Empty;

        public virtual void Repair()
        {
            Failure = FailureMode.None;
            FailureReason = string.Empty;
            PendingDiscontinuity = true;
        }

        protected void Fail(FailureMode mode, string reason, AcceptContext ctx)
        {
            if (IsFailed) return;
            Failure = mode;
            FailureReason = reason;
            ctx.RequestDiscontinuity();
            ctx.Events.Add(new AnalogEvent(ctx.Time, Id, AnalogEventKind.Failure, reason));
        }

        /// <summary>Called once before simulation; allocate internal nodes or child devices here.</summary>
        public virtual void Setup(AnalogCircuit circuit) { }

        /// <summary>Called before the Newton loop of each time step.</summary>
        public virtual void BeginStep(StepInfo step) { }

        /// <summary>Add this device's contribution for the current Newton iterate.</summary>
        public abstract void Stamp(StampContext ctx);

        /// <summary>Called after the step converged; commit history and integrate slow state.</summary>
        public virtual void Accept(AcceptContext ctx) { }

        /// <summary>Earliest time after <paramref name="time"/> at which this device has a waveform corner.</summary>
        public virtual double NextBreakpoint(double time) => double.PositiveInfinity;

        /// <summary>Publish measurements (voltage, current, power, temperature...).</summary>
        public virtual void Report(ISignalSink sink) { }

        /// <summary>Return to the power-on state (history cleared, damage kept).</summary>
        public virtual void Reset() { }
    }

    public readonly struct StepInfo
    {
        public StepInfo(double time, double dt, bool isDc, bool trapezoidal)
        {
            Time = time;
            Dt = dt;
            IsDc = isDc;
            Trapezoidal = trapezoidal;
        }

        /// <summary>Time at the end of the step being solved.</summary>
        public double Time { get; }
        public double Dt { get; }
        public bool IsDc { get; }
        public bool Trapezoidal { get; }
    }

    public enum AnalogEventKind
    {
        Failure,
        Warning,
        StateChange
    }

    public readonly struct AnalogEvent
    {
        public AnalogEvent(double time, string deviceId, AnalogEventKind kind, string message)
        {
            Time = time;
            DeviceId = deviceId;
            Kind = kind;
            Message = message;
        }

        public double Time { get; }
        public string DeviceId { get; }
        public AnalogEventKind Kind { get; }
        public string Message { get; }

        public override string ToString() => $"[{Time:G6}s] {Kind} {DeviceId}: {Message}";
    }

    /// <summary>
    /// View of the MNA system handed to devices while stamping. Node 0 is ground;
    /// node n (n &gt; 0) owns unknown n-1. Branch unknowns follow the node unknowns.
    /// </summary>
    public sealed class StampContext
    {
        internal MnaSystem Mna = null!;
        internal double[] X = Array.Empty<double>();

        public double Time { get; internal set; }
        public double Dt { get; internal set; }
        public bool IsDc { get; internal set; }
        public bool Trapezoidal { get; internal set; }
        public double Gmin { get; internal set; }
        public double SourceScale { get; internal set; } = 1.0;
        public double AmbientTempC { get; internal set; } = 25.0;

        /// <summary>Set by a device that had to limit a junction voltage; forces another iteration.</summary>
        public bool LimitingApplied { get; set; }

        public double V(int node) => node <= 0 ? 0.0 : X[node - 1];

        public double V(int a, int b) => V(a) - V(b);

        public double Unknown(int index) => X[index];

        /// <summary>Conductance g between nodes a and b.</summary>
        public void AddConductance(int a, int b, double g) => Mna.AddConductance(a - 1, b - 1, g);

        /// <summary>Independent current i flowing through the device from node <paramref name="from"/> to node <paramref name="to"/>.</summary>
        public void AddCurrent(int from, int to, double i)
        {
            if (from > 0) Mna.AddRhs(from - 1, -i);
            if (to > 0) Mna.AddRhs(to - 1, i);
        }

        /// <summary>Current g*(V(cp)-V(cm)) flowing through the device from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public void AddTransconductance(int from, int to, int cp, int cm, double g)
        {
            int rf = from - 1, rt = to - 1, kp = cp - 1, km = cm - 1;
            if (rf >= 0)
            {
                if (kp >= 0) Mna.Add(rf, kp, g);
                if (km >= 0) Mna.Add(rf, km, -g);
            }
            if (rt >= 0)
            {
                if (kp >= 0) Mna.Add(rt, kp, -g);
                if (km >= 0) Mna.Add(rt, km, g);
            }
        }

        /// <summary>
        /// Couples a branch unknown into KCL: the branch current flows through the
        /// device from <paramref name="from"/> to <paramref name="to"/>.
        /// </summary>
        public void AddBranchCurrent(int from, int to, int branch)
        {
            if (from > 0) Mna.Add(from - 1, branch, 1.0);
            if (to > 0) Mna.Add(to - 1, branch, -1.0);
        }

        /// <summary>Adds coeff*V(node) to the equation in row <paramref name="row"/>.</summary>
        public void AddRowNode(int row, int node, double coeff)
        {
            if (node > 0) Mna.Add(row, node - 1, coeff);
        }

        /// <summary>Raw matrix access using unknown indices.</summary>
        public void AddMatrix(int row, int col, double value) => Mna.Add(row, col, value);

        public void AddRhs(int row, double value) => Mna.AddRhs(row, value);
    }

    public sealed class AcceptContext
    {
        internal double[] X = Array.Empty<double>();
        private bool _discontinuity;

        public double Time { get; internal set; }
        public double Dt { get; internal set; }
        public bool IsDc { get; internal set; }
        public double AmbientTempC { get; internal set; } = 25.0;
        public bool EnableDamage { get; internal set; } = true;
        public List<AnalogEvent> Events { get; } = new List<AnalogEvent>();

        public double V(int node) => node <= 0 ? 0.0 : X[node - 1];

        public double V(int a, int b) => V(a) - V(b);

        public double Unknown(int index) => X[index];

        /// <summary>A device changed state abruptly; the next step restarts with a small backward-Euler step.</summary>
        public void RequestDiscontinuity() => _discontinuity = true;

        internal bool ConsumeDiscontinuity()
        {
            bool value = _discontinuity;
            _discontinuity = false;
            return value;
        }
    }

    /// <summary>
    /// Single-pole thermal model: junction/body temperature driven by dissipated
    /// power through a thermal resistance to ambient.
    /// </summary>
    public sealed class ThermalState
    {
        public ThermalState(double thermalResistance, double thermalCapacitance)
        {
            ThermalResistance = thermalResistance;
            ThermalCapacitance = thermalCapacitance;
        }

        /// <summary>Kelvin per watt, body to ambient.</summary>
        public double ThermalResistance { get; set; }

        /// <summary>Joule per kelvin.</summary>
        public double ThermalCapacitance { get; set; }

        public double TemperatureC { get; private set; } = 25.0;

        public bool Initialized { get; private set; }

        public void Reset(double ambientC)
        {
            TemperatureC = ambientC;
            Initialized = true;
        }

        /// <summary>
        /// [thermal] The temperature is set from outside (a world thermal network that also knows the
        /// part's mounting, airflow and neighbours); <see cref="Advance"/> then leaves it alone.
        /// </summary>
        public bool Driven { get; set; }

        /// <summary>Sets the temperature computed by the outside model (see <see cref="Driven"/>).</summary>
        public void Set(double temperatureC)
        {
            TemperatureC = temperatureC;
            Initialized = true;
        }

        public void Advance(double powerW, double dt, double ambientC)
        {
            if (Driven) return;
            if (!Initialized) Reset(ambientC);
            if (dt <= 0.0 || ThermalResistance <= 0.0) return;
            if (ThermalCapacitance <= 0.0)
            {
                TemperatureC = ambientC + powerW * ThermalResistance;
                return;
            }

            // Exact solution of the first-order ODE over the step, so large dt stays stable.
            double tau = ThermalResistance * ThermalCapacitance;
            double target = ambientC + powerW * ThermalResistance;
            TemperatureC = target + (TemperatureC - target) * Math.Exp(-dt / tau);
        }
    }

    internal static class AnalogMath
    {
        public const double Boltzmann = 1.380649e-23;
        public const double ElectronCharge = 1.602176634e-19;
        public const double ZeroCelsius = 273.15;

        public static double ThermalVoltage(double tempC) =>
            Boltzmann * (tempC + ZeroCelsius) / ElectronCharge;

        /// <summary>exp() that continues linearly above the overflow guard.</summary>
        public static double SafeExp(double x, out double derivative)
        {
            const double Limit = 80.0;
            if (x > Limit)
            {
                double e = Math.Exp(Limit);
                derivative = e;
                return e * (1.0 + (x - Limit));
            }

            double value = Math.Exp(x);
            derivative = value;
            return value;
        }

        /// <summary>SPICE pn-junction voltage limiting.</summary>
        public static double LimitJunction(double vNew, double vOld, double vt, double vCrit, ref bool limited)
        {
            if (vNew > vCrit && Math.Abs(vNew - vOld) > vt + vt)
            {
                if (vOld > 0.0)
                {
                    double arg = 1.0 + (vNew - vOld) / vt;
                    vNew = arg > 0.0 ? vOld + vt * Math.Log(arg) : vCrit;
                }
                else
                {
                    vNew = vt * Math.Log(vNew / vt);
                }

                limited = true;
            }

            return vNew;
        }

        public static double CriticalVoltage(double vt, double saturationCurrent) =>
            vt * Math.Log(vt / (Math.Sqrt(2.0) * saturationCurrent));

        public static double Clamp(double value, double min, double max) =>
            value < min ? min : (value > max ? max : value);
    }
}
