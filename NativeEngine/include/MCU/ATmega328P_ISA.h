#pragma once
#include <stdint.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

enum
{
    AVR_FLASH_START = 0x0000,
    AVR_FLASH_END   = 0x7FFF,
    AVR_SRAM_START  = 0x0100,
    AVR_SRAM_END    = 0x08FF,
    AVR_EEPROM_START= 0x0000,
    AVR_EEPROM_END  = 0x03FF
};

enum
{
    AVR_IO_BASE  = 0x20,
    AVR_PINA     = 0x20,
    AVR_DDRA     = 0x21,
    AVR_PORTA    = 0x22,
    AVR_PINB     = 0x23,
    AVR_DDRB     = 0x24,
    AVR_PORTB    = 0x25,
    AVR_PINC     = 0x26,
    AVR_DDRC     = 0x27,
    AVR_PORTC    = 0x28,
    AVR_PIND     = 0x29,
    AVR_DDRD     = 0x2A,
    AVR_PORTD    = 0x2B,
    AVR_PINE     = 0x2C,
    AVR_DDRE     = 0x2D,
    AVR_PORTE    = 0x2E,
    AVR_PINF     = 0x2F,
    AVR_DDRF     = 0x30,
    AVR_PORTF    = 0x31,
    AVR_PING     = 0x32,
    AVR_DDRG     = 0x33,
    AVR_PORTG    = 0x34,
    AVR_PINH     = 0x100,
    AVR_DDRH     = 0x101,
    AVR_PORTH    = 0x102,
    AVR_PINJ     = 0x103,
    AVR_DDRJ     = 0x104,
    AVR_PORTJ    = 0x105,
    AVR_PINK     = 0x106,
    AVR_DDRK     = 0x107,
    AVR_PORTK    = 0x108,
    AVR_PINL     = 0x109,
    AVR_DDRL     = 0x10A,
    AVR_PORTL    = 0x10B,
    AVR_SPL      = 0x3D,
    AVR_SPH      = 0x3E,
    AVR_SREG     = 0x3F,
    AVR_TIFR0    = 0x35,
    AVR_TIFR1    = 0x36,
    AVR_TIFR2    = 0x37,
    AVR_TIFR3    = 0x38,
    AVR_TIFR4    = 0x39,
    AVR_TIFR5    = 0x3A,
    AVR_PCIFR    = 0x3B,
    AVR_EIFR     = 0x1C,
    AVR_EIMSK    = 0x1D,
    AVR_EICRA    = 0x69,
    AVR_EICRB    = 0x6A,
    AVR_PCICR    = 0x68,
    AVR_PCMSK0   = 0x6B,
    AVR_PCMSK1   = 0x6C,
    AVR_PCMSK2   = 0x6D,
    AVR_WDTCSR   = 0x60,
    AVR_ADCL     = 0x78,
    AVR_ADCH     = 0x79,
    AVR_ADCSRA   = 0x7A,
    AVR_ADCSRB   = 0x7B,
    AVR_ADMUX    = 0x7C,
    AVR_TCCR0A   = 0x44,
    AVR_TCCR0B   = 0x45,
    AVR_TCNT0    = 0x46,
    AVR_OCR0A    = 0x47,
    AVR_OCR0B    = 0x48,
    AVR_TCCR1A   = 0x80,
    AVR_TCCR1B   = 0x81,
    AVR_TCNT1L   = 0x84,
    AVR_TCNT1H   = 0x85,
    AVR_OCR1AL   = 0x88,
    AVR_OCR1AH   = 0x89,
    AVR_OCR1BL   = 0x8A,
    AVR_OCR1BH   = 0x8B,
    AVR_TCCR2A   = 0xB0,
    AVR_TCCR2B   = 0xB1,
    AVR_TCNT2    = 0xB2,
    AVR_OCR2A    = 0xB3,
    AVR_OCR2B    = 0xB4,
    AVR_TCCR3A   = 0x90,
    AVR_TCCR3B   = 0x91,
    AVR_TCCR3C   = 0x92,
    AVR_TCNT3L   = 0x94,
    AVR_TCNT3H   = 0x95,
    AVR_OCR3AL   = 0x98,
    AVR_OCR3AH   = 0x99,
    AVR_OCR3BL   = 0x9A,
    AVR_OCR3BH   = 0x9B,
    AVR_OCR3CL   = 0x9C,
    AVR_OCR3CH   = 0x9D,
    AVR_TCCR4A   = 0xA0,
    AVR_TCCR4B   = 0xA1,
    AVR_TCCR4C   = 0xA2,
    AVR_TCNT4L   = 0xA4,
    AVR_TCNT4H   = 0xA5,
    AVR_OCR4AL   = 0xA8,
    AVR_OCR4AH   = 0xA9,
    AVR_OCR4BL   = 0xAA,
    AVR_OCR4BH   = 0xAB,
    AVR_OCR4CL   = 0xAC,
    AVR_OCR4CH   = 0xAD,
    AVR_TCCR5A   = 0x120,
    AVR_TCCR5B   = 0x121,
    AVR_TCCR5C   = 0x122,
    AVR_TCNT5L   = 0x124,
    AVR_TCNT5H   = 0x125,
    AVR_OCR5AL   = 0x128,
    AVR_OCR5AH   = 0x129,
    AVR_OCR5BL   = 0x12A,
    AVR_OCR5BH   = 0x12B,
    AVR_OCR5CL   = 0x12C,
    AVR_OCR5CH   = 0x12D,
    AVR_UCSR0A   = 0xC0,
    AVR_UCSR0B   = 0xC1,
    AVR_UCSR0C   = 0xC2,
    AVR_UBRR0L   = 0xC4,
    AVR_UBRR0H   = 0xC5,
    AVR_UDR0     = 0xC6,
    AVR_UCSR1A   = 0xC8,
    AVR_UCSR1B   = 0xC9,
    AVR_UCSR1C   = 0xCA,
    AVR_UBRR1L   = 0xCC,
    AVR_UBRR1H   = 0xCD,
    AVR_UDR1     = 0xCE,
    AVR_UCSR2A   = 0xD0,
    AVR_UCSR2B   = 0xD1,
    AVR_UCSR2C   = 0xD2,
    AVR_UBRR2L   = 0xD4,
    AVR_UBRR2H   = 0xD5,
    AVR_UDR2     = 0xD6,
    AVR_UCSR3A   = 0x130,
    AVR_UCSR3B   = 0x131,
    AVR_UCSR3C   = 0x132,
    AVR_UBRR3L   = 0x134,
    AVR_UBRR3H   = 0x135,
    AVR_UDR3     = 0x136,
    AVR_SPCR     = 0x4C,
    AVR_SPSR     = 0x4D,
    AVR_SPDR     = 0x4E,
    AVR_TWBR     = 0xB8,
    AVR_TWSR     = 0xB9,
    AVR_TWAR     = 0xBA,
    AVR_TWDR     = 0xBB,
    AVR_TWCR     = 0xBC,
    AVR_TWAMR    = 0xBD,
    AVR_TIMSK0   = 0x6E,
    AVR_TIMSK1   = 0x6F,
    AVR_TIMSK2   = 0x70,
    AVR_TIMSK3   = 0x71,
    AVR_TIMSK4   = 0x72,
    AVR_TIMSK5   = 0x73
};

