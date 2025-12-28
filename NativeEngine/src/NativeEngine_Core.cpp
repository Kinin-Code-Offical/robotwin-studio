#include <array>
#include <cstdint>
#include <cstddef>
#include <cmath>
#include <cstring>
#include <fstream>
#include <string>
#include <algorithm>
#include <vector>
#include "Core/MemoryMap.hpp"
#include "Core/BvmFormat.hpp"
#include "Core/NodalSolver.hpp"
#include "MCU/ATmega328P_ISA.h"
#include "Bridge/UnityInterface.h"

namespace
{
    constexpr double LOGIC_HIGH = 5.0;
    constexpr double LOGIC_LOW = 0.0;

    constexpr std::size_t NODE_GND = 0;
    constexpr std::size_t NODE_VCC = 1;
    constexpr std::size_t NODE_D13 = 2;
    constexpr std::size_t NODE_LED = 3;

    constexpr std::uint32_t ERROR_LED_OVERCURRENT = 1u << 0;
    constexpr std::uint32_t ERROR_RESISTOR_OVERPOWER = 1u << 1;

    struct EngineState
    {
        core::VirtualMemory memory{};
        AvrCore cpu{};
        SharedState shared{};
        bool initialized = false;
    };

    EngineState g_engine;

    std::uint16_t EncodeLDI(std::uint8_t reg, std::uint8_t imm)
    {
        std::uint8_t d = static_cast<std::uint8_t>(reg - 16);
        return static_cast<std::uint16_t>(0xE000 | ((imm & 0xF0) << 4) | (d << 4) | (imm & 0x0F));
    }

    std::uint16_t EncodeSBI(std::uint8_t io_addr, std::uint8_t bit)
    {
        std::uint8_t a = static_cast<std::uint8_t>(io_addr - AVR_IO_BASE);
        return static_cast<std::uint16_t>(0x9A00 | ((a & 0x1F) << 3) | (bit & 0x07));
    }

    std::uint16_t EncodeCBI(std::uint8_t io_addr, std::uint8_t bit)
    {
        std::uint8_t a = static_cast<std::uint8_t>(io_addr - AVR_IO_BASE);
        return static_cast<std::uint16_t>(0x9800 | ((a & 0x1F) << 3) | (bit & 0x07));
    }

    std::uint16_t EncodeDEC(std::uint8_t reg)
    {
        std::uint16_t d = static_cast<std::uint16_t>(reg & 0x1F);
        return static_cast<std::uint16_t>(0x940A | ((d & 0x10) << 4) | ((d & 0x0F) << 4));
    }

    std::uint16_t EncodeBRNE(std::int8_t k)
    {
        std::uint8_t kk = static_cast<std::uint8_t>(k);
        return static_cast<std::uint16_t>(0xF401 | ((kk & 0x7F) << 3));
    }

    std::uint16_t EncodeRJMP(std::int16_t k)
    {
        return static_cast<std::uint16_t>(0xC000 | (k & 0x0FFF));
    }

    void EmitWord(core::VirtualMemory& mem, std::size_t& word_index, std::uint16_t opcode)
    {
        std::size_t byte_index = word_index * 2;
        if (byte_index + 1 >= core::FLASH_SIZE) return;
        mem.flash[byte_index] = static_cast<std::uint8_t>(opcode & 0xFF);
        mem.flash[byte_index + 1] = static_cast<std::uint8_t>(opcode >> 8);
        word_index++;
    }

    void BuildDefaultBlinkProgram(core::VirtualMemory& mem)
    {
        std::size_t w = 0;
        EmitWord(mem, w, EncodeSBI(AVR_DDRB, 5));

        std::size_t loop_start = w;
        EmitWord(mem, w, EncodeSBI(AVR_PORTB, 5));
        EmitWord(mem, w, EncodeLDI(16, 0xFF));
        std::size_t delay1 = w;
        EmitWord(mem, w, EncodeDEC(16));
        EmitWord(mem, w, EncodeBRNE(static_cast<std::int8_t>(delay1 - w - 1)));

        EmitWord(mem, w, EncodeCBI(AVR_PORTB, 5));
        EmitWord(mem, w, EncodeLDI(16, 0xFF));
        std::size_t delay2 = w;
        EmitWord(mem, w, EncodeDEC(16));
        EmitWord(mem, w, EncodeBRNE(static_cast<std::int8_t>(delay2 - w - 1)));

        EmitWord(mem, w, EncodeRJMP(static_cast<std::int16_t>(loop_start - w - 1)));
    }

