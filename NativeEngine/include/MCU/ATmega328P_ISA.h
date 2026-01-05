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
    AVR_PINB     = 0x23,
    AVR_DDRB     = 0x24,
    AVR_PORTB    = 0x25,
    AVR_PINC     = 0x26,
    AVR_DDRC     = 0x27,
    AVR_PORTC    = 0x28,
    AVR_PIND     = 0x29,
    AVR_DDRD     = 0x2A,
    AVR_PORTD    = 0x2B,
    AVR_SPL      = 0x3D,
    AVR_SPH      = 0x3E,
    AVR_SREG     = 0x3F,
    AVR_TIFR0    = 0x35,
    AVR_TIFR1    = 0x36,
    AVR_TIFR2    = 0x37,
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
    AVR_UCSR0A   = 0xC0,
    AVR_UCSR0B   = 0xC1,
    AVR_UCSR0C   = 0xC2,
    AVR_UBRR0L   = 0xC4,
    AVR_UBRR0H   = 0xC5,
    AVR_UDR0     = 0xC6,
    AVR_TIMSK0   = 0x6E,
    AVR_TIMSK1   = 0x6F,
    AVR_TIMSK2   = 0x70
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
    void (*io_write_hook)(struct AvrCore* core, uint8_t address, uint8_t value, void* user);
    void* io_read_user;
    void (*io_read_hook)(struct AvrCore* core, uint8_t address, uint8_t value, void* user);
} AvrCore;

void AVR_Init(AvrCore* core,
              uint8_t* flash, size_t flash_size,
              uint8_t* sram, size_t sram_size,
              volatile uint8_t* io, size_t io_size,
              uint8_t* regs, size_t regs_size);
void AVR_SetIoWriteHook(AvrCore* core, void (*hook)(AvrCore* core, uint8_t address, uint8_t value, void* user), void* user);
void AVR_SetIoReadHook(AvrCore* core, void (*hook)(AvrCore* core, uint8_t address, uint8_t value, void* user), void* user);

uint8_t AVR_ExecuteNext(AvrCore* core);
uint8_t AVR_IoRead(AvrCore* core, uint8_t address);
void AVR_IoWrite(AvrCore* core, uint8_t address, uint8_t value);
void AVR_IoSetBit(AvrCore* core, uint8_t address, uint8_t bit, uint8_t state);
uint8_t AVR_IoGetBit(AvrCore* core, uint8_t address, uint8_t bit);

#ifdef __cplusplus
}
#endif
