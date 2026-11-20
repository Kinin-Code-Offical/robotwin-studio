using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;

namespace RobotTwin.CoreSim.Firmware.Esp32
{
    public sealed class Esp32Options
    {
        /// <summary>Time from reset to setup() (ROM + 2nd stage bootloader + app start), seconds.</summary>
        public double BootDelay { get; set; } = 0.3;

        /// <summary>Wall-clock time a sketch task may run without any API call before it is reported hung.</summary>
        public int WatchdogMilliseconds { get; set; } = 2000;

        /// <summary>Seed of esp_random()/random() (the simulation is deterministic by default).</summary>
        public ulong RandomSeed { get; set; } = 0x5EED5EEDUL;

        /// <summary>Typical ESP32 ADC non-linearity (dead zone near 0 V, compression near full scale) instead of an ideal ADC.</summary>
        public bool RealisticAdc { get; set; }

        /// <summary>Multiplier for the per-call execution time charged to the sketch.</summary>
        public double CostScale { get; set; } = 1.0;

        /// <summary>Print the ROM boot banner ("rst:0x1 (POWERON_RESET)...") on every reset.</summary>
        public bool BootMessages { get; set; } = true;

        /// <summary>Copy core/ESP-IDF error and warning logs into the serial output (as on a real board).</summary>
        public bool EchoLogsToSerial { get; set; } = true;

        /// <summary>Chip current while running; NaN uses the board part's "idle" parameter.</summary>
        public double RunCurrent { get; set; } = double.NaN;

        public double WifiExtraCurrent { get; set; } = 0.05;
        public double ResetCurrent { get; set; } = 0.002;
        public double DeepSleepCurrent { get; set; } = 10e-6;

        /// <summary>Replay a single output edge or pulse inside a slice at its exact time (one-shot PWM) instead of at the slice boundary.</summary>
        public bool ExactOutputEdges { get; set; } = true;
    }

    public enum Esp32BoardState
    {
        /// <summary>No supply, brown-out or EN held low.</summary>
        Off,
        Running,
        DeepSleep
    }

    /// <summary>Snapshot of one GPIO as the sketch configured it.</summary>
    public readonly struct Esp32PinState
    {
        public Esp32PinState(int gpio, int mode, bool output, bool input, bool pwm, double frequency, double duty, bool dac, double dacVolts)
        {
            Gpio = gpio;
            Mode = mode;
            OutputLevel = output;
            InputLevel = input;
            PwmAttached = pwm;
            PwmFrequency = frequency;
            PwmDuty = duty;
            DacEnabled = dac;
            DacVoltage = dacVolts;
        }

        public int Gpio { get; }

        /// <summary>Arduino-ESP32 pinMode value (INPUT 0x01, OUTPUT 0x03, INPUT_PULLUP 0x05, ...); 0 = never configured.</summary>
        public int Mode { get; }

        public bool OutputLevel { get; }
        public bool InputLevel { get; }
        public bool PwmAttached { get; }
        public double PwmFrequency { get; }
        public double PwmDuty { get; }
        public bool DacEnabled { get; }
        public double DacVoltage { get; }
    }

    /// <summary>
    /// Runs a natively compiled ESP32 Arduino sketch (see <see cref="Esp32SketchCompiler"/>) in
    /// lockstep with the analog circuit, at HAL (Arduino API) level. Same usage as
    /// <see cref="FirmwareCoSimulation"/>: call <see cref="Advance"/> with the slice, then step
    /// the circuit by the same amount. Inputs are sampled at the slice start with ESP32
    /// thresholds (VIL 0.25·VDD, VIH 0.75·VDD, Schmitt), the sketch runs until its simulated
    /// time reaches the end of the slice, and its outputs (push-pull/open-drain levels,
    /// 45 kΩ pull-ups/downs, LEDC PWM, DAC) are applied to the board's pin devices.
    /// </summary>
    public sealed class Esp32CoSimulation : IDisposable
    {
        public const double BrownOutVoltage = 2.43;
        public const double BrownOutReleaseVoltage = 2.55;
        private const double OneShotPeriod = 1.0;

        private const int ModeInputBit = 0x01;
        private const int ModeOutputBit = 0x02;
        private const int ModePullup = 0x04;
        private const int ModePulldown = 0x08;
        private const int ModeOpenDrain = 0x10;

        private sealed class Gpio
        {
            public DigitalPin? Pin;
            public Esp32PadAux? Aux;
            public int Mode;
            public bool Out;
            public bool Pwm;
            public double PwmFrequency;
            public double PwmDuty;
            public double PwmStart;
            public bool Dac;
            public double DacVolts;
            public bool InLevel;
            public bool InKnown;
            public int Touch = 75;
            public double AppliedFrequency = double.NaN;
            public double AppliedDuty = double.NaN;
            public bool OneShot;
            public bool AppliedOutLevel;
            public bool SliceStartOut;
            public readonly List<(double Time, bool Level)> Transitions = new List<(double, bool)>();
        }

