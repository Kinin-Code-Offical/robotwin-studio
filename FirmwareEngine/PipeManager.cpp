#include "PipeManager.h"

#include <cstdio>
#include <cstring>
#include <cstdlib>
#include <thread>
#include <sddl.h>

#pragma comment(lib, "Advapi32.lib")

namespace firmware
{
    namespace
    {
        void WriteBits(std::uint8_t *dst, std::size_t dstBits, std::size_t offset, std::size_t width, std::uint64_t value)
        {
            if (!dst || width == 0)
                return;
            if (offset + width > dstBits)
                return;
            for (std::size_t bit = 0; bit < width; ++bit)
            {
                std::size_t target = offset + bit;
                std::size_t byteIndex = target / 8;
                std::size_t bitIndex = target % 8;
                if ((value >> bit) & 0x1u)
                {
                    dst[byteIndex] = static_cast<std::uint8_t>(dst[byteIndex] | (1u << bitIndex));
                }
            }
        }

        void WriteDebugBits(std::uint8_t *dst, std::size_t dstBytes, const OutputDebugState &debug)
        {
            if (!dst || dstBytes < kDebugBitBytes)
                return;
            std::memset(dst, 0, dstBytes);
            const std::size_t dstBits = dstBytes * 8;
            WriteBits(dst, dstBits, kDbgBitPc, 16, debug.pc);
            WriteBits(dst, dstBits, kDbgBitSp, 16, debug.sp);
            WriteBits(dst, dstBits, kDbgBitSreg, 8, debug.sreg);
            WriteBits(dst, dstBits, kDbgBitFlashBytes, 32, debug.flashBytes);
            WriteBits(dst, dstBits, kDbgBitSramBytes, 32, debug.sramBytes);
            WriteBits(dst, dstBits, kDbgBitEepromBytes, 32, debug.eepromBytes);
            WriteBits(dst, dstBits, kDbgBitIoBytes, 32, debug.ioBytes);
            WriteBits(dst, dstBits, kDbgBitCpuHz, 32, debug.cpuHz);
            WriteBits(dst, dstBits, kDbgBitStackHighWater, 16, debug.stackHighWater);
            WriteBits(dst, dstBits, kDbgBitHeapTop, 16, debug.heapTopAddress);
            WriteBits(dst, dstBits, kDbgBitStackMin, 16, debug.stackMinAddress);
            WriteBits(dst, dstBits, kDbgBitDataSegmentEnd, 16, debug.dataSegmentEnd);
            WriteBits(dst, dstBits, kDbgBitStackOverflows, 32, static_cast<std::uint32_t>(debug.stackOverflows));
            WriteBits(dst, dstBits, kDbgBitInvalidMem, 32, static_cast<std::uint32_t>(debug.invalidMemoryAccesses));
            WriteBits(dst, dstBits, kDbgBitInterruptCount, 32, static_cast<std::uint32_t>(debug.interruptCount));
            WriteBits(dst, dstBits, kDbgBitInterruptLatencyMax, 32, static_cast<std::uint32_t>(debug.interruptLatencyMax));
            WriteBits(dst, dstBits, kDbgBitTimingViolations, 32, static_cast<std::uint32_t>(debug.timingViolations));
            WriteBits(dst, dstBits, kDbgBitCriticalSectionCycles, 32, static_cast<std::uint32_t>(debug.criticalSectionCycles));
            WriteBits(dst, dstBits, kDbgBitSleepCycles, 32, static_cast<std::uint32_t>(debug.sleepCycles));
            WriteBits(dst, dstBits, kDbgBitFlashAccessCycles, 32, static_cast<std::uint32_t>(debug.flashAccessCycles));
            WriteBits(dst, dstBits, kDbgBitUartOverflows, 32, static_cast<std::uint32_t>(debug.uartOverflows));
            WriteBits(dst, dstBits, kDbgBitTimerOverflows, 32, static_cast<std::uint32_t>(debug.timerOverflows));
            WriteBits(dst, dstBits, kDbgBitBrownOutResets, 32, static_cast<std::uint32_t>(debug.brownOutResets));
            WriteBits(dst, dstBits, kDbgBitGpioStateChanges, 32, static_cast<std::uint32_t>(debug.gpioStateChanges));
            WriteBits(dst, dstBits, kDbgBitPwmCycles, 32, static_cast<std::uint32_t>(debug.pwmCycles));
            WriteBits(dst, dstBits, kDbgBitI2cTransactions, 32, static_cast<std::uint32_t>(debug.i2cTransactions));
            WriteBits(dst, dstBits, kDbgBitSpiTransactions, 32, static_cast<std::uint32_t>(debug.spiTransactions));
        }

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
        payload.flash_bytes = 0;
        payload.sram_bytes = 0;
        payload.eeprom_bytes = 0;
        payload.io_bytes = 0;
        payload.cpu_hz = 0;
        WritePacket(MessageType::HelloAck, reinterpret_cast<const std::uint8_t *>(&payload), sizeof(payload));
    }

