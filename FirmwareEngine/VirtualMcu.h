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
            std::uint64_t stackOverflows = 0;
            std::uint64_t invalidMemoryAccesses = 0;
            std::uint64_t interruptCycles = 0;
            std::uint64_t eepromWrites = 0;
            std::uint16_t stackHighWaterMark = 0;
            std::uint16_t heapTopAddress = 0;
            std::uint16_t stackMinAddress = 0xFFFF;
            std::uint16_t dataSegmentEnd = 0;
            std::uint64_t watchdogResets = 0;
            std::uint64_t brownOutResets = 0;
            std::uint64_t sleepCycles = 0;
            std::uint64_t flashAccessCycles = 0;
            std::uint64_t uartOverflows = 0;
            std::uint64_t timerOverflows = 0;
            // Robotics Development Metrics
            std::uint64_t gpioStateChanges = 0;
            std::uint64_t pwmCycles = 0;
            std::uint64_t i2cTransactions = 0;
            std::uint64_t spiTransactions = 0;
            std::uint64_t interruptLatencyMax = 0;
            std::uint64_t interruptLatencyTotal = 0;
            std::uint64_t interruptCount = 0;
            std::uint64_t timingViolations = 0;
            std::uint64_t criticalSectionCycles = 0;
        };

        struct GpioStateEvent
        {
            std::uint64_t timestamp;
            std::uint8_t port; // 'B', 'C', 'D', etc.
            std::uint8_t pin;
            bool state;
        };

        struct PwmState
        {
            std::uint16_t dutyCycle; // 0-1023 or 0-255 depending on mode
            std::uint16_t frequency;
            std::uint8_t pin;
            bool active;
        };

        struct I2cTransaction
        {
            std::uint64_t timestamp;
            std::uint8_t address;
            std::vector<std::uint8_t> data;
            bool isWrite;
            bool ack;
        };

        struct SpiTransaction
        {
            std::uint64_t timestamp;
            std::vector<std::uint8_t> txData;
            std::vector<std::uint8_t> rxData;
            std::uint32_t clockSpeed;
        };

        struct InterruptEvent
        {
            std::uint64_t triggerTime;
            std::uint64_t serviceTime;
            std::uint8_t vector;
            std::uint64_t latency;
        };

        struct CpuTraceEvent
        {
            std::uint64_t tick;
            std::uint16_t pc;
            std::uint16_t opcode;
            std::uint16_t sp;
            std::uint8_t sreg;
        };

        explicit VirtualMcu(const BoardProfile &profile);

        bool LoadBvm(const std::vector<std::uint8_t> &buffer, std::string &error);
        void Reset();
        void SoftReset();
        void StepCycles(std::uint64_t cycles);
        std::uint32_t GetPC() const;

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

        // Robotics Development API
        const std::vector<GpioStateEvent> &GetGpioHistory() const { return _gpioHistory; }
        const std::vector<PwmState> &GetPwmStates() const { return _pwmStates; }
        const std::vector<I2cTransaction> &GetI2cLog() const { return _i2cLog; }
        const std::vector<SpiTransaction> &GetSpiLog() const { return _spiLog; }
        const std::vector<InterruptEvent> &GetInterruptLog() const { return _interruptLog; }
        void ClearDevelopmentLogs();
        void SetDeterministicMode(bool enabled) { _deterministicMode = enabled; }
        void SetRealtimeDeadline(std::uint64_t cycles) { _realtimeDeadline = cycles; }
        void EnableDevelopmentTracking(bool enabled) { _enableDevelopmentTracking = enabled; }
        void SetTrackingSampleInterval(std::uint64_t interval) { _trackingSampleInterval = interval; }
        double GetAverageInterruptLatency() const;
        std::uint64_t GetMaxInterruptLatency() const { return _perf.interruptLatencyMax; }
        void EnableCpuTrace(bool enabled) { _traceCpuEnabled = enabled; }
        void SetCpuTraceInterval(std::uint32_t interval) { _traceCpuInterval = interval > 0 ? interval : 1; }
        bool PopCpuTrace(CpuTraceEvent &out);

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
        bool PatchMemory(MemoryType type, std::uint32_t address, const std::uint8_t *data, std::size_t size, std::string &error);

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
        void CheckStackIntegrity();
        bool ValidateMemoryAccess(std::uint16_t address, bool isWrite);
        void UpdateWatchdogTimer(std::uint64_t cycles);
        void CheckBrownOutCondition();
        void SimulatePowerMode();
        void EnforceFlashAccessLatency();
        void CheckPeripheralConstraints();
        void TrackGpioChanges();
        void AnalyzePwmOutputs();
        void LogI2cTransaction();
        void LogSpiTransaction();
        void TrackInterruptLatency();
        void DetectTimingViolations();
        void RecordExecutionSnapshot();

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
        // Realism tracking:
        bool _inInterrupt = false;
        std::uint16_t _stackMinAddress = 0xFFFF;
        std::uint16_t _dataSegmentEnd = 0;
        std::vector<std::uint32_t> _eepromWriteCount;
        std::size_t _uartQueueLimit = 2048;
        // Watchdog Timer
        std::uint64_t _watchdogCounter = 0;
        std::uint64_t _watchdogTimeout = 0;
        bool _watchdogEnabled = false;
        // Power Management
        std::uint8_t _sleepMode = 0;
        bool _sleepEnabled = false;
        double _vccVoltage = 5.0;
        double _brownOutThreshold = 2.7;
        // Flash Access Latency
        std::uint64_t _flashAccessDelay = 0;
        std::uint16_t _lastFlashAddress = 0;
        // Peripheral Constraints
        std::uint16_t _uartRxBufferSize = 128;
        std::uint16_t _uartTxBufferSize = 128;
        // Robotics Development Tracking
        std::vector<GpioStateEvent> _gpioHistory;
        std::vector<PwmState> _pwmStates;
        std::vector<I2cTransaction> _i2cLog;
        std::vector<SpiTransaction> _spiLog;
        std::vector<InterruptEvent> _interruptLog;
        std::uint64_t _lastInterruptTrigger = 0;
        std::uint64_t _criticalSectionStart = 0;
        bool _inCriticalSection = false;
        std::uint64_t _realtimeDeadline = 0;
        bool _deterministicMode = false;
        std::uint32_t _randomSeed = 0x12345678;
        std::size_t _maxGpioHistory = 10000;
        std::size_t _maxI2cLog = 1000;
        std::size_t _maxSpiLog = 1000;
        std::size_t _maxInterruptLog = 1000;
        bool _enableDevelopmentTracking = false;
        std::uint64_t _trackingSampleInterval = 1000;
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
        bool _traceCpuEnabled = false;
        std::uint32_t _traceCpuInterval = 1;
        std::deque<CpuTraceEvent> _traceCpuQueue;
        std::size_t _traceCpuMax = 4096;
        PerfCounters _perf{};
    };
}