typedef struct AvrCore
{
    uint8_t* flash;
    size_t flash_size;
    uint8_t* sram;
    size_t sram_size;
    volatile uint8_t* io;
    size_t io_size;
    uint8_t* regs;
    size_t regs_size;
    uint16_t pc;
    uint8_t zero_flag;
    uint8_t carry_flag;
    uint16_t sp;
    void* io_write_user;
    void (*io_write_hook)(struct AvrCore* core, uint16_t address, uint8_t value, void* user);
    void* io_read_user;
    void (*io_read_hook)(struct AvrCore* core, uint16_t address, uint8_t value, void* user);
    uint8_t mcu_kind;
} AvrCore;

enum
{
    AVR_MCU_328P = 0,
    AVR_MCU_2560 = 1
};

void AVR_Init(AvrCore* core,
              uint8_t* flash, size_t flash_size,
              uint8_t* sram, size_t sram_size,
              volatile uint8_t* io, size_t io_size,
              uint8_t* regs, size_t regs_size);
void AVR_SetMcuKind(AvrCore* core, uint8_t mcu_kind);
void AVR_SetIoWriteHook(AvrCore* core, void (*hook)(AvrCore* core, uint16_t address, uint8_t value, void* user), void* user);
void AVR_SetIoReadHook(AvrCore* core, void (*hook)(AvrCore* core, uint16_t address, uint8_t value, void* user), void* user);

uint8_t AVR_ExecuteNext(AvrCore* core);
uint8_t AVR_IoRead(AvrCore* core, uint16_t address);
void AVR_IoWrite(AvrCore* core, uint16_t address, uint8_t value);
void AVR_IoSetBit(AvrCore* core, uint16_t address, uint8_t bit, uint8_t state);
uint8_t AVR_IoGetBit(AvrCore* core, uint16_t address, uint8_t bit);

#ifdef __cplusplus
}
#endif
