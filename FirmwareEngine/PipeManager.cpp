#include "PipeManager.h"

#include <cstdio>
#include <cstring>
#include <cstdlib>
#include <thread>

namespace firmware
{
    namespace
    {
        bool LockstepTraceEnabled()
        {
            static const bool enabled = []()
            {
                const char *env = std::getenv("RTFW_LOCKSTEP_TRACE");
                return env != nullptr && env[0] != '\0' && env[0] != '0';
            }();
            return enabled;
        }

        bool WaitOverlapped(HANDLE handle, OVERLAPPED &ov, DWORD &outBytes, DWORD timeoutMs)
        {
            outBytes = 0;
            DWORD wait = WaitForSingleObject(ov.hEvent, timeoutMs);
            if (wait != WAIT_OBJECT_0)
            {
                CancelIoEx(handle, &ov);
                return false;
            }
            if (!GetOverlappedResult(handle, &ov, &outBytes, FALSE))
            {
                return false;
            }
            return true;
        }

        std::wstring BuildPipePath(const std::wstring &name)
        {
            if (name.rfind(L"\\\\.\\pipe\\", 0) == 0)
            {
                return name;
            }
            return L"\\\\.\\pipe\\" + name;
        }

        std::string ReadFixedString(const char *data, std::size_t size)
        {
            if (!data || size == 0)
                return {};
            std::size_t len = 0;
            while (len < size && data[len] != '\0')
            {
                len++;
            }
            return std::string(data, len);
        }

        void WriteFixedString(char *dst, std::size_t size, const std::string &value)
        {
            if (!dst || size == 0)
                return;
            std::memset(dst, 0, size);
            if (value.empty())
                return;
            std::size_t copy = value.size();
            if (copy >= size)
                copy = size - 1;
            std::memcpy(dst, value.data(), copy);
        }

        std::uint64_t NowMicros()
        {
            LARGE_INTEGER freq{};
            LARGE_INTEGER counter{};
            if (!QueryPerformanceFrequency(&freq) || !QueryPerformanceCounter(&counter) || freq.QuadPart == 0)
            {
                return 0;
            }
            return static_cast<std::uint64_t>((counter.QuadPart * 1000000ULL) / static_cast<std::uint64_t>(freq.QuadPart));
        }
    }

    PipeManager::PipeManager() = default;

    PipeManager::~PipeManager()
    {
        Stop();
    }

    bool PipeManager::Start(const std::wstring &pipeName)
    {
        if (_running)
            return false;
        _pipeName = BuildPipePath(pipeName);
        _running = true;
        _threadHandle = CreateThread(nullptr, 0, [](LPVOID param) -> DWORD
                                     {
            auto* self = static_cast<PipeManager*>(param);
            self->ThreadMain();
            return 0; }, this, 0, nullptr);
        return _threadHandle != nullptr;
    }

    void PipeManager::Stop()
    {
        _running = false;
        // Cancel any blocking overlapped I/O so the thread can exit promptly.
        DisconnectPipe();
        _connected = false;
        if (_threadHandle)
        {
            WaitForSingleObject(_threadHandle, 2000);
            CloseHandle(_threadHandle);
            _threadHandle = nullptr;
        }
    }

    bool PipeManager::IsConnected() const
    {
        return _connected.load();
    }

    bool PipeManager::PopCommand(PipeCommand &outCommand)
    {
        std::lock_guard<std::mutex> guard(_queueMutex);
        if (_queue.empty())
            return false;
        outCommand = _queue.front();
        _queue.pop();
        return true;
    }

    void PipeManager::SendHelloAck(std::uint32_t flags)
    {
        HelloAckPayload payload{};
        payload.flags = flags;
        payload.pin_count = static_cast<std::uint32_t>(kPinCount);
        payload.board_id_size = static_cast<std::uint32_t>(kBoardIdSize);
        payload.analog_count = static_cast<std::uint32_t>(kAnalogCount);
        WritePacket(MessageType::HelloAck, reinterpret_cast<const std::uint8_t *>(&payload), sizeof(payload));
    }

