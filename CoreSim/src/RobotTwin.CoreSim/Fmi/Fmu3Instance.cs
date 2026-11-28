using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace RobotTwin.CoreSim.Fmi
{
    /// <summary>[fmi] A running FMU instance, FMI 2.0 or 3.0: what the circuit's FMU block and the tests use.</summary>
    public interface IFmuInstance : IDisposable
    {
        FmuDescription Description { get; }

        /// <summary>The communication point reached, s.</summary>
        double Time { get; }

        /// <summary>The FMU's messages so far (the last 200).</summary>
        List<string> Messages { get; }

        /// <summary>Starts the experiment, gives parameters and start values, and leaves initialisation.</summary>
        void Initialise(double startTime, IReadOnlyDictionary<string, double>? values = null);

        void Set(FmuVariable v, double value);

        double Get(FmuVariable v);

        /// <summary>One communication step from <see cref="Time"/>.</summary>
        void Step(double h);
    }

    /// <summary>[fmi] Opens an FMU's binary as the instance its FMI version needs.</summary>
    public static class FmuInstance
    {
        public static IFmuInstance Open(FmuArchive fmu, string instanceName) =>
            fmu.Description.IsFmi3 ? new Fmu3Instance(fmu, instanceName) : new Fmu2Instance(fmu, instanceName);
    }

    /// <summary>
    /// [fmi3] A running FMI 3.0 co-simulation instance: the FMU's binary (binaries/x86_64-windows/&lt;id&gt;.dll) loaded through
    /// kernel32, made with <c>fmi3InstantiateCoSimulation</c> (its instantiation token, the resources folder as a path, our
    /// logger; no event mode, no early return), initialised with the parameters and start values, then stepped with
    /// <c>fmi3DoStep</c>. Each variable is set and read through the function of its own type (fmi3SetFloat64, …Int32,
    /// …Boolean, one byte a value). An FMI 3.0 model-exchange-only FMU is not run yet (the 2.0 one is, by our integrator).
    /// </summary>
    public sealed class Fmu3Instance : IFmuInstance
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr InstantiateCoSimulation(string instanceName, string instantiationToken, string resourcePath, byte visible, byte loggingOn,
            byte eventModeUsed, byte earlyReturnAllowed, IntPtr requiredIntermediateVariables, UIntPtr nRequired, IntPtr instanceEnvironment, IntPtr logMessage, IntPtr intermediateUpdate);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int EnterInitializationMode(IntPtr instance, byte toleranceDefined, double tolerance, double startTime, byte stopTimeDefined, double stopTime);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Simple(IntPtr instance);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FreeInstance(IntPtr instance);

        // fmi3Set/GetXxx(instance, vr[], nvr, values[], nValues): the values passed as raw memory of the type's own size.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Access(IntPtr instance, uint[] vr, UIntPtr nvr, IntPtr values, UIntPtr nValues);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int DoStep(IntPtr instance, double currentCommunicationPoint, double communicationStepSize, byte noSetFmuStatePriorToCurrentPoint,
            out byte eventHandlingNeeded, out byte terminateSimulation, out byte earlyReturn, out double lastSuccessfulTime);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void LogMessage(IntPtr instanceEnvironment, int status, IntPtr category, IntPtr message);

        private static readonly object Gate = new object();
        private static readonly HashSet<string> Loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private IntPtr _module;
        private readonly string _modulePath;
        private readonly bool _copy;
        private readonly LogMessage _logger;
        private readonly EnterInitializationMode _enterInit;
        private readonly Simple _exitInit, _terminate;
        private readonly FreeInstance _freeInstance;
        private readonly DoStep _doStep;
        private readonly Dictionary<string, Access> _access = new Dictionary<string, Access>(StringComparer.Ordinal);
        private IntPtr _instance;
        private bool _initialised;
        private bool _terminated;

        public event Action<string>? Log;

        public List<string> Messages { get; } = new List<string>();

        public FmuDescription Description { get; }

        public double Time { get; private set; }

        public Fmu3Instance(FmuArchive fmu, string instanceName)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IntPtr.Size != 8)
                throw new PlatformNotSupportedException("FMU'nun x86_64-windows ikilisi yalnız 64 bit Windows'ta çalışır.");
            Description = fmu.Description;
            if (Description.CoSimulationId.Length == 0)
                throw new NotSupportedException($"{Description.ModelName}: FMI 3.0 model değişimi FMU'su henüz çalıştırılmıyor (yalnız ortak benzetim).");
            string dll = fmu.BinaryPath ?? throw new InvalidOperationException($"{Description.ModelName}: FMU'da x86_64-windows ikilisi yok (binaries/x86_64-windows/{Description.BinaryId}.dll).");
            lock (Gate)
            {
                _modulePath = dll;
                if (Loaded.Contains(dll))
                {
                    // an FMU's globals are not always per instance: a second one gets its own copy of the binary
                    _modulePath = Path.Combine(Path.GetDirectoryName(dll)!, Path.GetFileNameWithoutExtension(dll) + ".i" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".dll");
                    File.Copy(dll, _modulePath, true);
                    _copy = true;
                }

                _module = LoadLibraryW(_modulePath);
                if (_module == IntPtr.Zero) throw new InvalidOperationException($"FMU ikilisi yüklenemedi: '{_modulePath}' (Win32 hata kodu {Marshal.GetLastWin32Error()}).");
                Loaded.Add(_modulePath);
            }

            var instantiate = Bind<InstantiateCoSimulation>("fmi3InstantiateCoSimulation");
            _enterInit = Bind<EnterInitializationMode>("fmi3EnterInitializationMode");
            _exitInit = Bind<Simple>("fmi3ExitInitializationMode");
            _terminate = Bind<Simple>("fmi3Terminate");
            _freeInstance = Bind<FreeInstance>("fmi3FreeInstance");
            _doStep = Bind<DoStep>("fmi3DoStep");
            _logger = (env, status, category, message) =>
            {
                string line = $"[{(status switch { 0 => "ok", 1 => "uyarı", 2 => "atıldı", 3 => "hata", 4 => "ölümcül", _ => status.ToString(System.Globalization.CultureInfo.InvariantCulture) })}] {Marshal.PtrToStringAnsi(category)}: {Marshal.PtrToStringAnsi(message)}";
                Messages.Add(line);
                if (Messages.Count > 200) Messages.RemoveAt(0);
                Log?.Invoke(line);
            };
            // FMI 3.0 gives the resources as a native path with its separator at the end (2.0 used a file URI).
            string resources = Path.Combine(fmu.Folder, "resources") + Path.DirectorySeparatorChar;
            _instance = instantiate(instanceName, Description.Guid, resources, 0, 0, 0, 0, IntPtr.Zero, UIntPtr.Zero, IntPtr.Zero,
                Marshal.GetFunctionPointerForDelegate(_logger), IntPtr.Zero);
            if (_instance == IntPtr.Zero)
            {
                Dispose();
                throw new InvalidOperationException($"{Description.ModelName}: fmi3InstantiateCoSimulation başarısız (örnekleme belirteci ya da kaynaklar uyuşmuyor olabilir).");
            }
        }

        private T Bind<T>(string name) where T : Delegate
        {
            IntPtr p = GetProcAddress(_module, name);
            if (p == IntPtr.Zero) throw new InvalidOperationException($"FMU ikilisinde {name} yok (FMI 3.0 ortak benzetim arabirimi eksik).");
            return Marshal.GetDelegateForFunctionPointer<T>(p);
        }

        public void Initialise(double startTime, IReadOnlyDictionary<string, double>? values = null)
        {
            Check(_enterInit(_instance, 0, 0, startTime, 0, 0), "fmi3EnterInitializationMode");
            if (values != null)
                foreach (var kv in values)
                    if (Description.Find(kv.Key) is { } v) Set(v, kv.Value);
            Check(_exitInit(_instance), "fmi3ExitInitializationMode");
            Time = startTime;
            _initialised = true;
        }

        /// <summary>A variable's type as FMI 3.0 names its functions (Float64 when the description did not say).</summary>
        private static string TypeOf(FmuVariable v) =>
            v.Fmi3Type.Length > 0 ? (v.Fmi3Type == "Enumeration" ? "Int64" : v.Fmi3Type) : v.Type switch
            {
                FmuType.Boolean => "Boolean",
                FmuType.Integer => "Int32",
                FmuType.Enumeration => "Int64",
                _ => "Float64"
            };

        private static int SizeOf(string type) => type switch
        {
            "Float64" or "Int64" or "UInt64" => 8,
            "Float32" or "Int32" or "UInt32" => 4,
            "Int16" or "UInt16" => 2,
            _ => 1   // Int8, UInt8, Boolean (C's bool)
        };

        private Access AccessFor(string verb, string type)
        {
            string name = "fmi3" + verb + type;
            if (!_access.TryGetValue(name, out var f))
            {
                f = Bind<Access>(name);
                _access[name] = f;
            }

            return f;
        }

        public void Set(FmuVariable v, double value)
        {
            string type = TypeOf(v);
            if (v.Type == FmuType.String) return;
            IntPtr buffer = Marshal.AllocHGlobal(8);
            try
            {
                switch (type)
                {
                    case "Float64": Marshal.WriteInt64(buffer, BitConverter.DoubleToInt64Bits(value)); break;
                    case "Float32": Marshal.WriteInt32(buffer, BitConverter.ToInt32(BitConverter.GetBytes((float)value), 0)); break;
                    case "Int64": Marshal.WriteInt64(buffer, (long)Math.Round(value)); break;
                    case "UInt64": Marshal.WriteInt64(buffer, unchecked((long)(ulong)Math.Max(0, Math.Round(value)))); break;
                    case "Int32": Marshal.WriteInt32(buffer, (int)Math.Round(value)); break;
                    case "UInt32": Marshal.WriteInt32(buffer, unchecked((int)(uint)Math.Max(0, Math.Round(value)))); break;
                    case "Int16": Marshal.WriteInt16(buffer, (short)Math.Round(value)); break;
                    case "UInt16": Marshal.WriteInt16(buffer, unchecked((short)(ushort)Math.Max(0, Math.Round(value)))); break;
                    case "Boolean": Marshal.WriteByte(buffer, value != 0 ? (byte)1 : (byte)0); break;
                    default: Marshal.WriteByte(buffer, unchecked((byte)(sbyte)Math.Round(value))); break;
                }

                Check(AccessFor("Set", type)(_instance, new[] { v.ValueReference }, (UIntPtr)1, buffer, (UIntPtr)1), "fmi3Set" + type + " " + v.Name);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public double Get(FmuVariable v)
        {
            string type = TypeOf(v);
            if (v.Type == FmuType.String) return double.NaN;
            IntPtr buffer = Marshal.AllocHGlobal(8);
            try
            {
                Check(AccessFor("Get", type)(_instance, new[] { v.ValueReference }, (UIntPtr)1, buffer, (UIntPtr)1), "fmi3Get" + type + " " + v.Name);
                return type switch
                {
                    "Float64" => BitConverter.Int64BitsToDouble(Marshal.ReadInt64(buffer)),
                    "Float32" => BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(buffer)), 0),
                    "Int64" => Marshal.ReadInt64(buffer),
                    "UInt64" => unchecked((ulong)Marshal.ReadInt64(buffer)),
                    "Int32" => Marshal.ReadInt32(buffer),
                    "UInt32" => unchecked((uint)Marshal.ReadInt32(buffer)),
                    "Int16" => Marshal.ReadInt16(buffer),
                    "UInt16" => unchecked((ushort)Marshal.ReadInt16(buffer)),
                    "Boolean" => Marshal.ReadByte(buffer) != 0 ? 1 : 0,
                    "Int8" => unchecked((sbyte)Marshal.ReadByte(buffer)),
                    _ => Marshal.ReadByte(buffer)
                };
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public void Step(double h)
        {
            if (!_initialised) throw new InvalidOperationException("FMU önce Initialise ile başlatılmalı.");
            if (_terminated) return;
            Check(_doStep(_instance, Time, h, 1, out _, out byte terminate, out byte early, out double last), "fmi3DoStep");
            // Without early return allowed the step is whole; an FMU that asks to end the simulation stays where it is.
            Time = early != 0 ? last : Time + h;
            if (terminate != 0) _terminated = true;
        }

        private void Check(int status, string what)
        {
            // fmi3OK = 0, fmi3Warning = 1: going on; discard, error and fatal stop the co-simulation.
            if (status <= 1) return;
            throw new InvalidOperationException($"{Description.ModelName}: {what} {(status == 2 ? "atıldı" : status == 3 ? "hata" : status == 4 ? "ölümcül hata" : "?")} ({status}).");
        }

        ~Fmu3Instance() => Dispose();

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            if (_instance != IntPtr.Zero)
            {
                try
                {
                    if (_initialised) _terminate(_instance);
                    _freeInstance(_instance);
                }
                catch (SEHException)
                {
                }

                _instance = IntPtr.Zero;
            }

            lock (Gate)
            {
                if (_module != IntPtr.Zero)
                {
                    FreeLibrary(_module);
                    Loaded.Remove(_modulePath);
                    _module = IntPtr.Zero;
                }

                if (_copy)
                {
                    try
                    {
                        File.Delete(_modulePath);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                    }
                }
            }

            GC.KeepAlive(_logger);
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr module);
    }
}