        private readonly CompiledCircuit _circuit;
        private readonly Func<double>? _supply;
        private readonly Esp32ChipLoad? _chip;
        private readonly Gpio[] _gpio = new Gpio[Esp32Gpio.Count];
        private readonly double _runCurrent;

        private readonly StringBuilder[] _serialText = { new StringBuilder(), new StringBuilder(), new StringBuilder() };
        private readonly Decoder[] _decoders = { Encoding.UTF8.GetDecoder(), Encoding.UTF8.GetDecoder(), Encoding.UTF8.GetDecoder() };
        private readonly Queue<byte>[] _rx = { new Queue<byte>(), new Queue<byte>(), new Queue<byte>() };
        private readonly double[] _rxNext = new double[3];
        private readonly uint[] _baud = { 115200, 115200, 115200 };
        private readonly List<(int Gpio, bool Level, double Time)> _edges = new List<(int, bool, double)>();
        private readonly List<string> _logLines = new List<string>();

        // Delegates handed to native code: kept in fields so the GC cannot collect them.
        private readonly Esp32Abi.LogFn _log;
        private readonly Esp32Abi.PinModeFn _pinMode;
        private readonly Esp32Abi.DigitalWriteFn _digitalWrite;
        private readonly Esp32Abi.DigitalReadFn _digitalRead;
        private readonly Esp32Abi.AnalogReadFn _analogRead;
        private readonly Esp32Abi.PwmWriteFn _pwmWrite;
        private readonly Esp32Abi.DacWriteFn _dacWrite;
        private readonly Esp32Abi.TouchReadFn _touchRead;
        private readonly Esp32Abi.SerialBeginFn _serialBegin;
        private readonly Esp32Abi.SerialWriteFn _serialWrite;
        private readonly Esp32Abi.I2cStartFn _i2cStart;
        private readonly Esp32Abi.I2cWriteFn _i2cWrite;
        private readonly Esp32Abi.I2cReadFn _i2cRead;
        private readonly Esp32Abi.I2cStopFn _i2cStop;
        private readonly Esp32Abi.SystemEventFn _systemEvent;
        private readonly Esp32Abi.RadioRequestFn _radioRequest;
        private readonly byte[] _radioIn = new byte[8192];
        private readonly byte[] _radioOut = new byte[8192];
        private Esp32Abi.HostApi _api;

        private Esp32SketchModule? _module;
        private double _vdd = 3.3;
        private bool _enHigh = true;
        private bool _resetRequested;
        private bool _pendingRestart;
        private bool _pendingSleep;
        private long _pendingSleepUs = -1;
        private int _ext0Gpio = -1;
        private bool _ext0Level;
        private double _wakeAt = double.NaN;
        private bool _wifiOn;
        private bool _everRan;
        private double _minVddWhileOff = double.PositiveInfinity;
        private double _lastEnd;
        private bool _disposed;