    void UpdateSharedState(EngineState& state, const solver::BlinkResult& result)
    {
        state.shared.node_voltages[NODE_GND] = static_cast<float>(result.v_gnd);
        state.shared.node_voltages[NODE_VCC] = static_cast<float>(result.v_vcc);
        state.shared.node_voltages[NODE_D13] = static_cast<float>(result.v_d13);
        state.shared.node_voltages[NODE_LED] = static_cast<float>(result.v_led);
        state.shared.currents[0] = static_cast<float>(result.i_res);
        state.shared.currents[1] = static_cast<float>(result.i_led);

        std::uint32_t errors = 0;
        if (result.led_over) errors |= ERROR_LED_OVERCURRENT;
        if (result.resistor_over) errors |= ERROR_RESISTOR_OVERPOWER;
        state.shared.error_flags = errors;
    }

    void UpdatePinVoltages(EngineState& state)
    {
        for (std::size_t i = 0; i < MAX_PINS; ++i)
        {
            state.shared.pin_voltages[i] = 0.0f;
        }

        for (int i = 0; i <= 7; ++i)
        {
            bool is_output = AVR_IoGetBit(&state.cpu, AVR_DDRD, static_cast<uint8_t>(i)) != 0;
            bool is_high = AVR_IoGetBit(&state.cpu, AVR_PORTD, static_cast<uint8_t>(i)) != 0;
            state.shared.pin_voltages[i] = (is_output && is_high) ? static_cast<float>(LOGIC_HIGH) : static_cast<float>(LOGIC_LOW);
        }

        for (int i = 0; i <= 5; ++i)
        {
            bool is_output = AVR_IoGetBit(&state.cpu, AVR_DDRB, static_cast<uint8_t>(i)) != 0;
            bool is_high = AVR_IoGetBit(&state.cpu, AVR_PORTB, static_cast<uint8_t>(i)) != 0;
            state.shared.pin_voltages[8 + i] = (is_output && is_high) ? static_cast<float>(LOGIC_HIGH) : static_cast<float>(LOGIC_LOW);
        }

        for (int i = 0; i <= 5; ++i)
        {
            bool is_output = AVR_IoGetBit(&state.cpu, AVR_DDRC, static_cast<uint8_t>(i)) != 0;
            bool is_high = AVR_IoGetBit(&state.cpu, AVR_PORTC, static_cast<uint8_t>(i)) != 0;
            state.shared.pin_voltages[14 + i] = (is_output && is_high) ? static_cast<float>(LOGIC_HIGH) : static_cast<float>(LOGIC_LOW);
        }
    }

    void SolveCircuit(EngineState& state)
    {
        bool ddrb5 = AVR_IoGetBit(&state.cpu, AVR_DDRB, 5) != 0;
        bool portb5 = AVR_IoGetBit(&state.cpu, AVR_PORTB, 5) != 0;
        double vpin = (ddrb5 && portb5) ? LOGIC_HIGH : LOGIC_LOW;

        solver::BlinkParams params;
        params.logic_high = LOGIC_HIGH;
        params.logic_low = LOGIC_LOW;
        auto result = solver::SolveBlink(vpin, params);
        UpdateSharedState(state, result);
        UpdatePinVoltages(state);
    }

    void InitEngine(EngineState& state)
    {
        std::fill(state.memory.flash.begin(), state.memory.flash.end(), 0);
        std::fill(state.memory.sram.begin(), state.memory.sram.end(), 0);
        std::fill(state.memory.eeprom.begin(), state.memory.eeprom.end(), 0);
        std::fill(state.memory.regs.begin(), state.memory.regs.end(), 0);
        std::memset((void*)state.memory.io, 0, sizeof(state.memory.io));

        AVR_Init(&state.cpu,
                 state.memory.flash.data(), state.memory.flash.size(),
                 state.memory.sram.data(), state.memory.sram.size(),
                 state.memory.io, core::IO_SIZE,
                 state.memory.regs.data(), state.memory.regs.size());

        BuildDefaultBlinkProgram(state.memory);

        state.shared.component_positions[0][0] = 100;
        state.shared.component_positions[0][1] = 200;
        state.shared.component_positions[1][0] = 400;
        state.shared.component_positions[1][1] = 200;
        state.shared.component_positions[2][0] = 550;
        state.shared.component_positions[2][1] = 200;

        state.shared.node_voltages[NODE_GND] = static_cast<float>(LOGIC_LOW);
        state.shared.node_voltages[NODE_VCC] = static_cast<float>(LOGIC_HIGH);
        state.shared.node_voltages[NODE_D13] = static_cast<float>(LOGIC_LOW);
        state.shared.node_voltages[NODE_LED] = static_cast<float>(LOGIC_LOW);
        for (std::size_t i = 0; i < MAX_PINS; ++i)
        {
            state.shared.pin_voltages[i] = 0.0f;
        }
        state.shared.currents[0] = 0.0f;
        state.shared.currents[1] = 0.0f;
        state.shared.error_flags = 0;
        state.shared.tick = 0;

        state.initialized = true;
    }

