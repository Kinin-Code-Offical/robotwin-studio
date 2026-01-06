#include "Protocol.h"

#include <windows.h>

#include <chrono>
#include <cstdio>
#include <cstdint>
#include <cstring>
#include <string>
#include <vector>

namespace
{
    using namespace firmware;

    std::wstring Utf8ToWide(const std::string &value)
    {
        if (value.empty())
            return {};
        int len = MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, nullptr, 0);
        if (len <= 0)
            return {};
        std::wstring out;
        out.resize(static_cast<std::size_t>(len - 1));
        MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, out.data(), len);
        return out;
    }

    std::wstring BuildPipePath(const std::wstring &name)
    {
        if (name.rfind(L"\\\\.\\pipe\\", 0) == 0)
            return name;
        return L"\\\\.\\pipe\\" + name;
    }

    bool WriteExact(HANDLE handle, const void *data, std::size_t size)
    {
        const std::uint8_t *bytes = static_cast<const std::uint8_t *>(data);
        std::size_t total = 0;
        while (total < size)
        {
            DWORD written = 0;
            DWORD want = static_cast<DWORD>(size - total);
            if (!WriteFile(handle, bytes + total, want, &written, nullptr) || written == 0)
            {
                return false;
            }
            total += written;
        }
        return true;
    }

    bool ReadExact(HANDLE handle, void *data, std::size_t size)
    {
        std::uint8_t *bytes = static_cast<std::uint8_t *>(data);
        std::size_t total = 0;
        while (total < size)
        {
            DWORD read = 0;
            DWORD want = static_cast<DWORD>(size - total);
            if (!ReadFile(handle, bytes + total, want, &read, nullptr) || read == 0)
            {
                return false;
            }
            total += read;
        }
        return true;
    }

    bool SendPacket(HANDLE handle, MessageType type, const void *payload, std::size_t payloadSize, std::uint32_t sequence)
    {
        PacketHeader header{};
        header.magic = kProtocolMagic;
        header.version_major = kProtocolMajor;
        header.version_minor = kProtocolMinor;
        header.type = static_cast<std::uint16_t>(type);
        header.flags = 0;
        header.payload_size = static_cast<std::uint32_t>(payloadSize);
        header.sequence = sequence;

        if (!WriteExact(handle, &header, sizeof(header)))
            return false;
        if (payloadSize > 0)
        {
            if (!WriteExact(handle, payload, payloadSize))
                return false;
        }
        return true;
    }

    bool ReadPacket(HANDLE handle, PacketHeader &outHeader, std::vector<std::uint8_t> &outPayload)
    {
        outPayload.clear();
        if (!ReadExact(handle, &outHeader, sizeof(outHeader)))
            return false;
        if (outHeader.payload_size > kMaxPayloadBytes)
            return false;
        if (outHeader.payload_size > 0)
        {
            outPayload.resize(outHeader.payload_size);
            if (!ReadExact(handle, outPayload.data(), outPayload.size()))
                return false;
        }
        return true;
    }

    std::string GetSelfDirUtf8()
    {
        wchar_t path[MAX_PATH]{};
        DWORD len = GetModuleFileNameW(nullptr, path, MAX_PATH);
        if (len == 0 || len >= MAX_PATH)
            return {};
        std::wstring w(path, path + len);
        auto slash = w.find_last_of(L"\\/");
        if (slash != std::wstring::npos)
            w = w.substr(0, slash);

        int outLen = WideCharToMultiByte(CP_UTF8, 0, w.c_str(), -1, nullptr, 0, nullptr, nullptr);
        if (outLen <= 0)
            return {};
        std::string out;
        out.resize(static_cast<std::size_t>(outLen - 1));
        WideCharToMultiByte(CP_UTF8, 0, w.c_str(), -1, out.data(), outLen, nullptr, nullptr);
        return out;
    }

    bool SpawnFirmwareHost(const std::wstring &pipeName, const std::wstring &logPath, PROCESS_INFORMATION &pi)
    {
        std::string selfDir = GetSelfDirUtf8();
        if (selfDir.empty())
            return false;
        std::wstring exePath = Utf8ToWide(selfDir + "\\\\RoboTwinFirmwareHost.exe");

        std::wstring cmdLine = L"\"" + exePath + L"\" --pipe \"" + pipeName + L"\" --lockstep --trace-lockstep";
        if (!logPath.empty())
        {
            cmdLine += L" --log \"" + logPath + L"\"";
        }

        STARTUPINFOW si{};
        si.cb = sizeof(si);
        std::memset(&pi, 0, sizeof(pi));

        // CreateProcessW requires mutable cmdline buffer.
        std::vector<wchar_t> cmdBuf(cmdLine.begin(), cmdLine.end());
        cmdBuf.push_back(L'\0');

        BOOL ok = CreateProcessW(
            exePath.c_str(),
            cmdBuf.data(),
            nullptr,
            nullptr,
            FALSE,
            CREATE_NO_WINDOW,
            nullptr,
            nullptr,
            &si,
            &pi);

        return ok == TRUE;
    }

    HANDLE ConnectPipeClient(const std::wstring &pipeName, std::uint32_t timeoutMs)
    {
        std::wstring path = BuildPipePath(pipeName);
        auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(timeoutMs);
        while (std::chrono::steady_clock::now() < deadline)
        {
            HANDLE h = CreateFileW(
                path.c_str(),
                GENERIC_READ | GENERIC_WRITE,
                0,
                nullptr,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                nullptr);

            if (h != INVALID_HANDLE_VALUE)
            {
                DWORD mode = PIPE_READMODE_BYTE;
                SetNamedPipeHandleState(h, &mode, nullptr, nullptr);
                return h;
            }

            Sleep(25);
        }
        return INVALID_HANDLE_VALUE;
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

    bool WaitForOutputState(HANDLE pipe, std::uint64_t expectedStepSeq, std::uint32_t timeoutMs)
    {
        auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(timeoutMs);
        PacketHeader header{};
        std::vector<std::uint8_t> payload;

        while (std::chrono::steady_clock::now() < deadline)
        {
            DWORD available = 0;
            if (!PeekNamedPipe(pipe, nullptr, 0, nullptr, &available, nullptr))
            {
                return false;
            }
            if (available == 0)
            {
                Sleep(5);
                continue;
            }

            if (!ReadPacket(pipe, header, payload))
            {
                return false;
            }

            if (header.magic != kProtocolMagic)
                continue;
            auto type = static_cast<MessageType>(header.type);
            if (type == MessageType::Error)
            {
                if (payload.size() >= sizeof(ErrorPayload))
                {
                    const auto *err = reinterpret_cast<const ErrorPayload *>(payload.data());
                    std::printf("[LockstepSanity] Received Error code=%u\n", static_cast<unsigned int>(err->code));
                }
                continue;
            }
            if (type == MessageType::Log)
            {
                std::printf("[LockstepSanity] Received Log\n");
                continue;
            }
            if (type != MessageType::OutputState)
            {
                std::printf("[LockstepSanity] Received packet type=%u\n", static_cast<unsigned int>(header.type));
                continue;
            }
            if (payload.size() < sizeof(OutputStatePayload))
                return false;
            const auto *out = reinterpret_cast<const OutputStatePayload *>(payload.data());
            if (out->step_sequence != expectedStepSeq)
            {
                continue;
            }
            return true;
        }
        return false;
    }
}

