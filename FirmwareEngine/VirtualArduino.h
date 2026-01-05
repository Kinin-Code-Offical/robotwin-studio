#pragma once

#include <cstdint>
#include <deque>
#include <string>
#include <vector>

#include "BoardProfile.h"

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
        explicit VirtualArduino(const BoardProfile& profile);

        bool LoadBvm(const std::vector<std::uint8_t>& buffer, std::string& error);
        void Reset();
        void StepCycles(std::uint64_t cycles);

        void SetInputPin(int pin, int value);
        void SyncInputs();

        std::uint8_t GetIo(std::uint8_t address) const;
        std::uint64_t TickCount() const { return _tickCount; }
        int PinCount() const { return _pinCount; }

        void SnapshotPorts(std::uint8_t& portb, std::uint8_t& portc, std::uint8_t& portd,
                           std::uint8_t& ddrb, std::uint8_t& ddrc, std::uint8_t& ddrd) const;

        bool ConsumeSerialByte(std::uint8_t& outByte);
        void LoadEepromFromFile(const std::string& path);
        void SaveEepromToFile(const std::string& path) const;
        void SetAnalogInput(int channel, float voltage);
        void QueueSerialInput(std::uint8_t value);

    private:
        bool LoadTextSection(const std::uint8_t* data, std::size_t size, std::uint64_t flags, std::string& error);
        bool ParseBvmText(const std::vector<std::uint8_t>& buffer, const std::uint8_t*& data,
                          std::size_t& size, std::uint64_t& flags, std::string& error);
        bool PinToPort(int pin, std::uint8_t& ddr, std::uint8_t& port, std::uint8_t& pinReg, std::uint8_t& bit);
        void HandleUartWrite(std::uint8_t value);
        void SimulateTimer0(std::uint64_t cycles);
        void SimulateTimer1(std::uint64_t cycles);
        void SimulateTimer2(std::uint64_t cycles);
        bool MeasureHexMaxAddress(const std::string& hexText, std::size_t& outMax);
        static void IoWriteHook(AvrCore* core, std::uint8_t address, std::uint8_t value, void* user);
        static void IoReadHook(AvrCore* core, std::uint8_t address, std::uint8_t value, void* user);
        double ComputeUartCyclesPerByte() const;
        bool IsUartRxEnabled() const;
        bool IsUartTxEnabled() const;

        McuState _state;
        BoardProfile _profile;
        int _pinCount = 0;
        std::vector<int> _pinInputs;
        std::vector<float> _analogInputs;
        double _timer0Remainder = 0.0;
        double _timer1Remainder = 0.0;
        double _timer2Remainder = 0.0;
        double _adcCyclesRemaining = 0.0;
        std::uint32_t _adcNoiseSeed = 0x1234567u;
        std::deque<std::uint8_t> _uartRxQueue;
        bool _uartRxReady = false;
        double _uartRxCyclesRemaining = 0.0;
        std::uint64_t _tickCount = 0;
        std::deque<std::uint8_t> _uartTxQueue;
        std::deque<std::uint8_t> _uartTxPending;
        bool _uartTxActive = false;
        double _uartTxCyclesRemaining = 0.0;
        std::uint8_t _uartTxByte = 0;
        std::size_t _uartQueueLimit = 2048;
    };
}
