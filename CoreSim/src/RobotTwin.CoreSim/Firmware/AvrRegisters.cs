using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RobotTwin.CoreSim.Firmware
{
    /// <summary>[registers] One I/O register of the ATmega328P: its name, data-space address, group and bit names (bit 7 first).</summary>
    public sealed class AvrRegister
    {
        public AvrRegister(string name, int address, string group, string? bits = null)
        {
            Name = name;
            Address = address;
            Group = group;
            Bits = bits?.Split(' ');
        }

        public string Name { get; }

        /// <summary>Address in the data space (the I/O space plus 0x20), as the core reads it.</summary>
        public int Address { get; }

        public string Group { get; }

        /// <summary>The eight bit names, bit 7 first ("-" for a reserved bit); null when the register is a plain value.</summary>
        public string[]? Bits { get; }

        /// <summary>"0x2A".</summary>
        public string Hex(byte v) => "0x" + v.ToString("X2", CultureInfo.InvariantCulture);

        /// <summary>"0010 1101".</summary>
        public static string Binary(byte v)
        {
            string s = Convert.ToString(v, 2).PadLeft(8, '0');
            return s.Substring(0, 4) + " " + s.Substring(4);
        }

        /// <summary>The set bits by name ("I Z C"), or "" when none is set or the register has no bit names.</summary>
        public string SetBits(byte v)
        {
            if (Bits == null) return string.Empty;
            var set = new List<string>();
            for (int i = 0; i < 8; i++)
                if ((v & (1 << (7 - i))) != 0 && Bits[i] != "-") set.Add(Bits[i]);
            return string.Join(" ", set);
        }
    }

    /// <summary>
    /// [registers] The ATmega328P's I/O registers as the datasheet names them (section 30, register summary), by the
    /// peripheral they belong to — what the debugger's Registers tab shows from the core's data space, with the flags of
    /// the ones whose bits matter at a glance (SREG, UCSR0A, ADCSRA, TWCR, SPCR, the interrupt masks).
    /// </summary>
    public static class AvrRegisters
    {
        public static IReadOnlyList<AvrRegister> Atmega328P { get; } = new[]
        {
            new AvrRegister("SREG", 0x5F, "Çekirdek", "I T H S V N Z C"),
            new AvrRegister("SPH", 0x5E, "Çekirdek"),
            new AvrRegister("SPL", 0x5D, "Çekirdek"),
            new AvrRegister("MCUCR", 0x55, "Çekirdek", "- BODS BODSE PUD - - IVSEL IVCE"),
            new AvrRegister("MCUSR", 0x54, "Çekirdek", "- - - - WDRF BORF EXTRF PORF"),
            new AvrRegister("SMCR", 0x53, "Çekirdek", "- - - - SM2 SM1 SM0 SE"),
            new AvrRegister("WDTCSR", 0x60, "Çekirdek", "WDIF WDIE WDP3 WDCE WDE WDP2 WDP1 WDP0"),
            new AvrRegister("PRR", 0x64, "Çekirdek", "PRTWI PRTIM2 PRTIM0 - PRTIM1 PRSPI PRUSART0 PRADC"),
            new AvrRegister("PINB", 0x23, "Port B", "PB7 PB6 PB5 PB4 PB3 PB2 PB1 PB0"),
            new AvrRegister("DDRB", 0x24, "Port B", "PB7 PB6 PB5 PB4 PB3 PB2 PB1 PB0"),
            new AvrRegister("PORTB", 0x25, "Port B", "PB7 PB6 PB5 PB4 PB3 PB2 PB1 PB0"),
            new AvrRegister("PINC", 0x26, "Port C", "- PC6 PC5 PC4 PC3 PC2 PC1 PC0"),
            new AvrRegister("DDRC", 0x27, "Port C", "- PC6 PC5 PC4 PC3 PC2 PC1 PC0"),
            new AvrRegister("PORTC", 0x28, "Port C", "- PC6 PC5 PC4 PC3 PC2 PC1 PC0"),
            new AvrRegister("PIND", 0x29, "Port D", "PD7 PD6 PD5 PD4 PD3 PD2 PD1 PD0"),
            new AvrRegister("DDRD", 0x2A, "Port D", "PD7 PD6 PD5 PD4 PD3 PD2 PD1 PD0"),
            new AvrRegister("PORTD", 0x2B, "Port D", "PD7 PD6 PD5 PD4 PD3 PD2 PD1 PD0"),
            new AvrRegister("TCCR0A", 0x44, "Zamanlayıcı 0", "COM0A1 COM0A0 COM0B1 COM0B0 - - WGM01 WGM00"),
            new AvrRegister("TCCR0B", 0x45, "Zamanlayıcı 0", "FOC0A FOC0B - - WGM02 CS02 CS01 CS00"),
            new AvrRegister("TCNT0", 0x46, "Zamanlayıcı 0"),
            new AvrRegister("OCR0A", 0x47, "Zamanlayıcı 0"),
            new AvrRegister("OCR0B", 0x48, "Zamanlayıcı 0"),
            new AvrRegister("TIMSK0", 0x6E, "Zamanlayıcı 0", "- - - - - OCIE0B OCIE0A TOIE0"),
            new AvrRegister("TIFR0", 0x35, "Zamanlayıcı 0", "- - - - - OCF0B OCF0A TOV0"),
            new AvrRegister("TCCR1A", 0x80, "Zamanlayıcı 1", "COM1A1 COM1A0 COM1B1 COM1B0 - - WGM11 WGM10"),
            new AvrRegister("TCCR1B", 0x81, "Zamanlayıcı 1", "ICNC1 ICES1 - WGM13 WGM12 CS12 CS11 CS10"),
            new AvrRegister("TCNT1L", 0x84, "Zamanlayıcı 1"),
            new AvrRegister("TCNT1H", 0x85, "Zamanlayıcı 1"),
            new AvrRegister("ICR1L", 0x86, "Zamanlayıcı 1"),
            new AvrRegister("ICR1H", 0x87, "Zamanlayıcı 1"),
            new AvrRegister("OCR1AL", 0x88, "Zamanlayıcı 1"),
            new AvrRegister("OCR1AH", 0x89, "Zamanlayıcı 1"),
            new AvrRegister("OCR1BL", 0x8A, "Zamanlayıcı 1"),
            new AvrRegister("OCR1BH", 0x8B, "Zamanlayıcı 1"),
            new AvrRegister("TIMSK1", 0x6F, "Zamanlayıcı 1", "- - ICIE1 - - OCIE1B OCIE1A TOIE1"),
            new AvrRegister("TIFR1", 0x36, "Zamanlayıcı 1", "- - ICF1 - - OCF1B OCF1A TOV1"),
            new AvrRegister("TCCR2A", 0xB0, "Zamanlayıcı 2", "COM2A1 COM2A0 COM2B1 COM2B0 - - WGM21 WGM20"),
            new AvrRegister("TCCR2B", 0xB1, "Zamanlayıcı 2", "FOC2A FOC2B - - WGM22 CS22 CS21 CS20"),
            new AvrRegister("TCNT2", 0xB2, "Zamanlayıcı 2"),
            new AvrRegister("OCR2A", 0xB3, "Zamanlayıcı 2"),
            new AvrRegister("OCR2B", 0xB4, "Zamanlayıcı 2"),
            new AvrRegister("TIMSK2", 0x70, "Zamanlayıcı 2", "- - - - - OCIE2B OCIE2A TOIE2"),
            new AvrRegister("TIFR2", 0x37, "Zamanlayıcı 2", "- - - - - OCF2B OCF2A TOV2"),
            new AvrRegister("EICRA", 0x69, "Kesmeler", "- - - - ISC11 ISC10 ISC01 ISC00"),
            new AvrRegister("EIMSK", 0x3D, "Kesmeler", "- - - - - - INT1 INT0"),
            new AvrRegister("EIFR", 0x3C, "Kesmeler", "- - - - - - INTF1 INTF0"),
            new AvrRegister("PCICR", 0x68, "Kesmeler", "- - - - - PCIE2 PCIE1 PCIE0"),
            new AvrRegister("PCIFR", 0x3B, "Kesmeler", "- - - - - PCIF2 PCIF1 PCIF0"),
            new AvrRegister("PCMSK0", 0x6B, "Kesmeler"),
            new AvrRegister("PCMSK1", 0x6C, "Kesmeler"),
            new AvrRegister("PCMSK2", 0x6D, "Kesmeler"),
            new AvrRegister("ADMUX", 0x7C, "ADC", "REFS1 REFS0 ADLAR - MUX3 MUX2 MUX1 MUX0"),
            new AvrRegister("ADCSRA", 0x7A, "ADC", "ADEN ADSC ADATE ADIF ADIE ADPS2 ADPS1 ADPS0"),
            new AvrRegister("ADCSRB", 0x7B, "ADC", "- ACME - - - ADTS2 ADTS1 ADTS0"),
            new AvrRegister("ADCL", 0x78, "ADC"),
            new AvrRegister("ADCH", 0x79, "ADC"),
            new AvrRegister("DIDR0", 0x7E, "ADC", "- - ADC5D ADC4D ADC3D ADC2D ADC1D ADC0D"),
            new AvrRegister("UCSR0A", 0xC0, "USART0", "RXC0 TXC0 UDRE0 FE0 DOR0 UPE0 U2X0 MPCM0"),
            new AvrRegister("UCSR0B", 0xC1, "USART0", "RXCIE0 TXCIE0 UDRIE0 RXEN0 TXEN0 UCSZ02 RXB80 TXB80"),
            new AvrRegister("UCSR0C", 0xC2, "USART0", "UMSEL01 UMSEL00 UPM01 UPM00 USBS0 UCSZ01 UCSZ00 UCPOL0"),
            new AvrRegister("UBRR0L", 0xC4, "USART0"),
            new AvrRegister("UBRR0H", 0xC5, "USART0"),
            new AvrRegister("UDR0", 0xC6, "USART0"),
            new AvrRegister("SPCR", 0x4C, "SPI", "SPIE SPE DORD MSTR CPOL CPHA SPR1 SPR0"),
            new AvrRegister("SPSR", 0x4D, "SPI", "SPIF WCOL - - - - - SPI2X"),
            new AvrRegister("SPDR", 0x4E, "SPI"),
            new AvrRegister("TWBR", 0xB8, "TWI (I²C)"),
            new AvrRegister("TWSR", 0xB9, "TWI (I²C)", "TWS7 TWS6 TWS5 TWS4 TWS3 - TWPS1 TWPS0"),
            new AvrRegister("TWAR", 0xBA, "TWI (I²C)"),
            new AvrRegister("TWDR", 0xBB, "TWI (I²C)"),
            new AvrRegister("TWCR", 0xBC, "TWI (I²C)", "TWINT TWEA TWSTA TWSTO TWWC TWEN - TWIE"),
            new AvrRegister("EECR", 0x3F, "EEPROM", "- - EEPM1 EEPM0 EERIE EEMPE EEPE EERE"),
            new AvrRegister("EEDR", 0x40, "EEPROM"),
            new AvrRegister("EEARL", 0x41, "EEPROM"),
            new AvrRegister("EEARH", 0x42, "EEPROM"),
        };

        public static AvrRegister? Find(string name) => Atmega328P.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

        private static readonly string[] VectorNames =
        {
            "RESET", "INT0", "INT1", "PCINT0", "PCINT1", "PCINT2", "WDT", "TIMER2_COMPA", "TIMER2_COMPB", "TIMER2_OVF",
            "TIMER1_CAPT", "TIMER1_COMPA", "TIMER1_COMPB", "TIMER1_OVF", "TIMER0_COMPA", "TIMER0_COMPB", "TIMER0_OVF",
            "SPI_STC", "USART_RX", "USART_UDRE", "USART_TX", "ADC", "EE_READY", "ANALOG_COMP", "TWI", "SPM_READY",
        };

        /// <summary>[timeline] An interrupt vector's name as avr-libc gives it (16 → "TIMER0_OVF").</summary>
        public static string VectorName(int vector) => vector >= 0 && vector < VectorNames.Length ? VectorNames[vector] : "#" + vector;

        /// <summary>[timeline] An Arduino pin's name (0–13 → "D0"…"D13", 14–19 → "A0"…"A5").</summary>
        public static string PinName(int pin) => pin < 14 ? "D" + pin : "A" + (pin - 14);

        /// <summary>The low byte of a 16-bit pair given its high byte (TCNT1H → TCNT1L, SPH → SPL), or null.</summary>
        public static AvrRegister? LowHalf(AvrRegister high) =>
            high.Name.Length > 1 && high.Name.EndsWith("H", StringComparison.Ordinal) ? Find(high.Name.Substring(0, high.Name.Length - 1) + "L") : null;

        /// <summary>SREG as its eight flags, a letter where set and "-" where clear ("I-----Z-").</summary>
        public static string Flags(byte sreg)
        {
            const string names = "ITHSVNZC";
            var c = new char[8];
            for (int i = 0; i < 8; i++) c[i] = (sreg & (1 << (7 - i))) != 0 ? names[i] : '-';
            return new string(c);
        }
    }
}