int main()
{
    using namespace firmware;

    // Unique-ish pipe name.
    DWORD pid = GetCurrentProcessId();
    std::wstring pipeName = L"RoboTwin.FirmwareEngine.Sanity." + std::to_wstring(static_cast<unsigned long long>(pid));

    std::string selfDir = GetSelfDirUtf8();
    std::wstring logPath;
    if (!selfDir.empty())
    {
        logPath = Utf8ToWide(selfDir + "\\\\lockstep_sanity_host_" + std::to_string(static_cast<unsigned long long>(pid)) + ".log");
    }

    PROCESS_INFORMATION pi{};
    if (!SpawnFirmwareHost(pipeName, logPath, pi))
    {
        std::printf("[LockstepSanity] Failed to spawn RoboTwinFirmwareHost.exe\n");
        return 2;
    }

    HANDLE client = ConnectPipeClient(pipeName, 2000);
    if (client == INVALID_HANDLE_VALUE)
    {
        std::printf("[LockstepSanity] Failed to connect to pipe\n");
        TerminateProcess(pi.hProcess, 3);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return 3;
    }

    std::uint32_t seq = 1;

    // Hello -> expect HelloAck (not strictly required for OutputState, but validates protocol).
    HelloPayload hello{};
    hello.flags = 0;
    hello.pin_count = static_cast<std::uint32_t>(kPinCount);
    hello.board_id_size = static_cast<std::uint32_t>(kBoardIdSize);
    hello.analog_count = static_cast<std::uint32_t>(kAnalogCount);
    if (!SendPacket(client, MessageType::Hello, &hello, sizeof(hello), seq++))
    {
        std::printf("[LockstepSanity] Failed to send Hello\n");
        CloseHandle(client);
        TerminateProcess(pi.hProcess, 4);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return 4;
    }

    // Read until HelloAck.
    PacketHeader header{};
    std::vector<std::uint8_t> payload;
    bool gotAck = false;
    for (int tries = 0; tries < 200; ++tries)
    {
        DWORD available = 0;
        if (!PeekNamedPipe(client, nullptr, 0, nullptr, &available, nullptr))
            break;
        if (available == 0)
        {
            Sleep(5);
            continue;
        }
        if (!ReadPacket(client, header, payload))
            break;
        if (header.magic != kProtocolMagic)
            continue;
        if (static_cast<MessageType>(header.type) == MessageType::HelloAck)
        {
            gotAck = true;
            break;
        }
    }

    if (!gotAck)
    {
        std::printf("[LockstepSanity] Did not receive HelloAck\n");
        CloseHandle(client);
        TerminateProcess(pi.hProcess, 5);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return 5;
    }

    // Send a Step with deltaMicros==0; contract says we must still get an OutputState.
    StepPayload step{};
    WriteFixedString(step.board_id, kBoardIdSize, "board0");
    step.step_sequence = 1;
    step.delta_micros = 0;
    for (std::size_t i = 0; i < kPinCount; ++i)
        step.pins[i] = 0;
    for (std::size_t i = 0; i < kAnalogCount; ++i)
        step.analog[i] = 0;
    step.sent_micros = 0;

    if (!SendPacket(client, MessageType::Step, &step, sizeof(step), seq++))
    {
        std::printf("[LockstepSanity] Failed to send Step\n");
        CloseHandle(client);
        TerminateProcess(pi.hProcess, 6);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return 6;
    }

    if (!WaitForOutputState(client, 1, 2000))
    {
        std::printf("[LockstepSanity] FAIL: Step did not produce OutputState\n");
        if (!logPath.empty())
        {
            std::wprintf(L"[LockstepSanity] Host log: %ls\n", logPath.c_str());
        }
        CloseHandle(client);
        TerminateProcess(pi.hProcess, 7);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return 7;
    }

    std::printf("[LockstepSanity] OK\n");

    CloseHandle(client);
    TerminateProcess(pi.hProcess, 0);
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    return 0;
}
