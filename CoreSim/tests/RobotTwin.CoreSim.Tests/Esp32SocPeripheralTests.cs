using System;
using RobotTwin.CoreSim.Firmware.Xtensa;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [esp32emu] The emulated ESP32's peripherals, driven through their registers as IDF drives them, with the PRO_CPU
    /// spinning in IRAM (`j .`) so only the clock and the peripherals move: the timer groups (esp_timer's LAC counter,
    /// alarms and their interrupts), the cross-core interrupts FreeRTOS yields with, the LEDC, the eFuse factory words, and
    /// the SAR ADC against Espressif's characterisation.
    /// </summary>
    public class Esp32SocPeripheralTests
    {
        private const uint Spin = 0x40080000;

        /// <summary>A chip on the PLL at 240 MHz (APB 80 MHz) whose PRO_CPU spins, interrupts masked.</summary>
        private static Esp32Soc Chip()
        {
            var soc = new Esp32Soc();
            soc.Write8(Spin, 0x06);       // j . (J with offset -4)
            soc.Write8(Spin + 1, 0xFF);
            soc.Write8(Spin + 2, 0xFF);
            soc.Pro.Reset(Spin);
            soc.Write32(0x3FF48070, 1u << 27);   // RTC_CNTL_CLK_CONF: SOC_CLK_SEL = PLL
            soc.Write32(0x3FF0003C, 2);          // DPORT_CPU_PER_CONF: 240 MHz
            return soc;
        }

        [Fact]
        public void AnRtcPadTakesItsPullsFromItsRtcIoRegister()
        {
            // ESP-IDF's gpio_pullup_en(4) on the ESP32 sets TOUCH_PAD0's RUE (rtc_gpio_pullup_en), not IO_MUX's FUN_WPU:
            // Arduino's pinMode(4, INPUT_PULLUP) left GPIO4 floating in the emulator (Bounce2 read 0 there)
            var soc = new Esp32Soc();
            soc.Write32(0x3FF49048, 2u << 12 | 1u << 9);            // IO_MUX GPIO4: GPIO function, input on, no pulls
            Assert.False(soc.PadDrive(4).PullUp);
            soc.Write32(0x3FF48494, 1u << 27);                      // RTC_IO_TOUCH_PAD0: RUE
            Assert.True(soc.PadDrive(4).PullUp);
            Assert.False(soc.PadDrive(4).PullDown);
            soc.Write32(0x3FF48494, 1u << 28);                      // RDE
            Assert.False(soc.PadDrive(4).PullUp);
            Assert.True(soc.PadDrive(4).PullDown);
            soc.Write32(0x3FF49048, 2u << 12 | 1u << 9 | 1u << 8);  // FUN_WPU does not reach an RTC pad
            soc.Write32(0x3FF48494, 0);
            Assert.False(soc.PadDrive(4).PullUp);

            soc.Write32(0x3FF4908C, 2u << 12 | 1u << 9 | 1u << 8);  // GPIO23, no RTC function: IO_MUX's FUN_WPU
            Assert.True(soc.PadDrive(23).PullUp);
            soc.Write32(0x3FF4848C, 1u << 22);                      // XTAL_32K_PAD X32P_RUE: GPIO32
            Assert.True(soc.PadDrive(32).PullUp);
            Assert.False(soc.PadDrive(33).PullUp);
        }

        [Fact]
        public void ClockFollowsTheClockRegisters()
        {
            var soc = new Esp32Soc();
            Assert.Equal(40e6, soc.CpuHz);       // reset: XTAL
            soc.Write32(0x3FF48070, 1u << 27);
            soc.Write32(0x3FF0003C, 1);
            Assert.Equal(160e6, soc.CpuHz);
            Assert.Equal(80e6, soc.ApbHz);
            soc = Chip();
            soc.Run(240_000);
            Assert.InRange(soc.Seconds, 0.999e-3, 1.001e-3);
        }

        [Fact]
        public void LacTimerCountsAsEspTimerSetsIt()
        {
            var soc = Chip();
            // esp_timer: divider 40 (80 MHz / 40 = 2 MHz), counting up, enabled
            soc.Write32(0x3FF5F070, 1u << 31 | 1u << 30 | 40u << 13);
            soc.Run(2_400_000);                   // 10 ms
            soc.Write32(0x3FF5F080, 1);           // UPDATE: latch
            uint lo = soc.Read32(0x3FF5F078);
            Assert.InRange(lo, 19_990u, 20_010u);
        }

        [Fact]
        public void TimerAlarmRaisesItsLevelInterruptUntilCleared()
        {
            var soc = Chip();
            soc.Write32(0x3FF00104 + 14 * 4, 9);  // matrix: TG0_T0_LEVEL (14) → PRO CPU interrupt 9
            soc.Write32(0x3FF5F098, 1);           // INT_ENA: T0
            soc.Write32(0x3FF5F010, 1000);        // alarm at 1000 counts
            soc.Write32(0x3FF5F014, 0);
            // T0: enabled, up, divider 80 (1 MHz), level interrupt, alarm armed
            soc.Write32(0x3FF5F000, 1u << 31 | 1u << 30 | 80u << 13 | 1u << 11 | 1u << 10);
            soc.Run(120_000);                     // 0.5 ms: not yet
            Assert.Equal(0u, soc.Pro.ExternalLevels & (1u << 9));
            soc.Run(240_000);                     // past 1 ms
            Assert.NotEqual(0u, soc.Pro.ExternalLevels & (1u << 9));
            Assert.Equal(1u, soc.Read32(0x3FF5F0A0) & 1);   // INT_ST
            Assert.Equal(0u, soc.Read32(0x3FF5F000) & (1u << 10));   // the alarm disarmed itself
            soc.Write32(0x3FF5F0A4, 1);           // INT_CLR
            Assert.Equal(0u, soc.Pro.ExternalLevels & (1u << 9));
        }

        [Fact]
        public void CrossCoreInterruptHoldsUntilTheIsrClearsIt()
        {
            var soc = Chip();
            soc.Write32(0x3FF00218 + 25 * 4, 2);  // APP map: FROM_CPU_1 (25) → APP CPU interrupt 2
            soc.Write32(0x3FF000E0, 1);           // PRO yields the APP: DPORT_CPU_INTR_FROM_CPU_1
            Assert.Equal(1u << 2, soc.App.ExternalLevels);
            Assert.Equal(0u, soc.Pro.ExternalLevels);
            Assert.Equal(1u, soc.Read32(0x3FF000E0));
            soc.Write32(0x3FF000E0, 0);           // the ISR clears it
            Assert.Equal(0u, soc.App.ExternalLevels);
        }

        [Fact]
        public void LedcTakesTheNewDutyAtTheNextPeriod()
        {
            var soc = Chip();
            soc.Write32(0x3FF59140, 0x0201F40A);  // HSTIMER0: APB, divider 15.625, 10 bits → 5 kHz (Arduino's ledcAttach(…, 5000, 10))
            soc.Write32(0x3FF59000, 1u << 2);     // HSCH0: output on, timer 0
            soc.Write32(0x3FF59008, 512u << 4);   // duty 512 / 1024
            soc.Write32(0x3FF5900C, 1u << 31 | 1u << 30 | 1u << 20 | 1u << 10);   // DUTY_START, one step of 0
            Assert.NotEqual(0u, soc.Read32(0x3FF5900C) & (1u << 31));   // still pending
            soc.Run(240 * 250);                   // 250 µs > one 200 µs period
            Assert.Equal(0u, soc.Read32(0x3FF5900C) & (1u << 31));
            Assert.Equal(512u << 4, soc.Read32(0x3FF59010));            // DUTY_R
            var output = soc.LedcOutput(0);
            Assert.NotNull(output);
            Assert.Equal(5000, output!.Value.Frequency, 3);
            Assert.Equal(0.5, output.Value.Duty, 6);
        }

        [Fact]
        public void LedcFadesInHardware()
        {
            var soc = Chip();
            soc.Write32(0x3FF59140, 0x0201F40A);  // 5 kHz, 10 bits
            soc.Write32(0x3FF59000, 1u << 2);
            soc.Write32(0x3FF59008, 0);
            // fade up: 100 steps of 5 every period (from 0 to 500 in 20 ms), end interrupt enabled
            soc.Write32(0x3FF59188, 1u << 8);
            soc.Write32(0x3FF5900C, 1u << 31 | 1u << 30 | 100u << 20 | 1u << 10 | 5u);
            soc.Run(240 * 10_000);                // 10 ms: about half way
            uint half = soc.Read32(0x3FF59010) >> 4;
            Assert.InRange(half, 230u, 260u);
            Assert.Equal(0u, soc.Read32(0x3FF59180) & (1u << 8));
            soc.Run(240 * 12_000);                // past 20 ms
            Assert.Equal(500u, soc.Read32(0x3FF59010) >> 4);
            Assert.NotEqual(0u, soc.Read32(0x3FF59180) & (1u << 8));   // DUTY_CHNG_END_HSCH0
        }

        [Fact]
        public void EfuseHoldsTheFactoryWords()
        {
            var soc = Chip();
            soc.FactoryMac = new byte[] { 0x02, 0x52, 0x57, 0x12, 0x34, 0x56 };
            uint w1 = soc.Read32(0x3FF5A004), w2 = soc.Read32(0x3FF5A008);
            byte[] mac = { (byte)(w2 >> 8), (byte)w2, (byte)(w1 >> 24), (byte)(w1 >> 16), (byte)(w1 >> 8), (byte)w1 };
            Assert.Equal(soc.FactoryMac, mac);
            byte crc = 0;
            foreach (byte b in mac)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++) crc = (byte)((crc & 1) != 0 ? (crc >> 1) ^ 0x8C : crc >> 1);
            }

            Assert.Equal(crc, (byte)(w2 >> 16));
            // ADC_VREF: 1107 mV is one 7 mV step up; revision 3 needs REV1, REV2 and APB_CTRL_DATE bit 31
            Assert.Equal(1u, (soc.Read32(0x3FF5A010) >> 8) & 0x1F);
            Assert.NotEqual(0u, soc.Read32(0x3FF5A00C) & (1u << 15));
            Assert.NotEqual(0u, soc.Read32(0x3FF5A014) & (1u << 20));
            Assert.Equal(1u, (soc.Read32(0x3FF5A014) >> 24) & 3);   // minor 1: revision 3.1
            Assert.NotEqual(0u, soc.Read32(0x3FF6607C) & 0x80000000u);
        }

        [Fact]
        public void CharacterisedAdcInvertsEspressifsModel()
        {
            // Inside the range, the code the chip gives calibrates back to the voltage (within a code's width).
            for (int unit = 0; unit < 2; unit++)
            {
                for (int mv = 200; mv <= 3000; mv += 50)
                {
                    int code = Esp32Soc.CharacterisedCode(unit, 3, 1107, mv);
                    Assert.InRange(Esp32Soc.CharacterisedMillivolts(unit, 3, 1107, code) - mv, -2, 2);
                }
            }

            Assert.Equal(0, Esp32Soc.CharacterisedCode(0, 3, 1107, 100));      // the dead zone
            Assert.Equal(4095, Esp32Soc.CharacterisedCode(0, 3, 1107, 3300)); // saturated
            Assert.Equal(4095, Esp32Soc.CharacterisedCode(0, 0, 1107, 1200)); // 0 dB tops out near 1.1 V
        }

        [Fact]
        public void GpioDrivesThePadAsDigitalWriteDoes()
        {
            var soc = Chip();
            soc.Write32(0x3FF49040, 2u << 12 | 1u << 9);   // IO_MUX GPIO2: function GPIO, input on
            Assert.False(soc.PadDrive(2).Driven);           // reset: output off, the pad floats
            soc.Write32(0x3FF44024, 1u << 2);               // ENABLE_W1TS
            soc.Write32(0x3FF44008, 1u << 2);               // OUT_W1TS
            var drive = soc.PadDrive(2);
            Assert.True(drive.Driven);
            Assert.True(drive.Level);
            Assert.False(drive.IsPwm);
            soc.Write32(0x3FF4400C, 1u << 2);               // OUT_W1TC
            Assert.False(soc.PadDrive(2).Level);
            // open drain: high lets go of the pin
            soc.Write32(0x3FF44088 + 2 * 4, 1u << 2);       // PAD_DRIVER
            soc.Write32(0x3FF44008, 1u << 2);
            Assert.False(soc.PadDrive(2).Driven);
            soc.Write32(0x3FF4400C, 1u << 2);
            Assert.True(soc.PadDrive(2).Driven);
        }

        [Fact]
        public void MatrixRoutesLedcToItsPin()
        {
            var soc = Chip();
            soc.Write32(0x3FF59140, 0x0201F40A);            // 5 kHz, 10 bits
            soc.Write32(0x3FF59000, 1u << 2);
            soc.Write32(0x3FF59008, 256u << 4);             // 25 %
            soc.Write32(0x3FF5900C, 1u << 31 | 1u << 30 | 1u << 20 | 1u << 10);
            soc.Run(240 * 250);
            soc.Write32(0x3FF49024, 2u << 12);              // IO_MUX GPIO25: function GPIO
            soc.Write32(0x3FF44530 + 25 * 4, 71);           // FUNC_OUT_SEL: LEDC_HS_SIG_OUT0
            var drive = soc.PadDrive(25);
            Assert.True(drive.Driven);
            Assert.True(drive.IsPwm);
            Assert.Equal(5000, drive.PwmFrequency, 3);
            Assert.Equal(0.25, drive.PwmDuty, 6);
            soc.Write32(0x3FF44530 + 25 * 4, 71u | 1u << 9); // inverted
            Assert.Equal(0.75, soc.PadDrive(25).PwmDuty, 6);
        }

        [Fact]
        public void GpioInputsUseSchmittThresholdsAndRaiseTheirInterrupt()
        {
            var soc = Chip();
            double volts = 0;
            soc.PadVoltage = gpio => gpio == 4 ? volts : 0;
            soc.Write32(0x3FF00104 + 22 * 4, 5);           // matrix: GPIO (22) → PRO CPU interrupt 5
            soc.Write32(0x3FF44088 + 4 * 4, 1u << 7 | 1u << 15);   // GPIO4: rising edge, to the PRO CPU
            soc.SamplePads();
            Assert.Equal(0u, soc.Read32(0x3FF4403C) & (1u << 4));
            volts = 2.0;                                    // between VIL (0.825) and VIH (2.475): still low
            soc.SamplePads();
            Assert.Equal(0u, soc.Read32(0x3FF4403C) & (1u << 4));
            volts = 2.6;
            soc.SamplePads();
            Assert.NotEqual(0u, soc.Read32(0x3FF4403C) & (1u << 4));
            Assert.NotEqual(0u, soc.Read32(0x3FF44068) & (1u << 4));   // PCPU_INT
            Assert.Equal(1u << 5, soc.Pro.ExternalLevels);
            volts = 1.0;                                    // above VIL: stays high
            soc.SamplePads();
            Assert.NotEqual(0u, soc.Read32(0x3FF4403C) & (1u << 4));
            soc.Write32(0x3FF4404C, 1u << 4);               // STATUS_W1TC
            Assert.Equal(0u, soc.Pro.ExternalLevels);
        }

        [Fact]
        public void UartTextIsUtf8AndReceivedBytesArriveAtTheBaudRate()
        {
            var soc = Chip();
            foreach (byte b in System.Text.Encoding.UTF8.GetBytes("Araçlara yeşil"))
                soc.Write32(0x60000000, b);                  // the FIFO through its AHB address, as uart_ll_write_txfifo
            Assert.Equal("Araçlara yeşil", soc.Uart0.ToString());

            soc.Write32(0x3FF40024, 0x0A006001);            // CONF1: RXFIFO_FULL at 1 byte
            soc.Write32(0x3FF4000C, 1);                     // INT_ENA: RXFIFO_FULL
            soc.Write32(0x3FF00104 + 34 * 4, 3);            // matrix: UART0 (34) → PRO CPU interrupt 3
            soc.SendToUart0(new byte[] { (byte)'o', (byte)'k' });
            Assert.Equal(0u, soc.Read32(0x3FF4001C) & 0xFF);   // still on the wire
            soc.Run(240 * 200);                             // 200 µs: two bytes at 115200 baud take 174 µs
            Assert.Equal(2u, soc.Read32(0x3FF4001C) & 0xFF);
            Assert.Equal(1u << 3, soc.Pro.ExternalLevels);
            Assert.Equal((uint)'o', soc.Read32(0x3FF40000));
            Assert.Equal((uint)'k', soc.Read32(0x3FF40000));
            Assert.Equal(0u, soc.Read32(0x3FF4001C) & 0xFF);
            Assert.Equal(0u, soc.Pro.ExternalLevels);       // emptied: the condition is gone
        }

        [Fact]
        public void Uart2SendsAndReceivesOnItsOwn()
        {
            var soc = Chip();
            foreach (byte b in System.Text.Encoding.UTF8.GetBytes("$GPGGA"))
                soc.Write32(0x6002E000, b);                  // UART2's FIFO through its AHB address
            Assert.Equal("$GPGGA", soc.UartText(2).ToString());
            Assert.Equal(string.Empty, soc.Uart0.ToString());
            soc.Write32(0x3FF6E014, 8333);                  // CLKDIV: 9600 baud at 80 MHz
            Assert.Equal(9600, soc.UartBaud(2), 0);
            soc.SendToUart(2, new byte[] { 1, 2, 3 });      // a byte at 9600 baud takes 1.04 ms
            soc.Run(240 * 2200);
            Assert.Equal(2u, soc.Read32(0x3FF6E01C) & 0xFF);
            soc.Run(240 * 1000);
            Assert.Equal(3u, soc.Read32(0x3FF6E01C) & 0xFF);
            Assert.Equal(0u, soc.Read32(0x3FF4001C) & 0xFF);   // UART0 got nothing
            Assert.Equal(1u, soc.Read32(0x3FF6E000));
        }

        [Fact]
        public void DacPutsItsValueOnThePad()
        {
            var soc = Chip();
            Assert.False(soc.PadDrive(25).IsDac);
            soc.Write32(0x3FF48484, 128u << 19 | 1u << 18 | 1u << 17);   // RTC_IO_PAD_DAC1: value 128, powered, on the pad
            var drive = soc.PadDrive(25);
            Assert.True(drive.IsDac);
            Assert.False(drive.Driven);
            Assert.Equal(3.3 * 128 / 255, drive.DacVolts, 6);
            Assert.False(soc.PadDrive(26).IsDac);
            soc.Write32(0x3FF48488, 255u << 19 | 1u << 18);
            Assert.Equal(3.3, soc.PadDrive(26).DacVolts, 6);
            Assert.False(default(Esp32PadDrive).IsPwm);
        }

        [Fact]
        public void TouchPadsReadTheirCounts()
        {
            var soc = Chip();
            soc.TouchValue = gpio => gpio == 4 ? 12 : gpio == 15 ? 70 : 75;
            Assert.Equal(12u << 16 | 75u, soc.Read32(0x3FF48870));    // T0 (GPIO4) touched, T1 (GPIO0)
            Assert.Equal(75u << 16 | 70u, soc.Read32(0x3FF48874));    // T2 (GPIO2), T3 (GPIO15)
            Assert.NotEqual(0u, soc.Read32(0x3FF48884) & (1u << 10)); // MEAS_DONE
        }

        /// <summary>
        /// [encoder] A PCNT unit wired as IDF's rotary encoder example decodes quadrature ×4: channel 0 counts A's edges
        /// steered by B, channel 1 B's edges steered by A; turning back counts down, and the high limit returns the count
        /// to zero with its event and interrupt.
        /// </summary>
        [Fact]
        public void PcntDecodesAQuadratureEncoder()
        {
            var soc = Chip();
            bool a = false, b = false;
            soc.PadVoltage = gpio => gpio == 4 ? (a ? 3.3 : 0) : gpio == 5 ? (b ? 3.3 : 0) : 0;
            soc.SamplePads();
            const uint pcnt = 0x3FF57000, funcIn = 0x3FF44130;
            foreach (uint mux in new uint[] { 0x3FF49048, 0x3FF4906C }) soc.Write32(mux, 2u << 12 | 1u << 9);   // GPIO4, GPIO5: GPIO function, input on
            soc.Write32(funcIn + 39 * 4, 1u << 7 | 4);   // unit 0, channel 0: edges from A (GPIO4)
            soc.Write32(funcIn + 41 * 4, 1u << 7 | 5);   //                     level from B (GPIO5)
            soc.Write32(funcIn + 40 * 4, 1u << 7 | 5);   // channel 1: edges from B
            soc.Write32(funcIn + 42 * 4, 1u << 7 | 4);   //            level from A
            // Channel 0: falling +1, rising −1, reversed while the level is low; channel 1: rising +1, falling −1, the same.
            soc.Write32(pcnt + 0x00, 1u << 16 | 2u << 18 | 1u << 22 | 2u << 24 | 1u << 26 | 1u << 30 | 1u << 12);
            soc.Write32(pcnt + 0x08, 16u | unchecked((uint)(ushort)(short)-100) << 16);   // limits +16 and −100
            soc.Write32(pcnt + 0x88, 1);
            soc.Write32(pcnt + 0xB0, 0x5554);                            // unit 0 out of reset, counting

            void Turn(int steps)
            {
                // From 00, A leads B going forward (10, 11, 01, 00) and B leads A going back (01, 11, 10, 00).
                var forward = new[] { (true, false), (true, true), (false, true), (false, false) };
                var back = new[] { (false, true), (true, true), (true, false), (false, false) };
                for (int i = 0; i < Math.Abs(steps); i++)
                {
                    var (na, nb) = steps > 0 ? forward[i % 4] : back[i % 4];
                    a = na;
                    b = nb;
                    soc.SamplePads();
                }
            }

            Turn(12);
            Assert.Equal(12, soc.PcntCount(0));
            Assert.Equal(12u, soc.Read32(pcnt + 0x60));
            Turn(-4);                                                    // back one cycle: 01, 11, 10, 00
            Assert.Equal(8, soc.PcntCount(0));
            Assert.Equal(0u, soc.Read32(pcnt + 0x80));
            Turn(8);                                                     // 16 reached: back to zero, H_LIM
            Assert.Equal(0, soc.PcntCount(0));
            Assert.Equal(1u, soc.Read32(pcnt + 0x84));                   // INT_ST, unit 0
            Assert.NotEqual(0u, soc.Read32(pcnt + 0x90) & (1u << 5));    // STATUS: H_LIM
            soc.Write32(pcnt + 0x8C, 1);
            Assert.Equal(0u, soc.Read32(pcnt + 0x84));
            soc.Write32(pcnt + 0xB0, 0x5556);                            // paused: edges do not count
            Turn(4);
            Assert.Equal(0, soc.PcntCount(0));
        }

        /// <summary>
        /// [motor] MCPWM0: a 1 kHz up-counting timer, an operator setting its output A high at zero and low at compare A
        /// (25 %), the dead-time module making A and an inverted B for a half bridge (10 µs on each edge), up-down counting,
        /// a toggle that halves the frequency, a timer stopped at zero and a software force; the pads carry the PWM.
        /// </summary>
        [Fact]
        public void McpwmGeneratesComplementaryPwmWithDeadTime()
        {
            var soc = Chip();
            const uint m = 0x3FF5E000;
            soc.Write32(0x3FF49070, 2u << 12);   // GPIO18: GPIO function
            soc.Write32(0x3FF49074, 2u << 12);   // GPIO19
            soc.Write32(0x3FF44530 + 18 * 4, 32);   // PWM0_OUT0A
            soc.Write32(0x3FF44530 + 19 * 4, 33);   // PWM0_OUT0B
            soc.Write32(m + 0x00, 15);                       // 160 MHz / 16 = 10 MHz
            soc.Write32(m + 0x04, 9 | 999u << 8);            // timer 0: / 10 = 1 MHz, period 0..999
            soc.Write32(m + 0x08, 2 | 1u << 3);              // start, free running, counting up
            soc.Write32(m + 0x40, 250);                      // operator 0, compare A
            soc.Write32(m + 0x50, 2u << 0 | 1u << 4);        // generator A: high at zero, low at A
            var a = soc.PadDrive(18);
            Assert.True(a.IsPwm);
            Assert.Equal(1000, a.PwmFrequency, 6);
            Assert.Equal(0.25, a.PwmDuty, 6);

            // Active-high complementary: A's rising edge 10 µs late, B = A inverted with its falling edge 10 µs late.
            soc.Write32(m + 0x58, 1u << 14 | 1u << 17);      // FED inverted, both paths in, delays in timer ticks
            soc.Write32(m + 0x60, 9);                        // RED: 10 ticks (the register holds one less)
            soc.Write32(m + 0x5C, 9);                        // FED: 10 ticks
            Assert.Equal(0.24, soc.PadDrive(18).PwmDuty, 6);
            var b = soc.PadDrive(19);
            Assert.Equal(1000, b.PwmFrequency, 6);
            Assert.Equal(0.74, b.PwmDuty, 6);

            // Up-down: high from A going up to A coming down (2 × (500 − 375) ticks of 1000), still 1 kHz.
            soc.Write32(m + 0x58, 3u << 15);                 // dead time bypassed
            soc.Write32(m + 0x04, 9 | 500u << 8);
            soc.Write32(m + 0x08, 2 | 3u << 3);
            soc.Write32(m + 0x40, 375);
            soc.Write32(m + 0x50, 2u << 4 | 1u << 16);       // UTEA high, DTEA low
            Assert.Equal(1000, soc.PadDrive(18).PwmFrequency, 6);
            Assert.Equal(0.25, soc.PadDrive(18).PwmDuty, 6);

            // A toggle at zero, counting up: 500 Hz, half the time high.
            soc.Write32(m + 0x04, 9 | 999u << 8);
            soc.Write32(m + 0x08, 2 | 1u << 3);
            soc.Write32(m + 0x50, 3u << 0);
            Assert.Equal(500, soc.PadDrive(18).PwmFrequency, 6);
            Assert.Equal(0.5, soc.PadDrive(18).PwmDuty, 6);

            // Stopped at zero after "high at zero, low at A": the output rests high; a force low holds it low.
            soc.Write32(m + 0x50, 2u << 0 | 1u << 4);
            soc.Write32(m + 0x08, 0 | 1u << 3);
            var stopped = soc.PadDrive(18);
            Assert.False(stopped.IsPwm);
            Assert.True(stopped.Level);
            soc.Write32(m + 0x4C, 1u << 6);
            Assert.False(soc.PadDrive(18).Level);
        }

        [Fact]
        public void RmtSendsWs2812BitsToThePad()
        {
            var soc = Chip();
            int pad = -1;
            byte[]? frame = null;
            soc.RmtFrame += (gpio, bytes) =>
            {
                pad = gpio;
                frame = bytes;
            };
            soc.Write32(0x3FF4906C, 2u << 12);              // IO_MUX GPIO5: function GPIO
            soc.Write32(0x3FF44530 + 5 * 4, 87);            // FUNC_OUT_SEL: RMT_SIG_OUT0
            soc.Write32(0x3FF56020, 2u | 1u << 24);         // CH0CONF0: DIV_CNT 2 (25 ns ticks), one block
            soc.Write32(0x3FF56024, 1u << 17);              // CH0CONF1: APB clock
            byte value = 0xA5;
            for (int i = 0; i < 8; i++)
            {
                bool one = (value & (0x80 >> i)) != 0;
                uint high = one ? 32u : 16u, low = one ? 18u : 34u;    // T1H 0.8 µs / T0H 0.4 µs
                soc.Write32(0x3FF56800 + (uint)i * 4, high | 1u << 15 | low << 16);
            }

            soc.Write32(0x3FF56800 + 8 * 4, 0);             // end marker
            soc.Write32(0x3FF560A8, 1);                     // TX_END interrupt enabled
            soc.Write32(0x3FF56024, 1u << 17 | 1);          // TX_START
            soc.Run(240 * 20);                              // 8 bits × 1.25 µs = 10 µs
            Assert.Equal(5, pad);
            Assert.Equal(new byte[] { 0xA5 }, frame);
            Assert.NotEqual(0u, soc.Read32(0x3FF560A0) & 1); // TX_END
        }

        [Fact]
        public void EmulatorGapNamesWhatTheEmulatorLacks()
        {
            Assert.Null(RobotTwin.CoreSim.Design.Esp32Parts.EmulatorGap("#include <Wire.h>\nvoid setup() {}"));
            Assert.Null(RobotTwin.CoreSim.Design.Esp32Parts.EmulatorGap("#include <WiFi.h>\n#include <WiFiUdp.h>\nvoid setup() { WiFi.begin(\"a\", \"b\"); }"));
            Assert.Equal("Wi-Fi (esp_now)", RobotTwin.CoreSim.Design.Esp32Parts.EmulatorGap("#  include <esp_now.h>"));
            Assert.Null(RobotTwin.CoreSim.Design.Esp32Parts.EmulatorGap("#include <WiFi.h>\nvoid setup() { WiFi.softAP(\"ap\"); }"));
            Assert.Equal("Wi-Fi (WiFiClientSecure)", RobotTwin.CoreSim.Design.Esp32Parts.EmulatorGap("#include <WiFiClientSecure.h>"));
            Assert.Equal("esp_camera", RobotTwin.CoreSim.Design.Esp32Parts.EmulatorGap("#include \"esp_camera.h\""));
            Assert.Equal("Bluetooth", RobotTwin.CoreSim.Design.Esp32Parts.EmulatorGap("#include <BLEDevice.h>"));
            Assert.Null(RobotTwin.CoreSim.Design.Esp32Parts.EmulatorGap("// WiFi.h is not used here"));
        }

        [Fact]
        public void SarAdcConvertsThePadAsAnalogReadDoes()
        {
            var soc = Chip();
            soc.PadVoltage = gpio => gpio == 34 ? 1.0 : 0;
            soc.Write32(0x3FF48800, 3u << 16 | 1u << 28);   // SAR_READ_CTRL: 12 bits, DATA_INV
            soc.Write32(0x3FF48834, 3u << 12);              // SAR_ATTEN1: channel 6 at 11 dB
            uint pad = 1u << (19 + 6) | 1u << 31;           // EN_PAD channel 6 (GPIO34), forced
            soc.Write32(0x3FF48854, pad);
            Assert.Equal(0u, soc.Read32(0x3FF48854) & (1u << 16));
            soc.Write32(0x3FF48854, pad | 1u << 17);        // START
            uint result = soc.Read32(0x3FF48854);
            Assert.NotEqual(0u, result & (1u << 16));      // DONE
            Assert.Equal((uint)Esp32Soc.CharacterisedCode(0, 3, soc.AdcVrefMillivolts, 1000), result & 0xFFFF);
            Assert.InRange(Esp32Soc.CharacterisedMillivolts(0, 3, soc.AdcVrefMillivolts, (int)(result & 0xFFFF)), 998, 1002);
        }
    }
}