    bool PipeManager::SendOutputState(const std::string &boardId, std::uint64_t stepSequence, std::uint64_t tickCount, const std::uint8_t *pins, std::size_t count,
                                      std::uint64_t cycles, std::uint64_t adcSamples,
                                      const std::uint64_t *uartTxBytes, const std::uint64_t *uartRxBytes,
                                      std::uint64_t spiTransfers, std::uint64_t twiTransfers, std::uint64_t wdtResets)
    {
        if (!pins || count < kPinCount)
            return false;
        OutputStatePayload payload{};
        WriteFixedString(payload.board_id, kBoardIdSize, boardId);
        payload.step_sequence = stepSequence;
        payload.tick_count = tickCount;
        std::memcpy(payload.pins, pins, kPinCount);
        payload.cycles = cycles;
        payload.adc_samples = adcSamples;
        if (uartTxBytes)
        {
            std::memcpy(payload.uart_tx_bytes, uartTxBytes, sizeof(payload.uart_tx_bytes));
        }
        if (uartRxBytes)
        {
            std::memcpy(payload.uart_rx_bytes, uartRxBytes, sizeof(payload.uart_rx_bytes));
        }
        payload.spi_transfers = spiTransfers;
        payload.twi_transfers = twiTransfers;
        payload.wdt_resets = wdtResets;
        payload.timestamp_micros = NowMicros();

        if (LockstepTraceEnabled())
        {
            std::printf("[Pipe] OutputState send board=%s seq=%llu tick=%llu\n", boardId.c_str(),
                        static_cast<unsigned long long>(stepSequence),
                        static_cast<unsigned long long>(tickCount));
        }
        return WritePacket(MessageType::OutputState, reinterpret_cast<const std::uint8_t *>(&payload), sizeof(payload));
    }

    void PipeManager::SendSerial(const std::string &boardId, const std::uint8_t *data, std::size_t size)
    {
        if (!data || size == 0)
            return;
        std::vector<std::uint8_t> payload;
        payload.resize(kBoardIdSize + size);
        WriteFixedString(reinterpret_cast<char *>(payload.data()), kBoardIdSize, boardId);
        std::memcpy(payload.data() + kBoardIdSize, data, size);
        WritePacket(MessageType::Serial, payload.data(), payload.size());
    }

    void PipeManager::SendStatus(const std::string &boardId, std::uint64_t tickCount)
    {
        StatusPayload payload{};
        WriteFixedString(payload.board_id, kBoardIdSize, boardId);
        payload.tick_count = tickCount;
        WritePacket(MessageType::Status, reinterpret_cast<const std::uint8_t *>(&payload), sizeof(payload));
    }

    void PipeManager::SendLog(const std::string &boardId, LogLevel level, const std::string &text)
    {
        if (text.empty())
            return;
        std::vector<std::uint8_t> payload;
        payload.resize(sizeof(LogPayload) + text.size());
        auto *header = reinterpret_cast<LogPayload *>(payload.data());
        WriteFixedString(header->board_id, kBoardIdSize, boardId);
        header->level = static_cast<std::uint8_t>(level);
        std::memcpy(payload.data() + sizeof(LogPayload), text.data(), text.size());
        WritePacket(MessageType::Log, payload.data(), payload.size());
    }

    void PipeManager::SendError(const std::string &boardId, std::uint32_t code, const std::string &text)
    {
        std::vector<std::uint8_t> payload;
        payload.resize(sizeof(ErrorPayload) + text.size());
        auto *header = reinterpret_cast<ErrorPayload *>(payload.data());
        WriteFixedString(header->board_id, kBoardIdSize, boardId);
        header->code = code;
        if (!text.empty())
        {
            std::memcpy(payload.data() + sizeof(ErrorPayload), text.data(), text.size());
        }
        WritePacket(MessageType::Error, payload.data(), payload.size());
    }

