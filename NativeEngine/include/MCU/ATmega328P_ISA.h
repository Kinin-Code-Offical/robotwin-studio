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
    AVR_PIND     = 0x29,
    AVR_DDRD     = 0x2A,
    AVR_PORTD    = 0x2B,
    AVR_ADMUX    = 0x7A,
    AVR_ADCSRA   = 0x7A
};

typedef struct
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
} AvrCore;

void AVR_Init(AvrCore* core,
              uint8_t* flash, size_t flash_size,
              uint8_t* sram, size_t sram_size,
              volatile uint8_t* io, size_t io_size,
              uint8_t* regs, size_t regs_size);

uint8_t AVR_ExecuteNext(AvrCore* core);
uint8_t AVR_IoRead(AvrCore* core, uint8_t address);
void AVR_IoWrite(AvrCore* core, uint8_t address, uint8_t value);
void AVR_IoSetBit(AvrCore* core, uint8_t address, uint8_t bit, uint8_t state);
uint8_t AVR_IoGetBit(AvrCore* core, uint8_t address, uint8_t bit);

#ifdef __cplusplus
}
#endif
