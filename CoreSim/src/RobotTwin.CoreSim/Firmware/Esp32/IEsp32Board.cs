using System;
using RobotTwin.CoreSim.Firmware;

namespace RobotTwin.CoreSim.Firmware.Esp32
{
    /// <summary>
    /// [esp32hw] An ESP32 board in a running circuit, whichever engine runs its firmware: the fast simulation (the sketch
    /// built natively against our Arduino HAL, <see cref="Esp32CoSimulation"/>) or the hardware-compatible mode (the
    /// firmware built for the real chip, run instruction by instruction on our own Xtensa emulator,
    /// <see cref="Esp32EmulatedBoard"/>). Both run in lockstep with the circuit: <see cref="Advance"/> the slice, then step
    /// the circuit by the same amount.
    /// </summary>
    public interface IEsp32Board : IDisposable
    {
        string BoardRefDes { get; }
        Esp32BoardState State { get; }

        /// <summary>Number of times the firmware was (re)started.</summary>
        int BootCount { get; }

        /// <summary>UART0's baud rate as the firmware set it (Serial.begin); 0 while the board is off or the UART is not set up.</summary>
        double SerialBaud { get; }

        /// <summary>The I²C buses the firmware's Wire and Wire1 drive (devices attach here).</summary>
        I2cBus I2c { get; }

        I2cBus I2c1 { get; }

        /// <summary>The SPI devices wired to the board's pins (SPI.transfer reaches them, a CS pin selects them).</summary>
        SpiBus Spi { get; }

        /// <summary>Text printed on UART0 during the last slice.</summary>
        event Action<string>? SerialOutput;

        /// <summary>Simulator notes: resets, crashes, what the engine cannot do yet.</summary>
        event Action<string>? Log;

        /// <summary>Runs the firmware for <paramref name="dt"/> seconds from <paramref name="now"/>; the caller then steps the circuit.</summary>
        void Advance(double dt, double now);

        /// <summary>Called on each accepted circuit substep (input edges at the solver's resolution).</summary>
        void OnSubstep(double time);

        /// <summary>Queues text for a UART's receive line.</summary>
        void SendSerial(string text, int port = 0);

        /// <summary>What touchRead() returns for a touch pad (untouched ≈ 70–80, touched &lt; 20).</summary>
        void SetTouchValue(int gpio, int value);

        /// <summary>Presses EN: the firmware restarts at the next <see cref="Advance"/>.</summary>
        void Reset();
    }
}