    bool PipeManager::SendOutputState(const std::string &boardId, std::uint64_t stepSequence, std::uint64_t tickCount, const std::uint8_t *pins, std::size_t count,
                                      std::uint64_t cycles, std::uint64_t adcSamples,
                                      const std::uint64_t *uartTxBytes, const std::uint64_t *uartRxBytes,
                                      std::uint64_t spiTransfers, std::uint64_t twiTransfers, std::uint64_t wdtResets,
                                      const OutputDebugState &debug)
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
        payload.flash_bytes = debug.flashBytes;
        payload.sram_bytes = debug.sramBytes;
        payload.eeprom_bytes = debug.eepromBytes;
        payload.io_bytes = debug.ioBytes;
        payload.cpu_hz = debug.cpuHz;
        payload.pc = debug.pc;
        payload.sp = debug.sp;
        payload.sreg = debug.sreg;
        payload.stack_high_water = debug.stackHighWater;
        payload.heap_top_address = debug.heapTopAddress;
        payload.stack_min_address = debug.stackMinAddress;
        payload.data_segment_end = debug.dataSegmentEnd;
        payload.stack_overflows = debug.stackOverflows;
        payload.invalid_memory_accesses = debug.invalidMemoryAccesses;
        payload.interrupt_count = debug.interruptCount;
        payload.interrupt_latency_max = debug.interruptLatencyMax;
        payload.timing_violations = debug.timingViolations;
        payload.critical_section_cycles = debug.criticalSectionCycles;
        payload.sleep_cycles = debug.sleepCycles;
        payload.flash_access_cycles = debug.flashAccessCycles;
        payload.uart_overflows = debug.uartOverflows;
        payload.timer_overflows = debug.timerOverflows;
        payload.brown_out_resets = debug.brownOutResets;
        payload.gpio_state_changes = debug.gpioStateChanges;
        payload.pwm_cycles = debug.pwmCycles;
        payload.i2c_transactions = debug.i2cTransactions;
        payload.spi_transactions = debug.spiTransactions;
        payload.debug_bit_count = kDebugBitCount;
        payload.reserved1 = 0;
        WriteDebugBits(payload.debug_bits, sizeof(payload.debug_bits), debug);

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

            if (type == MessageType::Serial)
            {
                if (payload.size() <= kBoardIdSize)
                {
                    SendError("system", 2, "Invalid serial payload");
                    continue;
                }
                PipeCommand cmd;
                cmd.type = PipeCommand::Type::SerialInput;
                cmd.boardId = ReadFixedString(reinterpret_cast<const char *>(payload.data()), kBoardIdSize);
                if (payload.size() > kBoardIdSize)
                {
                    cmd.data.assign(payload.begin() + kBoardIdSize, payload.end());
                }
                Enqueue(cmd);
                continue;
            }

            if (type == MessageType::MemoryPatch)
            {
                if (payload.size() < sizeof(MemoryPatchHeader))
                {
                    SendError("system", 2, "Invalid patch payload");
                    continue;
                }
                const auto *header = reinterpret_cast<const MemoryPatchHeader *>(payload.data());
                std::size_t expected = sizeof(MemoryPatchHeader) + header->length;
                if (payload.size() < expected)
                {
                    SendError("system", 2, "Patch payload truncated");
                    continue;
                }
                PipeCommand cmd;
                cmd.type = PipeCommand::Type::Patch;
                cmd.boardId = ReadFixedString(header->board_id, kBoardIdSize);
                cmd.memoryType = static_cast<MemoryType>(header->memory_type);
                cmd.address = header->address;
                cmd.data.assign(payload.begin() + sizeof(MemoryPatchHeader), payload.begin() + expected);
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

        SECURITY_ATTRIBUTES attrs{};
        attrs.nLength = sizeof(attrs);
        attrs.bInheritHandle = FALSE;
        PSECURITY_DESCRIPTOR security = nullptr;
        if (ConvertStringSecurityDescriptorToSecurityDescriptorW(L"D:(A;;GA;;;WD)", SDDL_REVISION_1, &security, nullptr))
        {
            attrs.lpSecurityDescriptor = security;
        }

        _pipeHandle = CreateNamedPipeW(
            _pipeName.c_str(),
            PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
            1,
            65536,
            65536,
            0,
            security ? &attrs : nullptr);

        if (security)
        {
            LocalFree(security);
        }

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
