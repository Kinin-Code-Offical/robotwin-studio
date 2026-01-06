#pragma once

#include <array>
#include <cstdint>
#include <deque>
#include <string>
#include <vector>

#include "BoardProfile.h"
#include "Protocol.h"

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

    class VirtualMcu
    {
    public:
        struct PerfCounters
        {
            std::uint64_t cycles = 0;
            std::uint64_t adcSamples = 0;
            std::uint64_t uartTxBytes[4] = {0, 0, 0, 0};
            std::uint64_t uartRxBytes[4] = {0, 0, 0, 0};
            std::uint64_t spiTransfers = 0;
            std::uint64_t twiTransfers = 0;
            std::uint64_t wdtResets = 0;
        };

        explicit VirtualMcu(const BoardProfile &profile);

        bool LoadBvm(const std::vector<std::uint8_t> &buffer, std::string &error);
        void Reset();
        void SoftReset();
        void StepCycles(std::uint64_t cycles);

        bool EraseFlash(std::string &error);
        bool ProgramFlash(std::uint32_t byteAddress, const std::uint8_t *data, std::size_t size, std::string &error);
        bool ReadFlash(std::uint32_t byteAddress, std::uint8_t *outData, std::size_t size, std::string &error) const;

        void SetInputPin(int pin, int value);
        void SyncInputs();

        std::uint8_t GetIo(std::uint16_t address) const;
        void SetIo(std::uint16_t address, std::uint8_t value);
        std::uint64_t TickCount() const { return _tickCount; }
        int PinCount() const { return _pinCount; }
        const PerfCounters &GetPerfCounters() const { return _perf; }

        void SnapshotPorts(std::uint8_t &portb, std::uint8_t &portc, std::uint8_t &portd,
                           std::uint8_t &ddrb, std::uint8_t &ddrc, std::uint8_t &ddrd) const;

        // Fills outPins with per-pin digital output states.
        // - outPins[i] = 0 or 1 when the pin is configured as OUTPUT.
        // - outPins[i] = 0xFF when the pin is INPUT / not driving.
        void SamplePinOutputs(std::uint8_t *outPins, std::size_t outCount) const;

        bool ConsumeSerialByte(std::uint8_t &outByte);
        bool ConsumeSerialByte(int channel, std::uint8_t &outByte);
        void LoadEepromFromFile(const std::string &path);
        void SaveEepromToFile(const std::string &path) const;
        void SetAnalogInput(int channel, float voltage);
        void QueueSerialInput(std::uint8_t value);
        void QueueSerialInput(int channel, std::uint8_t value);

    private:
        void ResetState(bool clearFlash);
        bool LoadTextSection(const std::uint8_t *data, std::size_t size, std::uint64_t flags, std::string &error);
        bool ParseBvmText(const std::vector<std::uint8_t> &buffer, const std::uint8_t *&data,
                          std::size_t &size, std::uint64_t &flags, std::string &error);
        bool PinToPort(int pin, std::uint16_t &ddr, std::uint16_t &port, std::uint16_t &pinReg, std::uint8_t &bit) const;
        void HandleUartWrite(int channel, std::uint8_t value);
        void SimulateTimer0(std::uint64_t cycles);
        void SimulateTimer1(std::uint64_t cycles);
        void SimulateTimer2(std::uint64_t cycles);
        void SimulateTimer3(std::uint64_t cycles);
        void SimulateTimer4(std::uint64_t cycles);
        void SimulateTimer5(std::uint64_t cycles);
        bool MeasureHexMaxAddress(const std::string &hexText, std::size_t &outMax);
        static void IoWriteHook(AvrCore *core, std::uint16_t address, std::uint8_t value, void *user);
        static void IoReadHook(AvrCore *core, std::uint16_t address, std::uint8_t value, void *user);
        double ComputeUartCyclesPerByte(int channel);
        double ComputeUartCyclesPerBit(int channel);
        std::uint32_t NextUartErrorSeed(int channel, std::uint8_t data);
        bool IsUartRxEnabled(int channel) const;
        bool IsUartTxEnabled(int channel) const;
        bool IsUartParityEnabled(int channel) const;
        bool HasUart(int channel) const;
        double ComputeSpiCyclesPerBit() const;
        double ComputeTwiCyclesPerBit() const;

        struct UartState
        {
            std::deque<std::uint8_t> rxQueue;
            bool rxReady = false;
            double rxCyclesRemaining = 0.0;
            std::uint64_t rxCount = 0;
            std::uint32_t errorSeed = 0x9876543u;
            std::deque<std::uint8_t> txQueue;
            std::deque<std::uint8_t> txPending;
            bool txActive = false;
            double txCyclesRemaining = 0.0;
            double udrEmptyCyclesRemaining = 0.0;
            std::uint8_t txByte = 0;
            std::uint16_t cachedUbr = 0;
            bool cachedU2x = false;
            bool cycleCacheValid = false;
            double cyclesPerBitCache = 0.0;
        };

        McuState _state;
        BoardProfile _profile;
        int _pinCount = 0;
        std::vector<int> _pinInputs;
        std::vector<std::uint8_t> _pinValueScratch;
        std::vector<std::uint8_t> _pinValueTouchedFlags;
        std::vector<std::uint16_t> _pinValueTouched;
        std::vector<float> _analogInputs;
        double _timer0Remainder = 0.0;
        double _timer1Remainder = 0.0;
        double _timer2Remainder = 0.0;
        double _timer3Remainder = 0.0;
        double _timer4Remainder = 0.0;
        double _timer5Remainder = 0.0;
        bool _timer0Up = true;
        bool _timer1Up = true;
        bool _timer2Up = true;
        bool _timer3Up = true;
        bool _timer4Up = true;
        bool _timer5Up = true;
        double _adcCyclesRemaining = 0.0;
        std::uint32_t _adcNoiseSeed = 0x1234567u;
        std::array<UartState, 4> _uarts{};
        std::uint64_t _tickCount = 0;
        std::size_t _uartQueueLimit = 2048;
        std::uint8_t _lastPinb = 0;
        std::uint8_t _lastPinc = 0;
        std::uint8_t _lastPind = 0;
        std::uint8_t _lastPine = 0;
        double _wdtCyclesRemaining = 0.0;
        bool _wdtResetArmed = false;
        bool _spiActive = false;
        double _spiCyclesRemaining = 0.0;
        std::uint8_t _spiData = 0;
        bool _spiSpsrRead = false;
        bool _twiActive = false;
        double _twiCyclesRemaining = 0.0;
        std::uint8_t _twiData = 0;
        std::uint8_t _twiStatus = 0xF8;
        PerfCounters _perf{};
    };
}