    bool ParseHexNibble(char c, std::uint8_t& out)
    {
        if (c >= '0' && c <= '9') { out = static_cast<std::uint8_t>(c - '0'); return true; }
        if (c >= 'A' && c <= 'F') { out = static_cast<std::uint8_t>(c - 'A' + 10); return true; }
        if (c >= 'a' && c <= 'f') { out = static_cast<std::uint8_t>(c - 'a' + 10); return true; }
        return false;
    }

    bool ParseHexByte(const char* ptr, std::uint8_t& out)
    {
        std::uint8_t hi = 0;
        std::uint8_t lo = 0;
        if (!ParseHexNibble(ptr[0], hi)) return false;
        if (!ParseHexNibble(ptr[1], lo)) return false;
        out = static_cast<std::uint8_t>((hi << 4) | lo);
        return true;
    }

    bool LoadHexText(core::VirtualMemory& mem, const char* text)
    {
        if (text == nullptr) return false;
        std::uint32_t upper = 0;
        const char* line = text;
        while (*line != '\0')
        {
            if (*line == '\r' || *line == '\n') { ++line; continue; }
            if (*line != ':') return false;
            const char* ptr = line + 1;
            std::uint8_t len = 0;
            std::uint8_t addr_hi = 0;
            std::uint8_t addr_lo = 0;
            std::uint8_t type = 0;
            if (!ParseHexByte(ptr, len)) return false; ptr += 2;
            if (!ParseHexByte(ptr, addr_hi)) return false; ptr += 2;
            if (!ParseHexByte(ptr, addr_lo)) return false; ptr += 2;
            if (!ParseHexByte(ptr, type)) return false; ptr += 2;
            std::uint32_t addr = (static_cast<std::uint32_t>(addr_hi) << 8) | addr_lo;
            std::uint8_t checksum = static_cast<std::uint8_t>(len + addr_hi + addr_lo + type);
            if (type == 0x00)
            {
                for (std::uint8_t i = 0; i < len; ++i)
                {
                    std::uint8_t data = 0;
                    if (!ParseHexByte(ptr, data)) return false;
                    ptr += 2;
                    checksum = static_cast<std::uint8_t>(checksum + data);
                    std::uint32_t final_addr = (upper << 16) + addr + i;
                    if (final_addr >= core::FLASH_SIZE) return false;
                    mem.flash[final_addr] = data;
                }
            }
            else if (type == 0x04)
            {
                std::uint8_t up_hi = 0;
                std::uint8_t up_lo = 0;
                if (!ParseHexByte(ptr, up_hi)) return false; ptr += 2;
                if (!ParseHexByte(ptr, up_lo)) return false; ptr += 2;
                checksum = static_cast<std::uint8_t>(checksum + up_hi + up_lo);
                upper = (static_cast<std::uint32_t>(up_hi) << 8) | up_lo;
            }
            else if (type == 0x01)
            {
                ptr += len * 2;
            }
            else
            {
                for (std::uint8_t i = 0; i < len; ++i)
                {
                    std::uint8_t data = 0;
                    if (!ParseHexByte(ptr, data)) return false;
                    ptr += 2;
                    checksum = static_cast<std::uint8_t>(checksum + data);
                }
            }

            std::uint8_t read_checksum = 0;
            if (!ParseHexByte(ptr, read_checksum)) return false;
            checksum = static_cast<std::uint8_t>(checksum + read_checksum);
            if (checksum != 0) return false;

            while (*ptr != '\0' && *ptr != '\n') ++ptr;
            line = ptr;
            if (type == 0x01) break;
        }
        return true;
    }

