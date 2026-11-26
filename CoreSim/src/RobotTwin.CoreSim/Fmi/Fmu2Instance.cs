using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace RobotTwin.CoreSim.Fmi
{
    /// <summary>
    /// [fmi] A running FMI 2.0 co-simulation instance: the FMU's own binary (binaries/win64/&lt;id&gt;.dll) loaded through
    /// kernel32 (so it works on netstandard2.1 too), instantiated with our memory and logging callbacks, initialised with
    /// the parameters and start values, then stepped: inputs set, <c>fmi2DoStep</c>, outputs read. Each instance loads its
    /// own copy of the binary when another already has it, since an FMU's globals are not always per instance.
    /// </summary>
    public sealed partial class Fmu2Instance : IDisposable
    {
        private const int Fmi2CoSimulation = 1;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr Instantiate(string instanceName, int fmuType, string guid, string resourceLocation, IntPtr functions, int visible, int loggingOn);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SetupExperiment(IntPtr c, int toleranceDefined, double tolerance, double startTime, int stopTimeDefined, double stopTime);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Simple(IntPtr c);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FreeInstance(IntPtr c);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RealAccess(IntPtr c, uint[] vr, UIntPtr nvr, [In, Out] double[] value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int IntAccess(IntPtr c, uint[] vr, UIntPtr nvr, [In, Out] int[] value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int DoStep(IntPtr c, double currentCommunicationPoint, double communicationStepSize, int noSetFmuStatePriorToCurrentPoint);

        // fmi2CallbackLogger is variadic; on x64 the extra arguments are simply not read.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Logger(IntPtr env, IntPtr instanceName, int status, IntPtr category, IntPtr message);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr Allocate(UIntPtr count, UIntPtr size);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Free(IntPtr p);

        private static readonly object Gate = new object();
        private static readonly HashSet<string> Loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private IntPtr _module;
        private readonly string _modulePath;
        private readonly bool _copy;
        private IntPtr _callbacks;
        private readonly Logger _logger;
        private readonly Allocate _allocate;
        private readonly Free _free;
        private readonly SetupExperiment _setup;
        private readonly Simple _enterInit, _exitInit, _terminate;
        private readonly FreeInstance _freeInstance;
        private readonly RealAccess _setReal, _getReal;
        private readonly IntAccess _setInteger, _getInteger, _setBoolean, _getBoolean;
        private readonly DoStep? _doStep;   // co-simulation only
        private IntPtr _component;
        private bool _initialised;

        /// <summary>What the FMU logged (its own messages, status and category).</summary>
        public event Action<string>? Log;

        /// <summary>The FMU's messages so far (the last 200), those logged while it was being made included.</summary>
        public List<string> Messages { get; } = new List<string>();

        public FmuDescription Description { get; }

        /// <summary>The communication point reached, s.</summary>
        public double Time { get; private set; }

        public Fmu2Instance(FmuArchive fmu, string instanceName)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IntPtr.Size != 8)
                throw new PlatformNotSupportedException("FMU'nun win64 ikilisi yalnız 64 bit Windows'ta çalışır.");
            Description = fmu.Description;
            string dll = fmu.BinaryPath ?? throw new InvalidOperationException($"{fmu.Description.ModelName}: FMU'da win64 ikilisi yok (binaries/win64/{fmu.Description.BinaryId}.dll).");
            lock (Gate)
            {
                _modulePath = dll;
                if (Loaded.Contains(dll))
                {
                    _modulePath = Path.Combine(Path.GetDirectoryName(dll)!, Path.GetFileNameWithoutExtension(dll) + ".i" + System.Guid.NewGuid().ToString("N").Substring(0, 8) + ".dll");
                    File.Copy(dll, _modulePath, true);
                    _copy = true;
                }

                _module = LoadLibraryW(_modulePath);
                if (_module == IntPtr.Zero) throw new InvalidOperationException($"FMU ikilisi yüklenemedi: '{_modulePath}' (Win32 hata kodu {Marshal.GetLastWin32Error()}).");
                Loaded.Add(_modulePath);
            }

            ModelExchange = fmu.Description.UsesModelExchange;
            T Bind<T>(string name) where T : Delegate
            {
                IntPtr p = GetProcAddress(_module, name);
                if (p == IntPtr.Zero) throw new InvalidOperationException($"FMU ikilisinde {name} yok (FMI 2.0 {(ModelExchange ? "model değişimi" : "ortak benzetim")} arabirimi eksik).");
                return Marshal.GetDelegateForFunctionPointer<T>(p);
            }

            var instantiate = Bind<Instantiate>("fmi2Instantiate");
            _setup = Bind<SetupExperiment>("fmi2SetupExperiment");
            _enterInit = Bind<Simple>("fmi2EnterInitializationMode");
            _exitInit = Bind<Simple>("fmi2ExitInitializationMode");
            _terminate = Bind<Simple>("fmi2Terminate");
            _freeInstance = Bind<FreeInstance>("fmi2FreeInstance");
            _setReal = Bind<RealAccess>("fmi2SetReal");
            _getReal = Bind<RealAccess>("fmi2GetReal");
            _setInteger = Bind<IntAccess>("fmi2SetInteger");
            _getInteger = Bind<IntAccess>("fmi2GetInteger");
            _setBoolean = Bind<IntAccess>("fmi2SetBoolean");
            _getBoolean = Bind<IntAccess>("fmi2GetBoolean");
            if (ModelExchange) BindModelExchange(name => GetProcAddress(_module, name));   // [fmi-me]
            else _doStep = Bind<DoStep>("fmi2DoStep");

            // The callbacks live as long as the instance: an FMU may keep the pointer to them.
            _logger = (env, name, status, category, message) =>
            {
                string line = $"[{(status switch { 0 => "ok", 1 => "uyarı", 2 => "atıldı", 3 => "hata", 4 => "ölümcül", _ => status.ToString(System.Globalization.CultureInfo.InvariantCulture) })}] {Marshal.PtrToStringAnsi(category)}: {Marshal.PtrToStringAnsi(message)}";
                Messages.Add(line);
                if (Messages.Count > 200) Messages.RemoveAt(0);
                Log?.Invoke(line);
            };
            _allocate = (count, size) =>
            {
                ulong bytes = Math.Max(1UL, count.ToUInt64() * size.ToUInt64());
                IntPtr p = Marshal.AllocHGlobal(new IntPtr((long)bytes));
                for (ulong i = 0; i < bytes; i++) Marshal.WriteByte(p, (int)i, 0);   // calloc
                return p;
            };
            _free = p =>
            {
                if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
            };
            _callbacks = Marshal.AllocHGlobal(5 * IntPtr.Size);
            Marshal.WriteIntPtr(_callbacks, 0, Marshal.GetFunctionPointerForDelegate(_logger));
            Marshal.WriteIntPtr(_callbacks, IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_allocate));
            Marshal.WriteIntPtr(_callbacks, 2 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_free));
            Marshal.WriteIntPtr(_callbacks, 3 * IntPtr.Size, IntPtr.Zero);   // stepFinished: steps are synchronous
            Marshal.WriteIntPtr(_callbacks, 4 * IntPtr.Size, IntPtr.Zero);

            string resources = new Uri(Path.Combine(fmu.Folder, "resources") + Path.DirectorySeparatorChar).AbsoluteUri;
            _component = instantiate(instanceName, ModelExchange ? Fmi2ModelExchange : Fmi2CoSimulation, fmu.Description.Guid, resources, _callbacks, 0, 0);
            if (_component == IntPtr.Zero)
            {
                Dispose();
                throw new InvalidOperationException($"{fmu.Description.ModelName}: fmi2Instantiate başarısız (GUID ya da kaynaklar uyuşmuyor olabilir).");
            }
        }

        /// <summary>Sets the experiment, gives parameters and start values, and leaves initialisation: the instance is ready to step.</summary>
        public void Initialise(double startTime, IReadOnlyDictionary<string, double>? values = null)
        {
            Check(_setup(_component, 0, 0, startTime, 0, 0), "fmi2SetupExperiment");
            Check(_enterInit(_component), "fmi2EnterInitializationMode");
            if (values != null)
                foreach (var kv in values)
                    if (Description.Find(kv.Key) is { } v) Set(v, kv.Value);
            Check(_exitInit(_component), "fmi2ExitInitializationMode");
            Time = startTime;
            _initialised = true;
            if (ModelExchange) StartContinuous();   // [fmi-me] event mode first, then continuous time
        }

        public void Set(FmuVariable v, double value)
        {
            var vr = new[] { v.ValueReference };
            switch (v.Type)
            {
                case FmuType.Real:
                    Check(_setReal(_component, vr, (UIntPtr)1, new[] { value }), "fmi2SetReal " + v.Name);
                    break;
                case FmuType.Boolean:
                    Check(_setBoolean(_component, vr, (UIntPtr)1, new[] { value != 0 ? 1 : 0 }), "fmi2SetBoolean " + v.Name);
                    break;
                case FmuType.Integer:
                case FmuType.Enumeration:
                    Check(_setInteger(_component, vr, (UIntPtr)1, new[] { (int)Math.Round(value) }), "fmi2SetInteger " + v.Name);
                    break;
            }
        }

        public double Get(FmuVariable v)
        {
            var vr = new[] { v.ValueReference };
            switch (v.Type)
            {
                case FmuType.Real:
                {
                    var r = new double[1];
                    Check(_getReal(_component, vr, (UIntPtr)1, r), "fmi2GetReal " + v.Name);
                    return r[0];
                }

                case FmuType.Boolean:
                {
                    var r = new int[1];
                    Check(_getBoolean(_component, vr, (UIntPtr)1, r), "fmi2GetBoolean " + v.Name);
                    return r[0] != 0 ? 1 : 0;
                }

                case FmuType.Integer:
                case FmuType.Enumeration:
                {
                    var r = new int[1];
                    Check(_getInteger(_component, vr, (UIntPtr)1, r), "fmi2GetInteger " + v.Name);
                    return r[0];
                }

                default:
                    return double.NaN;
            }
        }

        /// <summary>One communication step from <see cref="Time"/>; the FMU's own solver runs inside it.</summary>
        public void Step(double h)
        {
            if (!_initialised) throw new InvalidOperationException("FMU önce Initialise ile başlatılmalı.");
            if (ModelExchange)
            {
                IntegrateTo(Time + h);   // [fmi-me] our integrator
                return;
            }

            Check(_doStep!(_component, Time, h, 1), "fmi2DoStep");
            Time += h;
        }

        private void Check(int status, string what)
        {
            // fmi2OK = 0, fmi2Warning = 1: going on; discard, error and fatal stop the co-simulation.
            if (status <= 1) return;
            throw new InvalidOperationException($"{Description.ModelName}: {what} {(status == 2 ? "atıldı" : status == 3 ? "hata" : status == 4 ? "ölümcül hata" : "beklemede")} ({status}).");
        }

        /// <summary>An instance nobody disposed (a circuit built again) still lets its binary and memory go.</summary>
        ~Fmu2Instance() => Dispose();

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            if (_component != IntPtr.Zero)
            {
                try
                {
                    if (_initialised) _terminate(_component);
                    _freeInstance(_component);
                }
                catch (SEHException)
                {
                }

                _component = IntPtr.Zero;
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

            if (_callbacks != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_callbacks);
                _callbacks = IntPtr.Zero;
            }
            GC.KeepAlive(_logger);
            GC.KeepAlive(_allocate);
            GC.KeepAlive(_free);
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr module);
    }
}
