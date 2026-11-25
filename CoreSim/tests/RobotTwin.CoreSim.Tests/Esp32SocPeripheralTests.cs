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