    bool LoadHexFile(core::VirtualMemory& mem, const char* path)
    {
        if (path == nullptr || path[0] == '\0') return false;
        std::ifstream file(path, std::ios::in | std::ios::binary);
        if (!file.is_open()) return false;
        std::string content((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
        if (content.empty()) return false;
        return LoadHexText(mem, content.c_str());
    }

    bool LoadRawBinary(core::VirtualMemory& mem, const std::uint8_t* data, std::size_t size)
    {
        if (!data || size == 0) return false;
        if (size > core::FLASH_SIZE) return false;
        std::fill(mem.flash.begin(), mem.flash.end(), 0);
        std::memcpy(mem.flash.data(), data, size);
        return true;
    }

    bool LoadBvmBuffer(EngineState& state, const std::uint8_t* buffer, std::size_t size)
    {
        bvm::BvmView view;
        const char* error = nullptr;
        if (!bvm::Open(buffer, size, view, &error))
        {
            return false;
        }

        bvm::SectionView text{};
        if (!bvm::FindSection(view, ".text", text))
        {
            return false;
        }

        bool loaded = false;
        if ((text.flags & bvm::SectionTextHex) != 0)
        {
            const char* text_data = reinterpret_cast<const char*>(text.data);
            loaded = LoadHexText(state.memory, text_data);
        }
        else
        {
            loaded = LoadRawBinary(state.memory, text.data, static_cast<std::size_t>(text.size));
        }

        if (!loaded) return false;

        bvm::SectionView data{};
        if (bvm::FindSection(view, ".data", data))
        {
            std::size_t count = static_cast<std::size_t>(data.size);
            if (count > core::SRAM_SIZE) count = core::SRAM_SIZE;
            std::memcpy(state.memory.sram.data(), data.data, count);
        }

        state.cpu.pc = static_cast<std::uint16_t>(view.header->entry_offset / 2u);
        state.cpu.zero_flag = 0;
        return true;
    }

    bool LoadBvmFile(EngineState& state, const char* path)
    {
        if (path == nullptr || path[0] == '\0') return false;
        std::ifstream file(path, std::ios::in | std::ios::binary);
        if (!file.is_open()) return false;
        std::vector<std::uint8_t> buffer((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
        if (buffer.empty()) return false;
        return LoadBvmBuffer(state, buffer.data(), buffer.size());
    }

    void ExecuteCycles(EngineState& state, std::uint64_t cycles)
    {
        while (cycles > 0)
        {
            std::uint8_t cost = AVR_ExecuteNext(&state.cpu);
            if (cost == 0) cost = 1;
            for (std::uint8_t i = 0; i < cost && cycles > 0; ++i)
            {
                SolveCircuit(state);
                state.shared.tick++;
                cycles--;
            }
        }
    }
}

extern "C"
{
    UNITY_EXPORT void StepSimulation(float dt)
    {
        if (!g_engine.initialized)
        {
            InitEngine(g_engine);
        }
        if (dt <= 0.0f) return;
        std::uint64_t cycles = static_cast<std::uint64_t>(dt * 16000000.0);
        if (cycles == 0) cycles = 1;
        ExecuteCycles(g_engine, cycles);
    }

    UNITY_EXPORT void StepSimulationTicks(std::uint64_t ticks)
    {
        if (!g_engine.initialized)
        {
            InitEngine(g_engine);
        }
        if (ticks == 0) return;
        ExecuteCycles(g_engine, ticks);
    }

    UNITY_EXPORT int GetEngineVersion(void)
    {
        return 210;
    }

    UNITY_EXPORT float CalculateCurrent(float voltage, float resistance)
    {
        if (std::abs(resistance) < 1e-6f) return 0.0f;
        return static_cast<float>(voltage / resistance);
    }

    UNITY_EXPORT const SharedState* GetSharedState(void)
    {
        if (!g_engine.initialized)
        {
            InitEngine(g_engine);
        }
        return &g_engine.shared;
    }

    UNITY_EXPORT void SetComponentXY(std::uint32_t index, std::uint32_t x, std::uint32_t y)
    {
        if (!g_engine.initialized)
        {
            InitEngine(g_engine);
        }
        if (index >= MAX_COMPONENTS) return;
        g_engine.shared.component_positions[index][0] = x;
        g_engine.shared.component_positions[index][1] = y;
    }

    UNITY_EXPORT int LoadHexFromText(const char* text)
    {
        if (!g_engine.initialized)
        {
            InitEngine(g_engine);
        }
        bool loaded = LoadHexText(g_engine.memory, text);
        if (loaded)
        {
            g_engine.cpu.pc = 0;
            g_engine.cpu.zero_flag = 0;
        }
        return loaded ? 1 : 0;
    }

    UNITY_EXPORT int LoadHexFromFile(const char* path)
    {
        if (!g_engine.initialized)
        {
            InitEngine(g_engine);
        }
        bool loaded = LoadHexFile(g_engine.memory, path);
        if (loaded)
        {
            g_engine.cpu.pc = 0;
            g_engine.cpu.zero_flag = 0;
        }
        return loaded ? 1 : 0;
    }

    UNITY_EXPORT int LoadBvmFromMemory(const std::uint8_t* buffer, std::uint32_t size)
    {
        if (!g_engine.initialized)
        {
            InitEngine(g_engine);
        }
        if (!buffer || size == 0) return 0;
        return LoadBvmBuffer(g_engine, buffer, size) ? 1 : 0;
    }

    UNITY_EXPORT int LoadBvmFromFile(const char* path)
    {
        if (!g_engine.initialized)
        {
            InitEngine(g_engine);
        }
        return LoadBvmFile(g_engine, path) ? 1 : 0;
    }
}
