using System;
using System.Collections.Generic;
using System.Globalization;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware.Xtensa;

namespace RobotTwin.CoreSim.Firmware.Esp32
{
    /// <summary>
    /// [esp32hw] An ESP32 board running its firmware as built for the real chip (the hardware-compatible mode's
    /// <c>.elf</c> and merged flash image) on RobotWin's own Xtensa emulator (<see cref="Esp32Soc"/>), in lockstep with the
    /// circuit. Power behaves as fast mode's board (brown-out detector at 2.43 V, EN), each slice runs the chip for the
    /// slice's time at the clock the firmware set, the pads' inputs are sampled from the board's pins first and their
    /// drive (levels, the LEDC's PWM, pulls, open drain) is applied to the pins after, and UART0's output goes to the
    /// serial monitor. A restart the firmware asks for (esp_restart, a panic's reboot) starts the chip again with the ROM
    /// reporting a software reset, as on the real chip.
    /// </summary>
    public sealed class Esp32EmulatedBoard : IEsp32Board
    {
        private sealed class Pad
        {
            public DigitalPin? Pin;
            public Esp32PadAux? Aux;
            public double AppliedFrequency = double.NaN, AppliedDuty = double.NaN;
        }

        private readonly byte[] _elf;
        private readonly byte[]? _flashImage;
        private readonly Func<double>? _supply;
        private readonly Esp32ChipLoad? _chip;
        private readonly Pad[] _pads = new Pad[Esp32Gpio.Count];
        private readonly double _runCurrent;

        private Esp32Soc? _soc;
        private double _bootTime;
        private readonly int[] _uartSent = new int[3];
        private double _vdd = 3.3;
        private bool _enHigh = true, _resetRequested, _everRan, _disposed;
        private double _minVddWhileOff = double.PositiveInfinity;

        // Deep sleep: RTC memory kept for the wake, when the timer wakes the chip, the EXT0 pad and level.
        private byte[]? _rtcKept;
        private double _wakeAt = double.NaN;
        private (int Gpio, bool Level) _ext0 = (-1, false);

        /// <param name="elf">The application ELF the hardware build made (its segments and its ROM symbols).</param>
        /// <param name="flashImage">The merged flash image (bootloader, partition table, application), when there is one.</param>
        public Esp32EmulatedBoard(byte[] elf, byte[]? flashImage, CompiledCircuit circuit, string boardRefDes, Func<double>? supplyVoltage = null,
            Esp32Options? options = null)
        {
            _elf = elf ?? throw new ArgumentNullException(nameof(elf));
            _flashImage = flashImage;
            if (circuit == null) throw new ArgumentNullException(nameof(circuit));
            BoardRefDes = boardRefDes ?? throw new ArgumentNullException(nameof(boardRefDes));
            _supply = supplyVoltage;
            Options = options ?? new Esp32Options();
            _chip = circuit.Device<Esp32ChipLoad>(boardRefDes + ".CHIP");
            Spi.Warning += m => Note("SPI: " + m);
            _runCurrent = !double.IsNaN(Options.RunCurrent) ? Options.RunCurrent : _chip?.Current ?? 0.08;
            for (int g = 0; g < _pads.Length; g++)
            {
                var pad = new Pad();
                string? name = Esp32Gpio.PinName(g);
                if (name != null && !Esp32Gpio.IsFlashPin(g))
                {
                    pad.Pin = circuit.Device<DigitalPin>(boardRefDes + "." + name);
                    pad.Aux = circuit.Device<Esp32PadAux>(boardRefDes + "." + name + ".AUX");
                }

                _pads[g] = pad;
            }
        }

        public string BoardRefDes { get; }
        public Esp32Options Options { get; }
        public Esp32BoardState State { get; private set; } = Esp32BoardState.Off;
        public int BootCount { get; private set; }
        public bool InBrownOut { get; private set; }
        public double SerialBaud => State == Esp32BoardState.Running && _soc != null ? _soc.Uart0Baud : 0;

        /// <summary>How many times a watchdog reset the chip.</summary>
        public int WatchdogResets { get; private set; }

