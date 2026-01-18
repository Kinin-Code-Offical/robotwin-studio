#include "Protocol.h"

#include <windows.h>

#include <cctype>
#include <chrono>
#include <cstdio>
#include <cstdint>
#include <cstring>
#include <fstream>
#include <string>
#include <vector>

namespace
{
    using namespace firmware;

    constexpr std::uint32_t BvmMagic = 0x43534E45; // "CSNE"

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

    bool SpawnFirmwareHost(const std::wstring &pipeName, PROCESS_INFORMATION &pi)
    {
        std::string selfDir = GetSelfDirUtf8();
        if (selfDir.empty())
            return false;
        std::wstring exePath = Utf8ToWide(selfDir + "\\\\RoboTwinFirmwareHost.exe");
        std::wstring cmdLine = L"\"" + exePath + L"\" --pipe \"" + pipeName + L"\" --lockstep --log host_debug.log --trace-lockstep";

        STARTUPINFOW si{};
        si.cb = sizeof(si);
        std::memset(&pi, 0, sizeof(pi));

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
                0, // No sharing
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
}

int main(int argc, char **argv)
{
    if (argc < 2)
    {
        std::printf("Usage: SketchRunner.exe <path_to_bin>\n");
        return 1;
    }

    std::string binPath = argv[1];
    std::ifstream binFile(binPath, std::ios::binary);
    if (!binFile)
    {
        std::printf("Failed to open binary file: %s\n", binPath.c_str());
        return 1;
    }
    std::vector<std::uint8_t> binData((std::istreambuf_iterator<char>(binFile)), std::istreambuf_iterator<char>());
    std::printf("Loaded binary file: %zu bytes\n", binData.size());

    using namespace firmware;

    DWORD pid = GetCurrentProcessId();
    std::wstring pipeName = L"RoboTwin.FirmwareEngine.SketchRunner." + std::to_wstring(static_cast<unsigned long long>(pid));

    PROCESS_INFORMATION pi{};
    if (!SpawnFirmwareHost(pipeName, pi))
    {
        std::printf("Failed to spawn RoboTwinFirmwareHost.exe\n");
        return 2;
    }

    HANDLE client = ConnectPipeClient(pipeName, 2000);
    if (client == INVALID_HANDLE_VALUE)
    {
        std::printf("Failed to connect to pipe\n");
        TerminateProcess(pi.hProcess, 3);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return 3;
    }

    std::uint32_t seq = 1;

    // Send Hello
    HelloPayload hello{};
    hello.flags = 0;
    hello.pin_count = static_cast<std::uint32_t>(kPinCount);
    hello.board_id_size = static_cast<std::uint32_t>(kBoardIdSize);
    hello.analog_count = static_cast<std::uint32_t>(kAnalogCount);
    SendPacket(client, MessageType::Hello, &hello, sizeof(hello), seq++);

    // Create BVM container
    std::vector<std::uint8_t> bvmData;
    {
        BvmHeader header{};
        header.magic = BvmMagic;
        header.version_major = 1;
        header.version_minor = 0;
        header.header_size = sizeof(BvmHeader);
        header.section_count = 1;
        header.entry_offset = 0;
        header.section_table_offset = sizeof(BvmHeader);

        BvmSection section{};
        std::memset(section.name, 0, sizeof(section.name));
        std::strcpy(section.name, ".text");
        section.offset = sizeof(BvmHeader) + sizeof(BvmSection);
        section.size = binData.size();
        section.flags = 8; // SectionTextHex
        section.reserved = 0;

        bvmData.resize(sizeof(BvmHeader) + sizeof(BvmSection) + binData.size());

        std::memcpy(bvmData.data(), &header, sizeof(header));
        std::memcpy(bvmData.data() + sizeof(BvmHeader), &section, sizeof(section));
        std::memcpy(bvmData.data() + section.offset, binData.data(), binData.size());
    }

    // Load Binary
    std::vector<std::uint8_t> loadPayload;
    loadPayload.resize(sizeof(LoadBvmHeader) + bvmData.size());
    auto *loadHeader = reinterpret_cast<LoadBvmHeader *>(loadPayload.data());
    WriteFixedString(loadHeader->board_id, kBoardIdSize, "ArduinoUno");
    WriteFixedString(loadHeader->board_profile, kBoardIdSize, "ArduinoUno");
    std::memcpy(loadPayload.data() + sizeof(LoadBvmHeader), bvmData.data(), bvmData.size());
    SendPacket(client, MessageType::LoadBvm, loadPayload.data(), loadPayload.size(), seq++);

    bool success = false;
    std::uint64_t stepSeq = 1;
    bool lastPin13 = false;
    bool pin13Changed = false;

    auto start = std::chrono::steady_clock::now();

    // Run for up to 15 seconds
    while (std::chrono::steady_clock::now() - start < std::chrono::seconds(15))
    {
        // Send Step
        StepPayload step{};
        WriteFixedString(step.board_id, kBoardIdSize, "ArduinoUno");
        step.step_sequence = stepSeq;
        step.delta_micros = 10000; // 10ms step
        step.sent_micros = 0;

        if (!SendPacket(client, MessageType::Step, &step, sizeof(step), seq++))
        {
            std::printf("Failed to send step\n");
            break;
        }

        // Read all responses
        bool stepAcked = false;
        while (true)
        {
            DWORD available = 0;
            if (!PeekNamedPipe(client, nullptr, 0, nullptr, &available, nullptr))
            {
                break;
            }
            if (available == 0)
            {
                if (!stepAcked)
                {
                    Sleep(1);
                    continue;
                }
                break;
            }

            PacketHeader header{};
            std::vector<std::uint8_t> payload;
            if (!ReadPacket(client, header, payload))
                break;

            auto type = static_cast<MessageType>(header.type);
            if (type == MessageType::Serial)
            {
                if (payload.size() > kBoardIdSize)
                {
                    const uint8_t *rawData = payload.data() + kBoardIdSize;
                    size_t rawLen = payload.size() - kBoardIdSize;

                    std::printf("RAW SERIAL (%zu): ", rawLen);
                    for (size_t i = 0; i < rawLen; ++i)
                    {
                        unsigned char c = rawData[i];
                        if (std::isprint(c) || c == '\n' || c == '\r')
                            std::printf("%c", c);
                        else
                            std::printf("[%02X]", c);
                    }
                    std::printf("\n");

                    std::string serialData(reinterpret_cast<const char *>(rawData), rawLen);
                    if (serialData.find("Hello from RoboTwin!") != std::string::npos)
                    {
                        success = true;
                        goto cleanup;
                    }
                }
            }
            else if (type == MessageType::OutputState)
            {
                const auto *out = reinterpret_cast<const OutputStatePayload *>(payload.data());
                if (out->step_sequence == stepSeq)
                    stepAcked = true;

                if (out->pins[13] != 0xFF) // 0xFF is unknown
                {
                    bool val = out->pins[13] != 0;
                    if (stepSeq > 10 && val != lastPin13)
                    {
                        if (pin13Changed) // Toggled more than once
                            std::printf("Pin 13 toggled to %d at step %llu\n", val ? 1 : 0, stepSeq);
                        lastPin13 = val;
                        pin13Changed = true;
                    }
                }
            }
            else if (type == MessageType::Error)
            {
                const auto *err = reinterpret_cast<const ErrorPayload *>(payload.data());
                std::string msg;
                if (payload.size() > sizeof(ErrorPayload))
                {
                    msg.assign(reinterpret_cast<const char *>(payload.data() + sizeof(ErrorPayload)), payload.size() - sizeof(ErrorPayload));
                }
                std::printf("ERROR: %u %s\n", err->code, msg.c_str());
            }
        }

        if (stepAcked)
        {
            if (stepSeq % 100 == 0)
                std::printf("Step %llu\r", stepSeq);
            stepSeq++;
        }
    }

cleanup:
    TerminateProcess(pi.hProcess, 0);
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    CloseHandle(client);

    if (success)
    {
        std::printf("\nSUCCESS: Found expected string.\n");
        return 0;
    }
    else
    {
        std::printf("\nFAILURE: Did not find expected string within timeout.\n");
        return 1;
    }
}
