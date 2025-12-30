#include "VirtualArduino.h"

#include <cstring>

#include "Core/HexLoader.h"

namespace firmware
{
    namespace
    {
        constexpr std::uint32_t BvmMagic = 0x43534E45; // "CSNE"
        constexpr std::uint64_t SectionTextHex = 1ull << 3;
        constexpr std::uint64_t SectionTextRaw = 1ull << 4;
        constexpr std::size_t FlashSize = 0x8000;
        constexpr std::size_t SramSize = 0x0900;
        constexpr std::size_t EepromSize = 0x0400;
        constexpr std::size_t IoSize = 0x100;
        constexpr std::size_t RegSize = 32;
        constexpr std::uint8_t Udr0Address = 0xC6;

        struct BvmHeader
        {
            std::uint32_t magic;
            std::uint16_t version_major;
            std::uint16_t version_minor;
            std::uint32_t header_size;
            std::uint32_t section_count;
            std::uint64_t entry_offset;
            std::uint64_t section_table_offset;
            std::uint64_t flags;
            std::uint64_t reserved0;
            std::uint64_t reserved1;
            std::uint64_t reserved2;
        };

        struct BvmSection
        {
            char name[8];
            std::uint64_t offset;
            std::uint64_t size;
            std::uint64_t flags;
            std::uint64_t reserved;
        };

        bool SectionNameEquals(const BvmSection& section, const char* name)
        {
            return std::strncmp(section.name, name, 8) == 0;
        }
    }

    VirtualArduino::VirtualArduino()
    {
        _state.flash.resize(FlashSize, 0);
        _state.sram.resize(SramSize, 0);
        _state.eeprom.resize(EepromSize, 0);
        _state.io.resize(IoSize, 0);
        _state.regs.resize(RegSize, 0);
        _pinInputs.fill(0);
        Reset();
    }

    void VirtualArduino::Reset()
    {
        std::fill(_state.flash.begin(), _state.flash.end(), 0);
        std::fill(_state.sram.begin(), _state.sram.end(), 0);
        std::fill(_state.eeprom.begin(), _state.eeprom.end(), 0);
        std::fill(_state.io.begin(), _state.io.end(), 0);
        std::fill(_state.regs.begin(), _state.regs.end(), 0);
        _tickCount = 0;
        _serialPending = false;
        _serialByte = 0;

        AVR_Init(&_state.core,
                 _state.flash.data(), _state.flash.size(),
                 _state.sram.data(), _state.sram.size(),
                 _state.io.data(), _state.io.size(),
                 _state.regs.data(), _state.regs.size());
    }

    bool VirtualArduino::LoadBvm(const std::vector<std::uint8_t>& buffer, std::string& error)
    {
        const std::uint8_t* data = nullptr;
        std::size_t size = 0;
        std::uint64_t flags = 0;
        if (!ParseBvmText(buffer, data, size, flags, error))
        {
            return false;
        }
        Reset();
        return LoadTextSection(data, size, flags, error);
    }

    bool VirtualArduino::ParseBvmText(const std::vector<std::uint8_t>& buffer, const std::uint8_t*& data,
                                      std::size_t& size, std::uint64_t& flags, std::string& error)
    {
        if (buffer.size() < sizeof(BvmHeader))
        {
            error = "BVM buffer too small";
            return false;
        }
        auto* header = reinterpret_cast<const BvmHeader*>(buffer.data());
        if (header->magic != BvmMagic)
        {
            error = "Invalid BVM magic";
            return false;
        }
        if (header->section_table_offset + header->section_count * sizeof(BvmSection) > buffer.size())
        {
            error = "BVM section table out of bounds";
            return false;
        }

        auto* sections = reinterpret_cast<const BvmSection*>(buffer.data() + header->section_table_offset);
        for (std::uint32_t i = 0; i < header->section_count; ++i)
        {
            const auto& section = sections[i];
            if (!SectionNameEquals(section, ".text")) continue;
            if (section.offset + section.size > buffer.size())
            {
                error = "BVM text section out of bounds";
                return false;
            }
            data = buffer.data() + section.offset;
            size = static_cast<std::size_t>(section.size);
            flags = section.flags;
            return true;
        }

        error = "BVM missing .text section";
        return false;
    }

    bool VirtualArduino::LoadTextSection(const std::uint8_t* data, std::size_t size, std::uint64_t flags, std::string& error)
    {
        if (!data || size == 0)
        {
            error = "Empty firmware section";
            return false;
        }

        if ((flags & SectionTextHex) != 0)
        {
            std::string hex(reinterpret_cast<const char*>(data), size);
            if (!NativeEngine::Utils::HexLoader::LoadHexText(_state.flash, hex.c_str()))
            {
                error = "Failed to parse Intel HEX section";
                return false;
            }
            return true;
        }

        if ((flags & SectionTextRaw) != 0 || flags == 0)
        {
            std::size_t copySize = size > _state.flash.size() ? _state.flash.size() : size;
            std::memcpy(_state.flash.data(), data, copySize);
            return true;
        }

        error = "Unsupported BVM text flags";
        return false;
    }