        /// <summary>The emulated chip of the current run (null while the board is off): its registers, cores and peripherals.</summary>
        public Esp32Soc? Chip => _soc;

        /// <summary>Why the chip stopped for good in this run (an instruction or ROM function the emulator lacks …), or null.</summary>
        public string? Halted { get; private set; }

        public I2cBus I2c { get; } = new I2cBus();
        public I2cBus I2c1 { get; } = new I2cBus();
        public SpiBus Spi { get; } = new SpiBus();

        public event Action<string>? SerialOutput;
        public event Action<string>? Log;

        /// <summary>Text per UART (0 Serial, 1 Serial1, 2 Serial2) during the last slice.</summary>
        public event Action<int, string>? SerialPortOutput;

        public void Reset() => _resetRequested = true;

        public void OnSubstep(double time)
        {
            // Inputs are sampled at each slice's start; the chip runs a whole slice at a time.
        }

        public void SendSerial(string text, int port = 0)
        {
            if (string.IsNullOrEmpty(text)) return;
            // The UART's RX line: the bytes arrive at its baud rate while the chip runs (lost while it is off, as on a board)
            if (port >= 0 && port <= 2) _soc?.SendToUart(port, System.Text.Encoding.UTF8.GetBytes(text));
        }

        public void Advance(double dt, double now)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Esp32EmulatedBoard));
            double end = now + dt;
            double vdd = _supply != null ? _supply() : _chip != null ? _chip.SupplyVoltage : 3.3;
            if (double.IsNaN(vdd)) vdd = 0;
            double en = _chip != null && !double.IsNaN(_chip.EnableVoltage) ? _chip.EnableVoltage : vdd;
            _vdd = vdd > 0.5 ? vdd : 3.3;
            bool powerOk = InBrownOut ? vdd >= Esp32CoSimulation.BrownOutReleaseVoltage : vdd >= Esp32CoSimulation.BrownOutVoltage;
            if (State == Esp32BoardState.Off && !InBrownOut && !_everRan) powerOk = vdd >= Esp32CoSimulation.BrownOutReleaseVoltage;
            _enHigh = _enHigh ? en > 0.25 * vdd : en > 0.75 * vdd;
            if (State == Esp32BoardState.Off) _minVddWhileOff = Math.Min(_minVddWhileOff, vdd);

            if (!powerOk || !_enHigh)
            {
                if (State != Esp32BoardState.Off)
                {
                    if (!powerOk && vdd > 1.8) Emit("\r\nBrownout detector was triggered\r\n\r\n");
                    Note(!powerOk ? $"besleme düştü (brown-out): {vdd.ToString("F2", CultureInfo.InvariantCulture)} V" : "EN pini düşük tutuldu (sıfırlama)");
                    Shutdown();
                    _minVddWhileOff = vdd;
                }

                InBrownOut = !powerOk;
            }
            else if (State == Esp32BoardState.Off && Halted == null)
            {
                // A dip the brown-out detector caught is a brown-out reset; a real power cycle is power-on.
                Boot(now, InBrownOut && _minVddWhileOff > 1.0 ? 15 : 1);
                InBrownOut = false;
            }
            else if (_resetRequested)
            {
                Halted = null;
                _rtcKept = null;
                Boot(now, 1);
            }
            else if (State == Esp32BoardState.DeepSleep)
            {
                // The RTC timer, or the EXT0 pad reaching its level (the pad keeps its own Schmitt-free reading here).
                bool timer = !double.IsNaN(_wakeAt) && now >= _wakeAt;
                bool ext0 = _ext0.Gpio >= 0 && _pads[_ext0.Gpio].Pin is { } wakePin && (wakePin.Voltage > 0.5 * _vdd) == _ext0.Level;
                if (timer || ext0) Boot(now, 5, ext0 ? 1u : 8u);
            }

            _resetRequested = false;

            if (State == Esp32BoardState.Running && _soc != null)
            {
                _soc.PadSupply = _vdd;
                _soc.SamplePads();
                // The chip runs until its own clock reaches the slice's end (the clock the firmware set: a few passes when it
                // switches frequency within the slice).
                double target = end - _bootTime;
                for (int pass = 0; pass < 8 && _soc.Seconds < target && !Stopped(_soc); pass++)
                    _soc.Run((long)Math.Ceiling((target - _soc.Seconds) * _soc.CpuHz));
                FlushUart();
                if (_soc.RestartRequested)
                {
                    int reason = _soc.RestartReason;
                    if (reason == 12)
                    {
                        Note("yazılım yeniden başlattı (esp_restart)");
                    }
                    else
                    {
                        Note("bekçi (watchdog) kartı sıfırladı");
                        WatchdogResets++;
                    }

                    Boot(end, reason);
                }
                else if (_soc.DeepSleepRequested)
                {
                    // Into deep sleep: the RTC memory is kept, the rest powers down until the timer or EXT0 wakes it.
                    _rtcKept = _soc.SaveRtcMemory();
                    _wakeAt = _soc.WakeAfterSeconds is { } after ? end + after : double.NaN;
                    _ext0 = _soc.Ext0Wake;
                    Note(_soc.WakeAfterSeconds is { } s
                        ? $"derin uyku: {(s * 1e6).ToString("F0", CultureInfo.InvariantCulture)} µs"
                        : "derin uyku (zamanlayıcıyla uyanma yok)");
                    Shutdown();
                    State = Esp32BoardState.DeepSleep;
                }
                else if (_soc.Stopped != null || _soc.Pro.AtBreak)
                {
                    Halted = _soc.Stopped ?? $"çekirdek durdu @0x{_soc.Pro.Pc:X8}";
                    Note("Emülatör durdu: " + Halted);
                    Shutdown();
                }
            }

            ApplyPins(now);
            UpdateChipCurrent();
        }

        private static bool Stopped(Esp32Soc soc) => soc.Stopped != null || soc.Pro.AtBreak || soc.RestartRequested;

        private void Boot(double time, int romResetReason, uint wakeCause = 0)
        {
            Shutdown();
            var soc = new Esp32Soc { RomResetReason = romResetReason, PadSupply = _vdd };
            soc.Load(_elf);
            if (_flashImage != null) soc.LoadFlash(_flashImage);
            if (romResetReason == 5 && _rtcKept != null) soc.WakeFromDeepSleep(_rtcKept, wakeCause);   // RTC memory survives
            _rtcKept = null;
            _wakeAt = double.NaN;
            _ext0 = (-1, false);
            soc.PadVoltage = gpio => gpio >= 0 && gpio < _pads.Length && _pads[gpio].Pin is { } pin ? pin.Voltage : 0;
            soc.I2cBuses[0] = I2c;    // Wire
            soc.I2cBuses[1] = I2c1;   // Wire1
            soc.Spi = Spi;
            soc.PadLevelChanged += (gpio, level) => Spi.PinChanged(gpio, level);   // CS pins frame SPI devices
            if (Options.BootMessages)
                Emit("ets Jun  8 2016 00:22:57\r\n\r\nrst:" + RomResetText(romResetReason) + ",boot:0x13 (SPI_FAST_FLASH_BOOT)\r\n");
            _soc = soc;
            Array.Clear(_uartSent, 0, _uartSent.Length);
            _bootTime = time;
            State = Esp32BoardState.Running;
            BootCount++;
            _everRan = true;
        }

        private static string RomResetText(int reason)
        {
            switch (reason)
            {
                case 12: return "0xc (SW_CPU_RESET)";
                case 7: return "0x7 (TG0WDT_SYS_RESET)";
                case 8: return "0x8 (TG1WDT_SYS_RESET)";
                case 11: return "0xb (TGWDT_CPU_RESET)";
                case 5: return "0x5 (DEEPSLEEP_RESET)";
                case 15: return "0xf (RTCWDT_BROWN_OUT_RESET)";
                default: return "0x1 (POWERON_RESET)";
            }
        }

        private void Shutdown()
        {
            foreach (var slave in Spi.Slaves)
                if (slave.Selected) Spi.PinChanged(slave.Cs, true);   // the pins let go: chip selects float high
            if (_soc != null) FlushUart();
            _soc = null;
            State = Esp32BoardState.Off;
            foreach (var pad in _pads) Release(pad);
        }

        /// <summary>What the UARTs printed since the last slice: UART0 to the serial monitor, every port to <see cref="SerialPortOutput"/>.</summary>
        private void FlushUart()
        {
            if (_soc == null) return;
            for (int port = 0; port < 3; port++)
            {
                var uart = _soc.UartText(port);
                if (uart.Length <= _uartSent[port]) continue;
                string text = uart.ToString(_uartSent[port], uart.Length - _uartSent[port]);
                if (port == 0) Emit(text);
                SerialPortOutput?.Invoke(port, text);
                _uartSent[port] = uart.Length;
                if (_uartSent[port] > 1_000_000)
                {
                    uart.Clear();
                    _uartSent[port] = 0;
                }
            }
        }

        /// <summary>Each pad's drive onto its pin: a level, the LEDC's PWM, or released with its pulls.</summary>
        private void ApplyPins(double now)
        {
            for (int g = 0; g < _pads.Length; g++)
            {
                var pad = _pads[g];
                if (pad.Pin == null) continue;
                if (_soc == null || State != Esp32BoardState.Running)
                {
                    Release(pad);
                    continue;
                }

                var drive = _soc.PadDrive(g);
                var pin = pad.Pin;
                if (drive.IsDac)
                {
                    // The DAC drives the pad as an analog source (through the pad's auxiliary device, as in the fast mode).
                    StopPwm(pad);
                    pin.Mode = PinMode.Input;
                    if (pad.Aux != null)
                    {
                        pad.Aux.Pulldown = false;
                        pad.Aux.DacVoltage = drive.DacVolts;
                        pad.Aux.DacEnabled = true;
                    }

                    continue;
                }

                if (pad.Aux != null) pad.Aux.DacEnabled = false;
                if (drive.Driven && drive.IsPwm)
                {
                    if (pad.Aux != null) pad.Aux.Pulldown = false;
                    if (double.IsNaN(pad.AppliedDuty) || Math.Abs(pad.AppliedDuty - drive.PwmDuty) > 1e-12 || Math.Abs(pad.AppliedFrequency - drive.PwmFrequency) > 1e-9)
                    {
                        pin.Mode = PinMode.OutputLow;
                        pin.SetPwm(drive.PwmFrequency, drive.PwmDuty, now);
                        pad.AppliedDuty = drive.PwmDuty;
                        pad.AppliedFrequency = drive.PwmFrequency;
                    }

                    continue;
                }

                StopPwm(pad);
                if (drive.Driven)
                {
                    pin.Mode = drive.Level ? PinMode.OutputHigh : PinMode.OutputLow;
                    if (pad.Aux != null) pad.Aux.Pulldown = false;
                }
                else
                {
                    pin.Mode = drive.PullUp ? PinMode.InputPullup : PinMode.Input;
                    if (pad.Aux != null) pad.Aux.Pulldown = drive.PullDown && !drive.PullUp;
                }
            }
        }

        private static void StopPwm(Pad pad)
        {
            if (double.IsNaN(pad.AppliedDuty)) return;
            pad.Pin?.StopPwm();
            pad.AppliedDuty = pad.AppliedFrequency = double.NaN;
        }

        private static void Release(Pad pad)
        {
            if (pad.Pin == null) return;
            StopPwm(pad);
            pad.Pin.Mode = PinMode.Input;
            if (pad.Aux != null)
            {
                pad.Aux.Pulldown = false;
                pad.Aux.DacEnabled = false;
            }
        }

        private void UpdateChipCurrent()
        {
            if (_chip == null) return;
            _chip.Current = State == Esp32BoardState.Running ? _runCurrent
                : State == Esp32BoardState.DeepSleep ? Options.DeepSleepCurrent : Options.ResetCurrent;
        }

        private void Emit(string text)
        {
            if (!string.IsNullOrEmpty(text)) SerialOutput?.Invoke(text);
        }

        private void Note(string text) => Log?.Invoke(text);

        public void Dispose()
        {
            if (_disposed) return;
            Shutdown();
            _disposed = true;
        }
    }
}