        public Esp32CoSimulation(string dllPath, CompiledCircuit circuit, string boardRefDes, Func<double>? supplyVoltage = null,
            Esp32Options? options = null)
        {
            DllPath = dllPath ?? throw new ArgumentNullException(nameof(dllPath));
            _circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
            BoardRefDes = boardRefDes ?? throw new ArgumentNullException(nameof(boardRefDes));
            _supply = supplyVoltage;
            Options = options ?? new Esp32Options();

            _chip = circuit.Device<Esp32ChipLoad>(boardRefDes + ".CHIP");
            _runCurrent = !double.IsNaN(Options.RunCurrent) ? Options.RunCurrent : _chip?.Current ?? 0.08;
            for (int g = 0; g < _gpio.Length; g++)
            {
                var s = new Gpio();
                string? name = Esp32Gpio.PinName(g);
                if (name != null && !Esp32Gpio.IsFlashPin(g))
                {
                    s.Pin = circuit.Device<DigitalPin>(boardRefDes + "." + name);
                    s.Aux = circuit.Device<Esp32PadAux>(boardRefDes + "." + name + ".AUX");
                }

                _gpio[g] = s;
            }

            _log = OnLog;
            _pinMode = OnPinMode;
            _digitalWrite = OnDigitalWrite;
            _digitalRead = OnDigitalRead;
            _analogRead = OnAnalogRead;
            _pwmWrite = OnPwmWrite;
            _dacWrite = OnDacWrite;
            _touchRead = OnTouchRead;
            _serialBegin = OnSerialBegin;
            _serialWrite = OnSerialWrite;
            _i2cStart = OnI2cStart;
            _i2cWrite = OnI2cWrite;
            _i2cRead = OnI2cRead;
            _i2cStop = OnI2cStop;
            _systemEvent = OnSystemEvent;
            _radioRequest = OnRadioRequest;
            _api = new Esp32Abi.HostApi
            {
                AbiVersion = Esp32Abi.Version,
                StructSize = (uint)Marshal.SizeOf<Esp32Abi.HostApi>(),
                User = IntPtr.Zero,
                Log = Marshal.GetFunctionPointerForDelegate(_log),
                PinMode = Marshal.GetFunctionPointerForDelegate(_pinMode),
                DigitalWrite = Marshal.GetFunctionPointerForDelegate(_digitalWrite),
                DigitalRead = Marshal.GetFunctionPointerForDelegate(_digitalRead),
                AnalogRead = Marshal.GetFunctionPointerForDelegate(_analogRead),
                PwmWrite = Marshal.GetFunctionPointerForDelegate(_pwmWrite),
                DacWrite = Marshal.GetFunctionPointerForDelegate(_dacWrite),
                TouchRead = Marshal.GetFunctionPointerForDelegate(_touchRead),
                SerialBegin = Marshal.GetFunctionPointerForDelegate(_serialBegin),
                SerialWrite = Marshal.GetFunctionPointerForDelegate(_serialWrite),
                I2cStart = Marshal.GetFunctionPointerForDelegate(_i2cStart),
                I2cWrite = Marshal.GetFunctionPointerForDelegate(_i2cWrite),
                I2cRead = Marshal.GetFunctionPointerForDelegate(_i2cRead),
                I2cStop = Marshal.GetFunctionPointerForDelegate(_i2cStop),
                SystemEvent = Marshal.GetFunctionPointerForDelegate(_systemEvent),
                RadioRequest = Marshal.GetFunctionPointerForDelegate(_radioRequest)
            };
        }

        /// <summary>Version of the host ABI (rw_host_api.h RW_ABI_VERSION) sketch DLLs must be built for.</summary>
        public static int AbiVersion => Esp32Abi.Version;

        /// <summary>
        /// Wi-Fi / BLE / game-controller medium (null: the HAL uses its stand-alone stub: Wi-Fi
        /// "connects" after ~1 s, BLE and Bluepad32 see nothing).
        /// </summary>
        public IEsp32Radio? Radio { get; set; }

        public string DllPath { get; }
        public string BoardRefDes { get; }
        public Esp32Options Options { get; }
        public Esp32BoardState State { get; private set; } = Esp32BoardState.Off;
        public bool InBrownOut { get; private set; }

        /// <summary>Number of times the sketch was (re)started: power-on, EN, brown-out, watchdog, panic, restart, deep-sleep wake.</summary>
        public int BootCount { get; private set; }

        /// <summary>esp_reset_reason() of the current run (1 power-on, 3 software, 4 panic, 6 task watchdog, 8 deep sleep, 9 brown-out).</summary>
        public int ResetReason { get; private set; }

        /// <summary>Watchdog / panic report of the last crash, or null.</summary>
        public string? LastCrashMessage { get; private set; }

        /// <summary>Set when the sketch reported a hung task (watchdog); cleared on the next boot.</summary>
        public int WatchdogResets { get; private set; }

        public bool WifiOn => _wifiOn;

        /// <summary>I²C bus driven by Wire (SDA 21 / SCL 22 by default).</summary>
        public I2cBus I2c { get; } = new I2cBus();

        /// <summary>I²C bus driven by Wire1.</summary>
        public I2cBus I2c1 { get; } = new I2cBus();

        /// <summary>Text printed on UART0 (Serial, printf, core logs, boot/crash messages) during the last slice.</summary>
        public event Action<string>? SerialOutput;

        /// <summary>Text per UART (0 = Serial, 1 = Serial1, 2 = Serial2) during the last slice.</summary>
        public event Action<int, string>? SerialPortOutput;

        /// <summary>Simulator notes: core logs, resets, watchdog and crash reports.</summary>
        public event Action<string>? Log;

        public double SimulatedTime => _module != null && State == Esp32BoardState.Running ? _module.Now() * 1e-9 : _lastEnd;

        public Esp32PinState GetPin(int gpio)
        {
            if (gpio < 0 || gpio >= _gpio.Length) throw new ArgumentOutOfRangeException(nameof(gpio));
            var s = _gpio[gpio];
            return new Esp32PinState(gpio, s.Mode, s.Out, s.InLevel, s.Pwm, s.PwmFrequency, s.PwmDuty, s.Dac, s.DacVolts);
        }

        /// <summary>Value returned by touchRead() for a touch pad (untouched ≈ 70-80, touched &lt; 20).</summary>
        public void SetTouchValue(int gpio, int value)
        {
            if (gpio < 0 || gpio >= _gpio.Length) throw new ArgumentOutOfRangeException(nameof(gpio));
            _gpio[gpio].Touch = value;
        }

