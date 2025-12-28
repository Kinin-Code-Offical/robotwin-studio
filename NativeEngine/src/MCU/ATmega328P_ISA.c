#include "MCU/ATmega328P_ISA.h"

static uint16_t AVR_FetchWord(AvrCore* core)
{
    size_t index = (size_t)core->pc * 2;
    if (index + 1 >= core->flash_size)
    {
        return 0x0000;
    }
    uint16_t opcode = (uint16_t)(core->flash[index] | (core->flash[index + 1] << 8));
    core->pc++;
    return opcode;
}

void AVR_Init(AvrCore* core,
              uint8_t* flash, size_t flash_size,
              uint8_t* sram, size_t sram_size,
              volatile uint8_t* io, size_t io_size,
              uint8_t* regs, size_t regs_size)
{
    core->flash = flash;
    core->flash_size = flash_size;
    core->sram = sram;
    core->sram_size = sram_size;
    core->io = io;
    core->io_size = io_size;
    core->regs = regs;
    core->regs_size = regs_size;
    core->pc = 0;
    core->zero_flag = 0;
}

uint8_t AVR_IoRead(AvrCore* core, uint8_t address)
{
    size_t idx = (size_t)(address - AVR_IO_BASE);
    if (idx >= core->io_size) return 0;
    return core->io[idx];
}

void AVR_IoWrite(AvrCore* core, uint8_t address, uint8_t value)
{
    size_t idx = (size_t)(address - AVR_IO_BASE);
    if (idx >= core->io_size) return;
    core->io[idx] = value;
}

void AVR_IoSetBit(AvrCore* core, uint8_t address, uint8_t bit, uint8_t state)
{
    uint8_t value = AVR_IoRead(core, address);
    if (state)
    {
        value = (uint8_t)(value | (1u << bit));
    }
    else
    {
        value = (uint8_t)(value & ~(1u << bit));
    }
    AVR_IoWrite(core, address, value);
}

uint8_t AVR_IoGetBit(AvrCore* core, uint8_t address, uint8_t bit)
{
    uint8_t value = AVR_IoRead(core, address);
    return (uint8_t)((value & (1u << bit)) != 0u);
}

uint8_t AVR_ExecuteNext(AvrCore* core)
{
    uint16_t opcode = AVR_FetchWord(core);
    if (opcode == 0x0000)
    {
        return 1;
    }
    if ((opcode & 0xF000) == 0xE000)
    {
        uint8_t d = (uint8_t)(16 + ((opcode >> 4) & 0x0F));
        uint8_t k = (uint8_t)((opcode & 0x0F) | ((opcode >> 4) & 0xF0));
        if (d < core->regs_size)
        {
            core->regs[d] = k;
        }
        return 1;
    }
    if ((opcode & 0xFF00) == 0x9A00)
    {
        uint8_t a = (uint8_t)((opcode >> 3) & 0x1F);
        uint8_t b = (uint8_t)(opcode & 0x07);
        AVR_IoSetBit(core, (uint8_t)(AVR_IO_BASE + a), b, 1);
        return 2;
    }
    if ((opcode & 0xFF00) == 0x9800)
    {
        uint8_t a = (uint8_t)((opcode >> 3) & 0x1F);
        uint8_t b = (uint8_t)(opcode & 0x07);
        AVR_IoSetBit(core, (uint8_t)(AVR_IO_BASE + a), b, 0);
        return 2;
    }
    if ((opcode & 0xFE0F) == 0x940A)
    {
        uint8_t d = (uint8_t)((opcode >> 4) & 0x1F);
        if (d < core->regs_size)
        {
            uint8_t value = (uint8_t)(core->regs[d] - 1);
            core->regs[d] = value;
            core->zero_flag = (uint8_t)(value == 0);
        }
        return 1;
    }
    if ((opcode & 0xFC07) == 0xF401)
    {
        int8_t k = (int8_t)((opcode >> 3) & 0x7F);
        if (k & 0x40)
        {
            k = (int8_t)(k | 0x80);
        }
        if (!core->zero_flag)
        {
            core->pc = (uint16_t)(core->pc + k);
            return 2;
        }
        return 1;
    }
    if ((opcode & 0xF000) == 0xC000)
    {
        int16_t k = (int16_t)(opcode & 0x0FFF);
        if (k & 0x0800)
        {
            k = (int16_t)(k | 0xF000);
        }
        core->pc = (uint16_t)(core->pc + k);
        return 2;
    }
    return 1;
}
