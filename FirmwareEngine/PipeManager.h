#pragma once

#include <windows.h>
#include <atomic>
#include <cstdint>
#include <mutex>
#include <queue>
#include <string>
#include <vector>

#include "Protocol.h"

namespace firmware
{
    struct PipeCommand
    {
        enum class Type
        {
            Load,
            Step,
            Patch
        };

        Type type = Type::Load;
        std::string boardId;
        std::string boardProfile;
        std::vector<std::uint8_t> data;
        std::uint64_t stepSequence = 0;
        std::uint32_t deltaMicros = 0;
        std::uint64_t sentMicros = 0;
        std::uint8_t pins[kPinCount]{};
        std::uint16_t analog[kAnalogCount]{};
        std::size_t analogCount = 0;
        MemoryType memoryType = MemoryType::Flash;
        std::uint32_t address = 0;
    };

    struct OutputDebugState
    {
        std::uint32_t flashBytes = 0;
        std::uint32_t sramBytes = 0;
        std::uint32_t eepromBytes = 0;
        std::uint32_t ioBytes = 0;
        std::uint32_t cpuHz = 0;
        std::uint16_t pc = 0;
        std::uint16_t sp = 0;
        std::uint8_t sreg = 0;
        std::uint16_t stackHighWater = 0;
        std::uint16_t heapTopAddress = 0;
        std::uint16_t stackMinAddress = 0;
        std::uint16_t dataSegmentEnd = 0;
        std::uint64_t stackOverflows = 0;
        std::uint64_t invalidMemoryAccesses = 0;
        std::uint64_t interruptCount = 0;
        std::uint64_t interruptLatencyMax = 0;
        std::uint64_t timingViolations = 0;
        std::uint64_t criticalSectionCycles = 0;
        std::uint64_t sleepCycles = 0;
        std::uint64_t flashAccessCycles = 0;
        std::uint64_t uartOverflows = 0;
        std::uint64_t timerOverflows = 0;
        std::uint64_t brownOutResets = 0;
        std::uint64_t gpioStateChanges = 0;
        std::uint64_t pwmCycles = 0;
        std::uint64_t i2cTransactions = 0;
        std::uint64_t spiTransactions = 0;
    };

    class PipeManager
    {
    public:
        PipeManager();
        ~PipeManager();

        bool Start(const std::wstring &pipeName);
        void Stop();

        bool IsConnected() const;
        DWORD LastWriteError() const { return _lastWriteError.load(); }
        bool PopCommand(PipeCommand &outCommand);

        void SendHelloAck(std::uint32_t flags);
        bool SendOutputState(const std::string &boardId, std::uint64_t stepSequence, std::uint64_t tickCount, const std::uint8_t *pins, std::size_t count,
                             std::uint64_t cycles, std::uint64_t adcSamples,
                             const std::uint64_t *uartTxBytes, const std::uint64_t *uartRxBytes,
                             std::uint64_t spiTransfers, std::uint64_t twiTransfers, std::uint64_t wdtResets,
                             const OutputDebugState &debug);
        void SendSerial(const std::string &boardId, const std::uint8_t *data, std::size_t size);
        void SendStatus(const std::string &boardId, std::uint64_t tickCount);
        void SendLog(const std::string &boardId, LogLevel level, const std::string &text);
        void SendError(const std::string &boardId, std::uint32_t code, const std::string &text);

    private:
        void ThreadMain();
        bool EnsurePipe();
        void DisconnectPipe();
        bool ReadPacket(PacketHeader &header, std::vector<std::uint8_t> &payload);
        bool ReadExact(std::uint8_t *buffer, std::size_t size);
        bool WritePacket(MessageType type, const std::uint8_t *payload, std::size_t size);
        void Enqueue(const PipeCommand &command);

        std::wstring _pipeName;
        std::atomic<bool> _running{false};
        std::atomic<bool> _connected{false};
        HANDLE _pipeHandle = INVALID_HANDLE_VALUE;
        HANDLE _threadHandle = nullptr;
        std::mutex _queueMutex;
        std::queue<PipeCommand> _queue;
        std::uint32_t _sequence = 1;
        std::atomic<DWORD> _lastWriteError{0};
    };
}
