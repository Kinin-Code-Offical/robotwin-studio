#include "MCU/ATmega328P_ISA.h"

uint8_t AVR_IoRead(AvrCore* core, uint8_t address);
void AVR_IoWrite(AvrCore* core, uint8_t address, uint8_t value);

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

static void AVR_UpdateSPRegisters(AvrCore* core)
{
    uint8_t spl = (uint8_t)(core->sp & 0xFF);
    uint8_t sph = (uint8_t)((core->sp >> 8) & 0xFF);
    AVR_IoWrite(core, AVR_SPL, spl);
    AVR_IoWrite(core, AVR_SPH, sph);
}

static void AVR_SetSP(AvrCore* core, uint16_t value)
{
    core->sp = value;
    AVR_UpdateSPRegisters(core);
}

static uint16_t AVR_GetSP(AvrCore* core)
{
    return core->sp;
}

static uint8_t AVR_ReadData(AvrCore* core, uint16_t address)
{
    if (address < core->regs_size)
    {
        return core->regs[address];
    }
    if (address >= AVR_IO_BASE && address < (AVR_IO_BASE + core->io_size))
    {
        size_t idx = (size_t)(address - AVR_IO_BASE);
        return core->io[idx];
    }
    if (address >= AVR_SRAM_START)
    {
        size_t idx = (size_t)(address - AVR_SRAM_START);
        if (idx < core->sram_size)
        {
            return core->sram[idx];
        }
    }
    return 0;
}

static void AVR_WriteData(AvrCore* core, uint16_t address, uint8_t value)
{
    if (address < core->regs_size)
    {
        core->regs[address] = value;
        return;
    }
    if (address >= AVR_IO_BASE && address < (AVR_IO_BASE + core->io_size))
    {
        AVR_IoWrite(core, (uint8_t)address, value);
        return;
    }
    if (address >= AVR_SRAM_START)
    {
        size_t idx = (size_t)(address - AVR_SRAM_START);
        if (idx < core->sram_size)
        {
            core->sram[idx] = value;
        }
    }
}

static void AVR_Push(AvrCore* core, uint8_t value)
{
    uint16_t sp = AVR_GetSP(core);
    if (sp > 0)
    {
        sp--;
        AVR_WriteData(core, sp, value);
        AVR_SetSP(core, sp);
    }
}

static uint8_t AVR_Pop(AvrCore* core)
{
    uint16_t sp = AVR_GetSP(core);
    uint8_t value = AVR_ReadData(core, sp);
    sp++;
    AVR_SetSP(core, sp);
    return value;
}

static uint16_t AVR_GetRegWord(AvrCore* core, uint8_t index)
{
    if (index + 1 >= core->regs_size)
    {
        return 0;
    }
    uint8_t lo = core->regs[index];
    uint8_t hi = core->regs[index + 1];
    return (uint16_t)(lo | (hi << 8));
}