        /// <summary>Queues text for a UART's RX line; it arrives at the configured baud rate.</summary>
        public void SendSerial(string text, int port = 0)
        {
            if (string.IsNullOrEmpty(text) || port < 0 || port > 2) return;
            foreach (byte b in Encoding.UTF8.GetBytes(text)) _rx[port].Enqueue(b);
        }

        /// <summary>Presses the EN (reset) button: the sketch restarts at the next <see cref="Advance"/>.</summary>
        public void Reset() => _resetRequested = true;

        /// <summary>
        /// Optional: wire to <see cref="Analog.AnalogSimulator.SubstepAccepted"/> to time-stamp input
        /// edges at the solver's resolution (exact pulseIn() widths, all edges of short pulses).
        /// </summary>
        public void OnSubstep(double time)
        {
            if (State == Esp32BoardState.Running) SampleInputs(time);
        }

        /// <summary>Runs the sketch for <paramref name="dt"/> seconds; the caller then steps the circuit by the same amount.</summary>
        public void Advance(double dt, double now)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Esp32CoSimulation));
            double end = now + dt;
            EndOneShots();

            double vdd = _supply != null ? _supply() : _chip != null ? _chip.SupplyVoltage : 3.3;
            if (double.IsNaN(vdd)) vdd = 0.0;
            double en = _chip != null && !double.IsNaN(_chip.EnableVoltage) ? _chip.EnableVoltage : vdd;
            _vdd = vdd > 0.5 ? vdd : 3.3;
            bool powerOk = InBrownOut ? vdd >= BrownOutReleaseVoltage : vdd >= BrownOutVoltage;
            if (State == Esp32BoardState.Off && !InBrownOut && !_everRan) powerOk = vdd >= BrownOutReleaseVoltage;
            _enHigh = _enHigh ? en > 0.25 * vdd : en > 0.75 * vdd;

            if (State == Esp32BoardState.Off) _minVddWhileOff = Math.Min(_minVddWhileOff, vdd);
            if (!powerOk || !_enHigh)
            {
                if (State != Esp32BoardState.Off)
                {
                    if (!powerOk && State == Esp32BoardState.Running && vdd > 1.8)
                    {
                        Emit(0, "\r\nBrownout detector was triggered\r\n\r\n");
                    }

                    Note(!powerOk ? $"brown-out at {vdd:F2} V" : "EN held low (reset)");
                    Shutdown();
                    State = Esp32BoardState.Off;
                    _minVddWhileOff = vdd;
                }

                InBrownOut = !powerOk;
            }
            else if (State == Esp32BoardState.Off)
            {
                // A dip that the brown-out detector caught is a brown-out reset; a real power cycle is power-on.
                Boot(now, InBrownOut && _minVddWhileOff > 1.0 ? 9 : 1);
                InBrownOut = false;
            }
            else if (_resetRequested && State != Esp32BoardState.Off)
            {
                Boot(now, 1);
            }
            else if (State == Esp32BoardState.DeepSleep)
            {
                SampleInputs(now);
                _edges.Clear();
                bool timer = !double.IsNaN(_wakeAt) && now >= _wakeAt;
                bool ext0 = _ext0Gpio >= 0 && _gpio[_ext0Gpio].InLevel == _ext0Level;
                if (timer || ext0) Boot(now, 8, timer ? 4 : 2);
            }

            _resetRequested = false;

            if (State == Esp32BoardState.Running && _module != null)
            {
                SampleInputs(now);
                foreach (var e in _edges) _module.PinChanged(e.Gpio, e.Level ? 1 : 0, Ns(e.Time));
                _edges.Clear();
                DeliverSerial(now, end);

                int status = _module.RunUntil(Ns(end));
                if (status == Esp32Abi.StatusWatchdog || status == Esp32Abi.StatusCrashed)
                {
                    string msg = _module.StatusMessage();
                    LastCrashMessage = msg;
                    if (status == Esp32Abi.StatusWatchdog) WatchdogResets++;
                    Emit(0, "\r\n" + msg + "\r\n\r\nRebooting...\r\n");
                    Note((status == Esp32Abi.StatusWatchdog ? "watchdog: " : "panic: ") + msg);
                    Boot(end, status == Esp32Abi.StatusWatchdog ? 6 : 4);
                }
                else if (_pendingRestart)
                {
                    Note("ESP.restart()");
                    Boot(end, 3);
                }
                else if (_pendingSleep)
                {
                    long us = _pendingSleepUs;
                    Note(us >= 0 ? $"deep sleep for {us} us" : "deep sleep (no timer wake-up)");
                    Shutdown();
                    State = Esp32BoardState.DeepSleep;
                    _wakeAt = us >= 0 ? end + us * 1e-6 : double.NaN;
                }
            }

            ApplyPins(now, end);
            UpdateChipCurrent();
            _lastEnd = end;
            FlushText();
        }

        // ------------------------------------------------------------------ lifecycle

        private void Boot(double time, int resetReason, int wakeupCause = 0)
        {
            Shutdown();
            foreach (var s in _gpio) ResetGpio(s);
            _pendingRestart = false;
            _pendingSleep = false;
            _pendingSleepUs = -1;
            _wakeAt = double.NaN;
            _wifiOn = false;
            ResetReason = resetReason;
            I2c.EndTransfer();
            I2c1.EndTransfer();

            if (Options.BootMessages)
            {
                Emit(0, "ets Jun  8 2016 00:22:57\r\n\r\nrst:" + RomResetText(resetReason) +
                        ",boot:0x13 (SPI_FAST_FLASH_BOOT)\r\nconfigsip: 0, SPIWP:0xee\r\nmode:DIO, clock div:1\r\nentry 0x400805e4\r\n");
            }

            _module = new Esp32SketchModule(DllPath);
            var cfg = new Esp32Abi.Config
            {
                StructSize = (uint)Marshal.SizeOf<Esp32Abi.Config>(),
                WatchdogMs = (uint)Math.Max(100, Options.WatchdogMilliseconds),
                TimeOriginNs = Ns(time),
                RandomSeed = Options.RandomSeed + (ulong)BootCount,
                AdcModel = Options.RealisticAdc ? 1 : 0,
                ResetReason = resetReason,
                WakeupCause = wakeupCause,
                CostScale = Options.CostScale > 0 ? Options.CostScale : 1.0
            };
            int rc = _module.Init(ref _api, ref cfg);
            if (rc != 0)
            {
                _module.Dispose();
                _module = null;
                throw new InvalidOperationException("rw_init failed (" + rc.ToString(CultureInfo.InvariantCulture) + ").");
            }

            // Initial input levels (the first report of each pin is not an edge).
            SampleInputs(time);
            _edges.Clear();
            for (int g = 0; g < _gpio.Length; g++)
            {
                if (_gpio[g].Pin != null) _module.PinChanged(g, _gpio[g].InLevel ? 1 : 0, Ns(time));
            }

            rc = _module.Start(Ns(time + Math.Max(0.0, Options.BootDelay)));
            if (rc != 0) throw new InvalidOperationException("rw_start failed (" + rc.ToString(CultureInfo.InvariantCulture) + ").");
            State = Esp32BoardState.Running;
            BootCount++;
            _everRan = true;
            LastBootTime = time;
        }

        /// <summary>Simulation time of the last (re)boot.</summary>
        public double LastBootTime { get; private set; } = double.NaN;

        private static string RomResetText(int reason)
        {
            switch (reason)
            {
                case 3: case 4: case 6: return "0xc (SW_CPU_RESET)";
                case 8: return "0x5 (DEEPSLEEP_RESET)";
                case 9: return "0xf (RTCWDT_BROWN_OUT_RESET)";
                default: return "0x1 (POWERON_RESET)";
            }
        }

        private void Shutdown()
        {
            if (_module != null)
            {
                _module.Stop();
                _module.Dispose();
                _module = null;
            }

            foreach (var s in _gpio)
            {
                ResetGpio(s);
                ReleasePin(s);
            }

            _wifiOn = false;
            State = Esp32BoardState.Off;
        }

        private static void ResetGpio(Gpio s)
        {
            s.Mode = 0;
            s.Out = false;
            s.Pwm = false;
            s.Dac = false;
            s.Transitions.Clear();
            s.SliceStartOut = false;
        }

        private static void ReleasePin(Gpio s)
        {
            if (s.Pin == null) return;
            if (!double.IsNaN(s.AppliedDuty) || s.OneShot) s.Pin.StopPwm();
            s.AppliedDuty = double.NaN;
            s.AppliedFrequency = double.NaN;
            s.OneShot = false;
            s.Pin.Mode = PinMode.Input;
            if (s.Aux != null)
            {
                s.Aux.Pulldown = false;
                s.Aux.DacEnabled = false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Shutdown();
            _disposed = true;
        }

        // ------------------------------------------------------------------ inputs / outputs

        private static ulong Ns(double seconds) => seconds <= 0.0 ? 0UL : (ulong)Math.Round(seconds * 1e9);

        private void SampleInputs(double time)
        {
            double vdd = _vdd;
            for (int g = 0; g < _gpio.Length; g++)
            {
                var s = _gpio[g];
                if (s.Pin == null) continue;
                double v = s.Pin.Voltage;
                if (!s.InKnown)
                {
                    s.InLevel = v > 0.5 * vdd;
                    s.InKnown = true;
                    continue;
                }

                bool level = s.InLevel;
                if (level && v < 0.25 * vdd) level = false;
                else if (!level && v > 0.75 * vdd) level = true;
                if (level == s.InLevel) continue;
                s.InLevel = level;
                _edges.Add((g, level, time));
            }
        }

        private void DeliverSerial(double now, double end)
        {
            for (int port = 0; port < 3; port++)
            {
                var q = _rx[port];
                if (q.Count == 0)
                {
                    _rxNext[port] = Math.Max(_rxNext[port], now);
                    continue;
                }

                double byteTime = 10.0 / Math.Max(300u, _baud[port]);
                if (_rxNext[port] < now) _rxNext[port] = now;
                var chunk = new List<byte>();
                while (q.Count > 0 && _rxNext[port] <= end)
                {
                    chunk.Add(q.Dequeue());
                    _rxNext[port] += byteTime;
                }

                if (chunk.Count > 0 && _module != null) _module.SerialRx(port, chunk.ToArray(), chunk.Count);
            }
        }

        private void EndOneShots()
        {
            foreach (var s in _gpio)
            {
                if (!s.OneShot || s.Pin == null) continue;
                s.Pin.StopPwm();
                s.OneShot = false;
                s.Pin.Mode = s.AppliedOutLevel ? PinMode.OutputHigh : PinMode.OutputLow;
            }
        }

        private void ApplyPins(double now, double end)
        {
            bool running = State == Esp32BoardState.Running;
            foreach (var s in _gpio)
            {
                if (s.Pin == null)
                {
                    s.Transitions.Clear();
                    continue;
                }

                if (!running)
                {
                    ReleasePin(s);
                    s.Transitions.Clear();
                    continue;
                }

                var pin = s.Pin;
                if (s.Dac)
                {
                    StopPwm(s);
                    pin.Mode = PinMode.Input;
                    if (s.Aux != null)
                    {
                        s.Aux.Pulldown = false;
                        s.Aux.DacVoltage = s.DacVolts * _vdd / 3.3;
                        s.Aux.DacEnabled = true;
                    }
                }
                else if (s.Pwm)
                {
                    if (s.Aux != null)
                    {
                        s.Aux.DacEnabled = false;
                        s.Aux.Pulldown = false;
                    }

                    if (s.PwmDuty <= 0.0 || s.PwmFrequency <= 0.0)
                    {
                        StopPwm(s);
                        pin.Mode = PinMode.OutputLow;
                    }
                    else if (s.PwmDuty >= 1.0)
                    {
                        StopPwm(s);
                        pin.Mode = PinMode.OutputHigh;
                    }
                    else if (double.IsNaN(s.AppliedDuty) || Math.Abs(s.AppliedDuty - s.PwmDuty) > 1e-12 || Math.Abs(s.AppliedFrequency - s.PwmFrequency) > 1e-9)
                    {
                        pin.Mode = PinMode.OutputLow;
                        pin.SetPwm(s.PwmFrequency, s.PwmDuty, s.PwmStart);
                        s.AppliedDuty = s.PwmDuty;
                        s.AppliedFrequency = s.PwmFrequency;
                    }
                }
                else
                {
                    StopPwm(s);
                    if (s.Aux != null) s.Aux.DacEnabled = false;
                    ApplyDigital(s, now, end);
                }

                s.Transitions.Clear();
                s.SliceStartOut = s.Out;
            }
        }

        private void ApplyDigital(Gpio s, double now, double end)
        {
            var pin = s.Pin!;
            int mode = s.Mode;
            bool pulldown = false;
            if (mode == 0)
            {
                pin.Mode = PinMode.Input;
            }
            else if ((mode & ModeOutputBit) != 0 && (mode & ModeOpenDrain) != 0)
            {
                if (!s.Out) pin.Mode = PinMode.OutputLow;
                else
                {
                    pin.Mode = (mode & ModePullup) != 0 ? PinMode.InputPullup : PinMode.Input;
                    pulldown = (mode & ModePulldown) != 0;
                }
            }
            else if ((mode & ModeOutputBit) != 0)
            {
                pin.Mode = s.Out ? PinMode.OutputHigh : PinMode.OutputLow;
                if (Options.ExactOutputEdges) ReplayEdges(s, s.SliceStartOut, now, end);
                s.AppliedOutLevel = s.Out;
            }
            else
            {
                pin.Mode = (mode & ModePullup) != 0 ? PinMode.InputPullup : PinMode.Input;
                pulldown = (mode & ModePulldown) != 0;
            }

            if (s.Aux != null) s.Aux.Pulldown = pulldown;
        }

        /// <summary>
        /// A single edge or a single pulse written during the slice is replayed at its exact
        /// time with a one-shot PWM on the pin (stopped at the next slice), so short trigger
        /// pulses and edge timing survive the slice granularity.
        /// </summary>
        private void ReplayEdges(Gpio s, bool level0, double now, double end)
        {
            var pin = s.Pin!;
            var edges = new List<(double Time, bool Level)>();
            bool level = level0;
            foreach (var t in s.Transitions)
            {
                if (t.Level == level) continue;
                level = t.Level;
                edges.Add((Math.Min(Math.Max(t.Time, now), end), t.Level));
            }

            if (edges.Count == 1)
            {
                double te = edges[0].Time;
                if (te <= now + 1e-12) return;
                bool rising = edges[0].Level;
                pin.SetPwm(1.0 / OneShotPeriod, 0.5, rising ? te : te - 0.5 * OneShotPeriod);
                s.OneShot = true;
            }
            else if (edges.Count == 2)
            {
                double t1 = edges[0].Time, t2 = edges[1].Time;
                double width = t2 - t1;
                if (width < 1e-9 || t1 <= now + 1e-12) return;
                if (edges[0].Level) pin.SetPwm(1.0 / OneShotPeriod, width / OneShotPeriod, t1);
                else pin.SetPwm(1.0 / OneShotPeriod, 1.0 - width / OneShotPeriod, t2);
                s.OneShot = true;
            }
        }

        private static void StopPwm(Gpio s)
        {
            if (double.IsNaN(s.AppliedDuty)) return;
            s.Pin?.StopPwm();
            s.AppliedDuty = double.NaN;
            s.AppliedFrequency = double.NaN;
        }

        private void UpdateChipCurrent()
        {
            if (_chip == null) return;
            switch (State)
            {
                case Esp32BoardState.Running:
                    _chip.Current = _runCurrent + (_wifiOn ? Options.WifiExtraCurrent : 0.0);
                    break;
                case Esp32BoardState.DeepSleep:
                    _chip.Current = Options.DeepSleepCurrent;
                    break;
                default:
                    _chip.Current = Options.ResetCurrent;
                    break;
            }
        }

        // ------------------------------------------------------------------ text

        private void Emit(int port, string text)
        {
            if (port >= 0 && port < 3) _serialText[port].Append(text);
        }

        private void Note(string text)
        {
            _logLines.Add(text);
        }

        private void FlushText()
        {
            for (int port = 0; port < 3; port++)
            {
                if (_serialText[port].Length == 0) continue;
                string text = _serialText[port].ToString();
                _serialText[port].Clear();
                if (port == 0) SerialOutput?.Invoke(text);
                SerialPortOutput?.Invoke(port, text);
            }

            if (_logLines.Count > 0)
            {
                var lines = _logLines.ToArray();
                _logLines.Clear();
                foreach (var line in lines) Log?.Invoke(line);
            }
        }

        // ------------------------------------------------------------------ callbacks (sketch threads, host blocked in rw_run_until)

        private double NativeNow() => _module != null ? _module.Now() * 1e-9 : _lastEnd;

        private void OnLog(IntPtr user, int level, IntPtr message)
        {
            try
            {
                string text = Marshal.PtrToStringUTF8(message) ?? string.Empty;
                if (level <= Esp32Abi.LogWarn && Options.EchoLogsToSerial) Emit(0, text + "\r\n");
                Note(text);
            }
            catch (Exception)
            {
                // Never let an exception cross into native code.
            }
        }

        private void OnPinMode(IntPtr user, int gpio, int mode)
        {
            if (gpio < 0 || gpio >= _gpio.Length) return;
            var s = _gpio[gpio];
            s.Mode = mode;
            s.Transitions.Clear();
            s.SliceStartOut = s.Out;   // edges are replayed relative to the level at the mode change
        }

        private void OnDigitalWrite(IntPtr user, int gpio, int level)
        {
            if (gpio < 0 || gpio >= _gpio.Length) return;
            var s = _gpio[gpio];
            bool v = level != 0;
            if (s.Out == v) return;
            s.Out = v;
            if (s.Pin != null && (s.Mode & ModeOutputBit) != 0 && (s.Mode & ModeOpenDrain) == 0 && !s.Pwm && !s.Dac && Options.ExactOutputEdges)
            {
                try
                {
                    s.Transitions.Add((NativeNow(), v));
                }
                catch (Exception)
                {
                }
            }
        }

        private int OnDigitalRead(IntPtr user, int gpio)
        {
            if (gpio < 0 || gpio >= _gpio.Length) return 0;
            return _gpio[gpio].InLevel ? 1 : 0;
        }

        private double OnAnalogRead(IntPtr user, int gpio)
        {
            if (gpio < 0 || gpio >= _gpio.Length) return 0.0;
            var pin = _gpio[gpio].Pin;
            if (pin == null) return 0.0;
            double v = pin.Voltage;
            return double.IsNaN(v) ? 0.0 : v;
        }

        private void OnPwmWrite(IntPtr user, int gpio, double frequency, double duty)
        {
            if (gpio < 0 || gpio >= _gpio.Length) return;
            var s = _gpio[gpio];
            s.Transitions.Clear();
            if (duty < 0.0)
            {
                s.Pwm = false;
                return;
            }

            bool changed = !s.Pwm || Math.Abs(s.PwmDuty - duty) > 1e-12 || Math.Abs(s.PwmFrequency - frequency) > 1e-9;
            s.Pwm = true;
            s.PwmFrequency = frequency;
            s.PwmDuty = duty;
            if (changed)
            {
                try { s.PwmStart = NativeNow(); } catch (Exception) { s.PwmStart = _lastEnd; }
            }
        }

        private void OnDacWrite(IntPtr user, int gpio, double volts)
        {
            if (gpio < 0 || gpio >= _gpio.Length) return;
            var s = _gpio[gpio];
            s.Dac = volts >= 0.0;
            s.DacVolts = Math.Max(0.0, volts);
        }

        private int OnTouchRead(IntPtr user, int gpio) => gpio >= 0 && gpio < _gpio.Length ? _gpio[gpio].Touch : 0;

        private void OnSerialBegin(IntPtr user, int port, uint baud)
        {
            if (port >= 0 && port < 3 && baud > 0) _baud[port] = baud;
        }

        private void OnSerialWrite(IntPtr user, int port, IntPtr data, int length)
        {
            if (port < 0 || port > 2 || data == IntPtr.Zero || length <= 0) return;
            try
            {
                var bytes = new byte[length];
                Marshal.Copy(data, bytes, 0, length);
                var chars = new char[_decoders[port].GetCharCount(bytes, 0, length)];
                int n = _decoders[port].GetChars(bytes, 0, length, chars, 0);
                _serialText[port].Append(chars, 0, n);
            }
            catch (Exception)
            {
            }
        }

        private I2cBus Bus(int bus) => bus == 1 ? I2c1 : I2c;

        private int OnI2cStart(IntPtr user, int bus, int address, int read)
        {
            try { return Bus(bus).BeginTransfer((byte)(address & 0x7F), read != 0) ? 1 : 0; }
            catch (Exception ex) { Note("I2C device error: " + ex.Message); return 0; }
        }

        private int OnI2cWrite(IntPtr user, int bus, int value)
        {
            try { return Bus(bus).WriteByte((byte)value) ? 1 : 0; }
            catch (Exception ex) { Note("I2C device error: " + ex.Message); return 0; }
        }

        private int OnI2cRead(IntPtr user, int bus, int ack)
        {
            try { return Bus(bus).ReadByte(ack != 0); }
            catch (Exception ex) { Note("I2C device error: " + ex.Message); return 0xFF; }
        }

        private void OnI2cStop(IntPtr user, int bus)
        {
            try { Bus(bus).EndTransfer(); }
            catch (Exception ex) { Note("I2C device error: " + ex.Message); }
        }

        private int OnRadioRequest(IntPtr user, int op, IntPtr input, int inputLength, IntPtr output, int outputCapacity)
        {
            var radio = Radio;
            if (radio == null) return Esp32RadioOp.NotSupported;
            try
            {
                int n = Math.Max(0, Math.Min(inputLength, _radioIn.Length));
                if (n > 0 && input != IntPtr.Zero) Marshal.Copy(input, _radioIn, 0, n);
                int cap = output == IntPtr.Zero ? 0 : Math.Max(0, Math.Min(outputCapacity, _radioOut.Length));
                Array.Clear(_radioOut, 0, cap);
                int rc = radio.Request(op, _radioIn, n, _radioOut, cap, NativeNow());
                if (cap > 0) Marshal.Copy(_radioOut, 0, output, cap);
                return rc;
            }
            catch (Exception ex)
            {
                // Never let an exception cross into native code.
                Note("radio error: " + ex.Message);
                return -2;
            }
        }

        private void OnSystemEvent(IntPtr user, int kind, long arg)
        {
            switch (kind)
            {
                case Esp32Abi.EventRestart:
                    _pendingRestart = true;
                    break;
                case Esp32Abi.EventDeepSleep:
                    _pendingSleep = true;
                    _pendingSleepUs = arg;
                    break;
                case Esp32Abi.EventWifi:
                    _wifiOn = arg != 0;
                    break;
                case Esp32Abi.EventExt0Wakeup:
                    _ext0Gpio = (int)(arg & 0xFF);
                    _ext0Level = ((arg >> 8) & 1) != 0;
                    break;
            }
        }
    }
}
