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
            Step
        };

        Type type = Type::Load;
        std::vector<std::uint8_t> data;
        std::uint32_t deltaMicros = 0;
        std::uint8_t pins[kPinCount]{};
    };

    class PipeManager
    {
    public:
        PipeManager();
        ~PipeManager();

        bool Start(const std::wstring& pipeName);
        void Stop();

        bool IsConnected() const;
        bool PopCommand(PipeCommand& outCommand);

        void SendHelloAck(std::uint32_t flags);
        void SendOutputState(std::uint64_t tickCount, const std::uint8_t* pins, std::size_t count);
        void SendSerial(const std::uint8_t* data, std::size_t size);
        void SendStatus(std::uint64_t tickCount);
        void SendLog(LogLevel level, const std::string& text);
        void SendError(std::uint32_t code, const std::string& text);

    private:
        void ThreadMain();
        bool EnsurePipe();
        void DisconnectPipe();
        bool ReadPacket(PacketHeader& header, std::vector<std::uint8_t>& payload);
        bool ReadExact(std::uint8_t* buffer, std::size_t size);
        bool WritePacket(MessageType type, const std::uint8_t* payload, std::size_t size);
        void Enqueue(const PipeCommand& command);

        std::wstring _pipeName;
        std::atomic<bool> _running{false};
        std::atomic<bool> _connected{false};
        HANDLE _pipeHandle = INVALID_HANDLE_VALUE;
        HANDLE _threadHandle = nullptr;
        std::mutex _queueMutex;
        std::queue<PipeCommand> _queue;
        std::uint32_t _sequence = 1;
    };
}