    void VirtualArduino::StepCycles(std::uint64_t cycles)
    {
        while (cycles > 0)
        {
            std::uint8_t cost = AVR_ExecuteNext(&_state.core);
            if (cost == 0) cost = 1;
            cycles = (cost > cycles) ? 0 : (cycles - cost);
            _tickCount += cost;
        }

        std::uint8_t current = GetIo(Udr0Address);
        if (!_serialPending && current != 0)
        {
            _serialByte = current;
            _serialPending = true;
            AVR_IoWrite(&_state.core, Udr0Address, 0);
        }
    }

    void VirtualArduino::SetInputPin(int pin, int value)
    {
        if (pin < 0 || pin >= static_cast<int>(_pinInputs.size())) return;
        _pinInputs[pin] = value ? 1 : 0;
    }

    void VirtualArduino::SyncInputs()
    {
        std::uint8_t ddrb = GetIo(AVR_DDRB);
        std::uint8_t ddrc = GetIo(AVR_DDRC);
        std::uint8_t ddrd = GetIo(AVR_DDRD);
        std::uint8_t portb = GetIo(AVR_PORTB);
        std::uint8_t portc = GetIo(AVR_PORTC);
        std::uint8_t portd = GetIo(AVR_PORTD);

        std::uint8_t pinb = 0;
        for (int bit = 0; bit < 6; ++bit)
        {
            bool isOutput = (ddrb & (1u << bit)) != 0;
            bool value = isOutput ? ((portb & (1u << bit)) != 0) : (_pinInputs[8 + bit] != 0);
            if (value) pinb |= (1u << bit);
        }

        std::uint8_t pinc = 0;
        for (int bit = 0; bit < 6; ++bit)
        {
            bool isOutput = (ddrc & (1u << bit)) != 0;
            bool value = isOutput ? ((portc & (1u << bit)) != 0) : (_pinInputs[14 + bit] != 0);
            if (value) pinc |= (1u << bit);
        }

        std::uint8_t pind = 0;
        for (int bit = 0; bit < 8; ++bit)
        {
            bool isOutput = (ddrd & (1u << bit)) != 0;
            bool value = isOutput ? ((portd & (1u << bit)) != 0) : (_pinInputs[bit] != 0);
            if (value) pind |= (1u << bit);
        }

        AVR_IoWrite(&_state.core, AVR_PINB, pinb);
        AVR_IoWrite(&_state.core, AVR_PINC, pinc);
        AVR_IoWrite(&_state.core, AVR_PIND, pind);
    }

    std::uint8_t VirtualArduino::GetIo(std::uint8_t address) const
    {
        return AVR_IoRead(const_cast<AvrCore*>(&_state.core), address);
    }

    void VirtualArduino::SnapshotPorts(std::uint8_t& portb, std::uint8_t& portc, std::uint8_t& portd,
                                       std::uint8_t& ddrb, std::uint8_t& ddrc, std::uint8_t& ddrd) const
    {
        portb = GetIo(AVR_PORTB);
        portc = GetIo(AVR_PORTC);
        portd = GetIo(AVR_PORTD);
        ddrb = GetIo(AVR_DDRB);
        ddrc = GetIo(AVR_DDRC);
        ddrd = GetIo(AVR_DDRD);
    }

    bool VirtualArduino::ConsumeSerialByte(std::uint8_t& outByte)
    {
        if (!_serialPending) return false;
        outByte = _serialByte;
        _serialPending = false;
        _serialByte = 0;
        return true;
    }

    bool VirtualArduino::PinToPort(int pin, std::uint8_t& ddr, std::uint8_t& port, std::uint8_t& pinReg, std::uint8_t& bit)
    {
        if (pin < 0) return false;
        if (pin <= 7)
        {
            ddr = AVR_DDRD;
            port = AVR_PORTD;
            pinReg = AVR_PIND;
            bit = static_cast<std::uint8_t>(pin);
            return true;
        }
        if (pin <= 13)
        {
            ddr = AVR_DDRB;
            port = AVR_PORTB;
            pinReg = AVR_PINB;
            bit = static_cast<std::uint8_t>(pin - 8);
            return true;
        }
        if (pin <= 19)
        {
            ddr = AVR_DDRC;
            port = AVR_PORTC;
            pinReg = AVR_PINC;
            bit = static_cast<std::uint8_t>(pin - 14);
            return true;
        }
        return false;
    }
}