    void PipeManager::ThreadMain()
    {
        while (_running)
        {
            if (!EnsurePipe())
            {
                std::this_thread::sleep_for(std::chrono::milliseconds(200));
                continue;
            }

            PacketHeader header{};
            std::vector<std::uint8_t> payload;
            if (!ReadPacket(header, payload))
            {
                DisconnectPipe();
                continue;
            }

            if (header.magic != kProtocolMagic)
            {
                SendError("system", 1, "Invalid protocol magic");
                continue;
            }
            if (header.version_major != kProtocolMajor)
            {
                SendError("system", 1, "Unsupported protocol major");
                continue;
            }

            auto type = static_cast<MessageType>(header.type);
            if (type == MessageType::Hello)
            {
                SendHelloAck(kFeatureTimestampMicros | kFeaturePerfCounters);
                continue;
            }

            if (type == MessageType::LoadBvm)
            {
                if (payload.size() < sizeof(LoadBvmHeader))
                {
                    SendError("system", 2, "Invalid load payload");
                    continue;
                }
                auto *header = reinterpret_cast<const LoadBvmHeader *>(payload.data());
                PipeCommand cmd;
                cmd.type = PipeCommand::Type::Load;
                cmd.boardId = ReadFixedString(header->board_id, kBoardIdSize);
                cmd.boardProfile = ReadFixedString(header->board_profile, kBoardIdSize);
                cmd.data.assign(payload.begin() + sizeof(LoadBvmHeader), payload.end());
                Enqueue(cmd);
                continue;
            }

            if (type == MessageType::Step)
            {
                const std::size_t baseSize = sizeof(StepPayload) - sizeof(std::uint64_t);
                if (payload.size() < baseSize)
                {
                    SendError("system", 2, "Invalid step payload");
                    continue;
                }
                const auto *step = reinterpret_cast<const StepPayload *>(payload.data());
                PipeCommand cmd;
                cmd.type = PipeCommand::Type::Step;
                cmd.boardId = ReadFixedString(step->board_id, kBoardIdSize);
                cmd.stepSequence = step->step_sequence;
                cmd.deltaMicros = step->delta_micros;
                cmd.sentMicros = payload.size() >= sizeof(StepPayload) ? step->sent_micros : 0;
                std::memcpy(cmd.pins, step->pins, kPinCount);
                cmd.analogCount = kAnalogCount;
                std::memcpy(cmd.analog, step->analog, sizeof(step->analog));

                if (LockstepTraceEnabled())
                {
                    std::printf("[Pipe] Step recv board=%s seq=%llu dt_us=%u sent_us=%llu\n", cmd.boardId.c_str(),
                                static_cast<unsigned long long>(cmd.stepSequence), static_cast<unsigned int>(cmd.deltaMicros),
                                static_cast<unsigned long long>(cmd.sentMicros));
                }
                Enqueue(cmd);
                continue;
            }
        }
    }

    bool PipeManager::EnsurePipe()
    {
        if (_connected)
            return true;
        if (_pipeHandle != INVALID_HANDLE_VALUE)
        {
            CloseHandle(_pipeHandle);
            _pipeHandle = INVALID_HANDLE_VALUE;
        }

        _pipeHandle = CreateNamedPipeW(
            _pipeName.c_str(),
            PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
            1,
            65536,
            65536,
            0,
            nullptr);

        if (_pipeHandle == INVALID_HANDLE_VALUE)
        {
            return false;
        }

        OVERLAPPED ov{};
        ov.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (!ov.hEvent)
        {
            CloseHandle(_pipeHandle);
            _pipeHandle = INVALID_HANDLE_VALUE;
            return false;
        }

        BOOL connected = ConnectNamedPipe(_pipeHandle, &ov);
        if (!connected)
        {
            DWORD err = GetLastError();
            if (err == ERROR_PIPE_CONNECTED)
            {
                SetEvent(ov.hEvent);
            }
            else if (err != ERROR_IO_PENDING)
            {
                CloseHandle(ov.hEvent);
                CloseHandle(_pipeHandle);
                _pipeHandle = INVALID_HANDLE_VALUE;
                return false;
            }
        }

        DWORD bytes = 0;
        if (!WaitOverlapped(_pipeHandle, ov, bytes, 5000))
        {
            CloseHandle(ov.hEvent);
            CloseHandle(_pipeHandle);
            _pipeHandle = INVALID_HANDLE_VALUE;
            return false;
        }
        CloseHandle(ov.hEvent);

        _connected = true;
        std::puts("Pipe Connected");
        return true;
    }

    void PipeManager::DisconnectPipe()
    {
        if (_pipeHandle != INVALID_HANDLE_VALUE)
        {
            CancelIoEx(_pipeHandle, nullptr);
            FlushFileBuffers(_pipeHandle);
            DisconnectNamedPipe(_pipeHandle);
            CloseHandle(_pipeHandle);
            _pipeHandle = INVALID_HANDLE_VALUE;
        }
        _connected = false;
        _sequence = 1;
    }

