#pragma once

#include <array>
#include <cstdint>
#include <string>
#include <vector>

extern "C"
{
#include "MCU/ATmega328P_ISA.h"
}

namespace firmware
{
    struct McuState
    {
        std::vector<std::uint8_t> flash;
        std::vector<std::uint8_t> sram;
        std::vector<std::uint8_t> eeprom;
        std::vector<std::uint8_t> io;
        std::vector<std::uint8_t> regs;
        AvrCore core{};
    };

    class VirtualArduino
    {
    public:
        VirtualArduino();

        bool LoadBvm(const std::vector<std::uint8_t>& buffer, std::string& error);
        void Reset();
        void StepCycles(std::uint64_t cycles);

        void SetInputPin(int pin, int value);
        void SyncInputs();

        std::uint8_t GetIo(std::uint8_t address) const;
        std::uint64_t TickCount() const { return _tickCount; }

        void SnapshotPorts(std::uint8_t& portb, std::uint8_t& portc, std::uint8_t& portd,
                           std::uint8_t& ddrb, std::uint8_t& ddrc, std::uint8_t& ddrd) const;

        bool ConsumeSerialByte(std::uint8_t& outByte);

    private:
        bool LoadTextSection(const std::uint8_t* data, std::size_t size, std::uint64_t flags, std::string& error);
        bool ParseBvmText(const std::vector<std::uint8_t>& buffer, const std::uint8_t*& data,
                          std::size_t& size, std::uint64_t& flags, std::string& error);
        bool PinToPort(int pin, std::uint8_t& ddr, std::uint8_t& port, std::uint8_t& pinReg, std::uint8_t& bit);

        McuState _state;
        std::array<int, 20> _pinInputs{};
        std::uint64_t _tickCount = 0;
        bool _serialPending = false;
        std::uint8_t _serialByte = 0;
    };
}