static void AVR_SetRegWord(AvrCore* core, uint8_t index, uint16_t value)
{
    if (index + 1 >= core->regs_size)
    {
        return;
    }
    core->regs[index] = (uint8_t)(value & 0xFF);
    core->regs[index + 1] = (uint8_t)((value >> 8) & 0xFF);
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
    core->carry_flag = 0;
    core->sp = (uint16_t)(AVR_SRAM_START + (uint16_t)sram_size - 1);
    AVR_UpdateSPRegisters(core);
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
    if (address == AVR_SPL)
    {
        core->sp = (uint16_t)((core->sp & 0xFF00) | value);
    }
    else if (address == AVR_SPH)
    {
        core->sp = (uint16_t)((core->sp & 0x00FF) | ((uint16_t)value << 8));
    }
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
    if ((opcode & 0xF800) == 0xB800)
    {
        uint8_t a = (uint8_t)((opcode & 0x0F) | ((opcode >> 5) & 0x30));
        uint8_t r = (uint8_t)((opcode >> 4) & 0x1F);
        if (r < core->regs_size)
        {
            AVR_IoWrite(core, (uint8_t)(AVR_IO_BASE + a), core->regs[r]);
        }
        return 1;
    }
    if ((opcode & 0xF800) == 0xB000)
    {
        uint8_t a = (uint8_t)((opcode & 0x0F) | ((opcode >> 5) & 0x30));
        uint8_t d = (uint8_t)((opcode >> 4) & 0x1F);
        if (d < core->regs_size)
        {
            core->regs[d] = AVR_IoRead(core, (uint8_t)(AVR_IO_BASE + a));
        }
        return 1;
    }
    if ((opcode & 0xFE0F) == 0x9000)
    {
        uint8_t d = (uint8_t)((opcode >> 4) & 0x1F);
        uint16_t addr = AVR_FetchWord(core);
        if (d < core->regs_size)
        {
            core->regs[d] = AVR_ReadData(core, addr);
        }
        return 2;
    }
    if ((opcode & 0xFE0F) == 0x9200)
    {
        uint8_t r = (uint8_t)((opcode >> 4) & 0x1F);
        uint16_t addr = AVR_FetchWord(core);
        if (r < core->regs_size)
        {
            AVR_WriteData(core, addr, core->regs[r]);
        }
        return 2;
    }
    if ((opcode & 0xFC00) == 0x2C00)
    {
        uint8_t d = (uint8_t)((opcode >> 4) & 0x1F);
        uint8_t r = (uint8_t)((opcode & 0x0F) | ((opcode >> 5) & 0x10));
        if (d < core->regs_size && r < core->regs_size)
        {
            core->regs[d] = core->regs[r];
        }
        return 1;
    }
    if ((opcode & 0xFF00) == 0x0100)
    {
        uint8_t d = (uint8_t)((opcode >> 4) & 0x0F);
        uint8_t r = (uint8_t)(opcode & 0x0F);
        uint8_t dIndex = (uint8_t)(d * 2);
        uint8_t rIndex = (uint8_t)(r * 2);
        if (dIndex + 1 < core->regs_size && rIndex + 1 < core->regs_size)
        {
            core->regs[dIndex] = core->regs[rIndex];
            core->regs[dIndex + 1] = core->regs[rIndex + 1];
        }
        return 1;
    }
    if ((opcode & 0xFC00) == 0x2400)
    {
        uint8_t d = (uint8_t)((opcode >> 4) & 0x1F);
        uint8_t r = (uint8_t)((opcode & 0x0F) | ((opcode >> 5) & 0x10));
        if (d < core->regs_size && r < core->regs_size)
        {
            uint8_t value = (uint8_t)(core->regs[d] ^ core->regs[r]);
            core->regs[d] = value;
            core->zero_flag = (uint8_t)(value == 0);
        }
        return 1;
    }
    if ((opcode & 0xF000) == 0x7000)
    {
        uint8_t d = (uint8_t)(16 + ((opcode >> 4) & 0x0F));
        uint8_t k = (uint8_t)((opcode & 0x0F) | ((opcode >> 4) & 0xF0));
        if (d < core->regs_size)
        {
            uint8_t value = (uint8_t)(core->regs[d] & k);
            core->regs[d] = value;
            core->zero_flag = (uint8_t)(value == 0);
        }
        return 1;
    }
    if ((opcode & 0xF000) == 0x6000)
    {
        uint8_t d = (uint8_t)(16 + ((opcode >> 4) & 0x0F));
        uint8_t k = (uint8_t)((opcode & 0x0F) | ((opcode >> 4) & 0xF0));
        if (d < core->regs_size)
        {
            uint8_t value = (uint8_t)(core->regs[d] | k);
            core->regs[d] = value;
            core->zero_flag = (uint8_t)(value == 0);
        }
        return 1;
    }
    if ((opcode & 0xF000) == 0x5000)
    {
        uint8_t d = (uint8_t)(16 + ((opcode >> 4) & 0x0F));
        uint8_t k = (uint8_t)((opcode & 0x0F) | ((opcode >> 4) & 0xF0));
        if (d < core->regs_size)
        {
            uint16_t lhs = core->regs[d];
            uint16_t rhs = k;
            uint16_t result = (uint16_t)(lhs - rhs);
            core->regs[d] = (uint8_t)result;
            core->zero_flag = (uint8_t)((uint8_t)result == 0);
            core->carry_flag = (uint8_t)(lhs < rhs);
        }
        return 1;
    }
    if ((opcode & 0xF000) == 0x4000)
    {
        uint8_t d = (uint8_t)(16 + ((opcode >> 4) & 0x0F));
        uint8_t k = (uint8_t)((opcode & 0x0F) | ((opcode >> 4) & 0xF0));
        if (d < core->regs_size)
        {
            uint16_t lhs = core->regs[d];
            uint16_t rhs = (uint16_t)k + (core->carry_flag ? 1 : 0);
            uint16_t result = (uint16_t)(lhs - rhs);
            core->regs[d] = (uint8_t)result;
            core->zero_flag = (uint8_t)((uint8_t)result == 0);
            core->carry_flag = (uint8_t)(lhs < rhs);
        }
        return 1;
    }
    if ((opcode & 0xF000) == 0x3000)
    {
        uint8_t d = (uint8_t)(16 + ((opcode >> 4) & 0x0F));
        uint8_t k = (uint8_t)((opcode & 0x0F) | ((opcode >> 4) & 0xF0));
        if (d < core->regs_size)
        {
            uint16_t lhs = core->regs[d];
            uint16_t rhs = k;
            uint16_t result = (uint16_t)(lhs - rhs);
            core->zero_flag = (uint8_t)((uint8_t)result == 0);
            core->carry_flag = (uint8_t)(lhs < rhs);
        }
        return 1;
    }
    if ((opcode & 0xFC00) == 0x1400)
    {
        uint8_t d = (uint8_t)((opcode >> 4) & 0x1F);
        uint8_t r = (uint8_t)((opcode & 0x0F) | ((opcode >> 5) & 0x10));
        if (d < core->regs_size && r < core->regs_size)
        {
            uint16_t lhs = core->regs[d];
            uint16_t rhs = core->regs[r];
            uint16_t result = (uint16_t)(lhs - rhs);
            core->zero_flag = (uint8_t)((uint8_t)result == 0);
            core->carry_flag = (uint8_t)(lhs < rhs);
        }
        return 1;
    }
    if ((opcode & 0xFC00) == 0x0C00)
    {
        uint8_t d = (uint8_t)((opcode >> 4) & 0x1F);
        uint8_t r = (uint8_t)((opcode & 0x0F) | ((opcode >> 5) & 0x10));
        if (d < core->regs_size && r < core->regs_size)
        {
            uint16_t sum = (uint16_t)core->regs[d] + (uint16_t)core->regs[r];
            core->regs[d] = (uint8_t)sum;
            core->zero_flag = (uint8_t)((uint8_t)sum == 0);
            core->carry_flag = (uint8_t)(sum > 0xFF);
        }
        return 1;
    }
    if ((opcode & 0xFC00) == 0x1C00)
    {
        uint8_t d = (uint8_t)((opcode >> 4) & 0x1F);
        uint8_t r = (uint8_t)((opcode & 0x0F) | ((opcode >> 5) & 0x10));
        if (d < core->regs_size && r < core->regs_size)
        {
            uint16_t sum = (uint16_t)core->regs[d] + (uint16_t)core->regs[r] + (core->carry_flag ? 1 : 0);
            core->regs[d] = (uint8_t)sum;
            core->zero_flag = (uint8_t)((uint8_t)sum == 0);
            core->carry_flag = (uint8_t)(sum > 0xFF);
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
    if ((opcode & 0xFE0F) == 0x920F)
    {
        uint8_t r = (uint8_t)((opcode >> 4) & 0x1F);
        if (r < core->regs_size)
        {
            AVR_Push(core, core->regs[r]);
        }
        return 2;
    }
    if ((opcode & 0xFE0F) == 0x900F)
    {
        uint8_t d = (uint8_t)((opcode >> 4) & 0x1F);
        if (d < core->regs_size)
        {
            core->regs[d] = AVR_Pop(core);
        }
        return 2;
    }
    if ((opcode & 0xF000) == 0xD000)
    {
        int16_t k = (int16_t)(opcode & 0x0FFF);
        if (k & 0x0800)
        {
            k = (int16_t)(k | 0xF000);
        }
        uint16_t returnAddr = core->pc;
        AVR_Push(core, (uint8_t)(returnAddr & 0xFF));
        AVR_Push(core, (uint8_t)((returnAddr >> 8) & 0xFF));
        core->pc = (uint16_t)(core->pc + k);
        return 3;
    }
    if ((opcode & 0xFE0E) == 0x940E)
    {
        uint16_t addr = AVR_FetchWord(core);
        uint16_t returnAddr = core->pc;
        AVR_Push(core, (uint8_t)(returnAddr & 0xFF));
        AVR_Push(core, (uint8_t)((returnAddr >> 8) & 0xFF));
        core->pc = addr;
        return 4;
    }
    if (opcode == 0x9508)
    {
        uint8_t high = AVR_Pop(core);
        uint8_t low = AVR_Pop(core);
        core->pc = (uint16_t)(low | (high << 8));
        return 4;
    }
    if ((opcode & 0xFF00) == 0x9600)
    {
        uint8_t d = (uint8_t)((opcode >> 4) & 0x03);
        uint8_t k = (uint8_t)((opcode & 0x0F) | ((opcode >> 2) & 0x30));
        uint8_t index = (uint8_t)(24 + (d * 2));
        uint16_t value = AVR_GetRegWord(core, index);
        value = (uint16_t)(value + k);
        AVR_SetRegWord(core, index, value);
        core->zero_flag = (uint8_t)(value == 0);
        return 2;
    }
    if ((opcode & 0xFF00) == 0x9700)
    {
        uint8_t d = (uint8_t)((opcode >> 4) & 0x03);
        uint8_t k = (uint8_t)((opcode & 0x0F) | ((opcode >> 2) & 0x30));
        uint8_t index = (uint8_t)(24 + (d * 2));
        uint16_t value = AVR_GetRegWord(core, index);
        value = (uint16_t)(value - k);
        AVR_SetRegWord(core, index, value);
        core->zero_flag = (uint8_t)(value == 0);
        return 2;
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
    if ((opcode & 0xFC07) == 0xF001)
    {
        int8_t k = (int8_t)((opcode >> 3) & 0x7F);
        if (k & 0x40)
        {
            k = (int8_t)(k | 0x80);
        }
        if (core->zero_flag)
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