    bool PipeManager::ReadPacket(PacketHeader &header, std::vector<std::uint8_t> &payload)
    {
        if (!ReadExact(reinterpret_cast<std::uint8_t *>(&header), sizeof(header)))
        {
            return false;
        }
        if (header.payload_size > kMaxPayloadBytes)
        {
            SendError("system", 2, "Payload too large");
            return false;
        }
        if (header.payload_size > 0)
        {
            payload.resize(header.payload_size);
            if (!ReadExact(payload.data(), payload.size()))
            {
                return false;
            }
        }
        else
        {
            payload.clear();
        }
        return true;
    }

    bool PipeManager::ReadExact(std::uint8_t *buffer, std::size_t size)
    {
        std::size_t total = 0;
        while (total < size && _running)
        {
            DWORD want = static_cast<DWORD>(size - total);
            DWORD bytesRead = 0;
            OVERLAPPED ov{};
            ov.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            if (!ov.hEvent)
            {
                return false;
            }

            BOOL ok = ReadFile(_pipeHandle, buffer + total, want, nullptr, &ov);
            if (!ok)
            {
                DWORD err = GetLastError();
                if (err != ERROR_IO_PENDING)
                {
                    CloseHandle(ov.hEvent);
                    return false;
                }
                // Reads can legitimately block while the host is waiting for firmware output.
                // Do not time out and disconnect the pipe during these idle periods.
                if (!WaitOverlapped(_pipeHandle, ov, bytesRead, INFINITE))
                {
                    CloseHandle(ov.hEvent);
                    return false;
                }
            }
            else
            {
                if (!GetOverlappedResult(_pipeHandle, &ov, &bytesRead, TRUE))
                {
                    CloseHandle(ov.hEvent);
                    return false;
                }
            }

            CloseHandle(ov.hEvent);
            if (bytesRead == 0)
            {
                return false;
            }
            total += bytesRead;
        }
        return total == size;
    }

    bool PipeManager::WritePacket(MessageType type, const std::uint8_t *payload, std::size_t size)
    {
        if (!IsConnected() || _pipeHandle == INVALID_HANDLE_VALUE)
            return false;

        PacketHeader header{};
        header.magic = kProtocolMagic;
        header.version_major = kProtocolMajor;
        header.version_minor = kProtocolMinor;
        header.type = static_cast<std::uint16_t>(type);
        header.flags = 0;
        header.payload_size = static_cast<std::uint32_t>(size);
        header.sequence = _sequence++;

        _lastWriteError = 0;

        DWORD written = 0;
        {
            OVERLAPPED ov{};
            ov.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            if (!ov.hEvent)
                return false;
            BOOL ok = WriteFile(_pipeHandle, &header, sizeof(header), nullptr, &ov);
            if (!ok)
            {
                DWORD err = GetLastError();
                if (err != ERROR_IO_PENDING)
                {
                    _lastWriteError = err;
                    CloseHandle(ov.hEvent);
                    return false;
                }
                if (!WaitOverlapped(_pipeHandle, ov, written, 2000) || written != sizeof(header))
                {
                    _lastWriteError = GetLastError();
                    CloseHandle(ov.hEvent);
                    return false;
                }
            }
            else
            {
                if (!GetOverlappedResult(_pipeHandle, &ov, &written, TRUE) || written != sizeof(header))
                {
                    _lastWriteError = GetLastError();
                    CloseHandle(ov.hEvent);
                    return false;
                }
            }
            CloseHandle(ov.hEvent);
        }
        if (size > 0 && payload)
        {
            OVERLAPPED ov{};
            ov.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            if (!ov.hEvent)
                return false;
            BOOL ok = WriteFile(_pipeHandle, payload, static_cast<DWORD>(size), nullptr, &ov);
            if (!ok)
            {
                DWORD err = GetLastError();
                if (err != ERROR_IO_PENDING)
                {
                    _lastWriteError = err;
                    CloseHandle(ov.hEvent);
                    return false;
                }
                if (!WaitOverlapped(_pipeHandle, ov, written, 2000) || written != size)
                {
                    _lastWriteError = GetLastError();
                    CloseHandle(ov.hEvent);
                    return false;
                }
            }
            else
            {
                if (!GetOverlappedResult(_pipeHandle, &ov, &written, TRUE) || written != size)
                {
                    _lastWriteError = GetLastError();
                    CloseHandle(ov.hEvent);
                    return false;
                }
            }
            CloseHandle(ov.hEvent);
        }
        return true;
    }

    void PipeManager::Enqueue(const PipeCommand &command)
    {
        std::lock_guard<std::mutex> guard(_queueMutex);
        _queue.push(command);
    }
}
