#include "PipeManager.h"
#include "Protocol.h"
#include "VirtualArduino.h"

#include <chrono>
#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <thread>
#include <windows.h>

using namespace firmware;

namespace
{
    constexpr double DefaultCpuHz = 16000000.0;
    constexpr std::uint64_t StatusInterval = 100000;

    double QueryNowSeconds()
    {
        static LARGE_INTEGER frequency = []() {
            LARGE_INTEGER freq;
            QueryPerformanceFrequency(&freq);
            return freq;
        }();
        LARGE_INTEGER now;
        QueryPerformanceCounter(&now);
        return static_cast<double>(now.QuadPart) / static_cast<double>(frequency.QuadPart);
    }

    bool ParseArg(const char* arg, const char* key)
    {
        return std::strcmp(arg, key) == 0;
    }
}

int main(int argc, char** argv)
{
    std::wstring pipeName = L"RoboTwin.FirmwareEngine.v1";
    double cpuHz = DefaultCpuHz;
    bool lockstep = true;
    const char* logPath = nullptr;

    for (int i = 1; i < argc; ++i)
    {
        if (ParseArg(argv[i], "--pipe") && i + 1 < argc)
        {
            std::wstring value;
            int len = MultiByteToWideChar(CP_UTF8, 0, argv[i + 1], -1, nullptr, 0);
            if (len > 0)
            {
                value.resize(static_cast<std::size_t>(len - 1));
                MultiByteToWideChar(CP_UTF8, 0, argv[i + 1], -1, value.data(), len);
                pipeName = value;
            }
            ++i;
            continue;
        }
        if (ParseArg(argv[i], "--hz") && i + 1 < argc)
        {
            cpuHz = std::atof(argv[i + 1]);
            if (cpuHz <= 0.0) cpuHz = DefaultCpuHz;
            ++i;
            continue;
        }
        if (ParseArg(argv[i], "--realtime"))
        {
            lockstep = false;
            continue;
        }
        if (ParseArg(argv[i], "--lockstep"))
        {
            lockstep = true;
            continue;
        }
        if (ParseArg(argv[i], "--log") && i + 1 < argc)
        {
            logPath = argv[i + 1];
            ++i;
            continue;
        }
    }

    std::FILE* logFile = nullptr;
    if (logPath != nullptr)
    {
        logFile = std::fopen(logPath, "a");
    }

    auto Log = [&](const char* fmt, ...) {
        char buffer[1024];
        va_list args;
        va_start(args, fmt);
        std::vsnprintf(buffer, sizeof(buffer), fmt, args);
        va_end(args);
        std::printf("%s\n", buffer);
        if (logFile)
        {
            std::fprintf(logFile, "%s\n", buffer);
            std::fflush(logFile);
        }
    };

    Log("VirtualArduinoFirmware - CoreSim Standalone");

    PipeManager pipe;
    if (!pipe.Start(pipeName))
    {
        Log("Failed to start pipe server.");
        return 1;
    }

    VirtualArduino mcu;
    bool hasFirmware = false;
    std::uint64_t lastStatusTick = 0;
    double lastTime = QueryNowSeconds();
    double remainder = 0.0;

    std::uint8_t lastOutputs[kPinCount]{};

    auto UpdateOutputs = [&](VirtualArduino& board) {
        std::uint8_t portb = 0;
        std::uint8_t portc = 0;
        std::uint8_t portd = 0;
        std::uint8_t ddrb = 0;
        std::uint8_t ddrc = 0;
        std::uint8_t ddrd = 0;
        board.SnapshotPorts(portb, portc, portd, ddrb, ddrc, ddrd);

        for (std::size_t i = 0; i < kPinCount; ++i)
        {
            lastOutputs[i] = 0;
        }

        for (int bit = 0; bit < 8; ++bit)
        {
            std::uint8_t mask = static_cast<std::uint8_t>(1u << bit);
            if ((ddrd & mask) == 0) continue;
            bool value = (portd & mask) != 0;
            lastOutputs[bit] = value ? 1 : 0;
        }

        for (int bit = 0; bit < 6; ++bit)
        {
            std::uint8_t mask = static_cast<std::uint8_t>(1u << bit);
            if ((ddrb & mask) == 0) continue;
            bool value = (portb & mask) != 0;
            lastOutputs[8 + bit] = value ? 1 : 0;
        }

        for (int bit = 0; bit < 6; ++bit)
        {
            std::uint8_t mask = static_cast<std::uint8_t>(1u << bit);
            if ((ddrc & mask) == 0) continue;
            bool value = (portc & mask) != 0;
            lastOutputs[14 + bit] = value ? 1 : 0;
        }
    };

    while (true)
    {
        PipeCommand cmd;
        while (pipe.PopCommand(cmd))
        {
            if (cmd.type == PipeCommand::Type::Load)
            {
                std::string error;
                if (mcu.LoadBvm(cmd.data, error))
                {
                    hasFirmware = true;
                    std::fill(lastOutputs, lastOutputs + kPinCount, 0);
                    Log("Binary Loaded (%zu bytes)", cmd.data.size());
                    pipe.SendLog(LogLevel::Info, "Binary loaded");
                }
                else
                {
                    Log("Load failed: %s", error.c_str());
                    hasFirmware = false;
                    pipe.SendError(100, error);
                }
                continue;
            }
            if (cmd.type == PipeCommand::Type::Step)
            {
                for (std::size_t i = 0; i < kPinCount; ++i)
                {
                    mcu.SetInputPin(static_cast<int>(i), cmd.pins[i] != 0 ? 1 : 0);
                }
                if (cmd.deltaMicros > 0)
                {
                    double cyclesExact = (static_cast<double>(cmd.deltaMicros) * cpuHz / 1e6) + remainder;
                    std::uint64_t cycles = static_cast<std::uint64_t>(cyclesExact);
                    remainder = cyclesExact - static_cast<double>(cycles);
                    mcu.SyncInputs();
                    mcu.StepCycles(cycles);
                    UpdateOutputs(mcu);
                    pipe.SendOutputState(mcu.TickCount(), lastOutputs, kPinCount);
                }
                continue;
            }
        }

        if (!hasFirmware)
        {
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
            continue;
        }

        if (lockstep)
        {
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
            continue;
        }

        double now = QueryNowSeconds();
        double elapsed = now - lastTime;
        lastTime = now;
        if (elapsed < 0.0) elapsed = 0.0;

        double cyclesExact = elapsed * cpuHz + remainder;
        std::uint64_t cycles = static_cast<std::uint64_t>(cyclesExact);
        remainder = cyclesExact - static_cast<double>(cycles);
        if (cycles == 0)
        {
            std::this_thread::sleep_for(std::chrono::microseconds(200));
            continue;
        }

        mcu.SyncInputs();

        mcu.StepCycles(cycles);

        UpdateOutputs(mcu);

        std::uint8_t serialByte = 0;
        if (mcu.ConsumeSerialByte(serialByte))
        {
            pipe.SendSerial(&serialByte, 1);
        }

        std::uint64_t tick = mcu.TickCount();
        if (tick - lastStatusTick >= StatusInterval)
        {
            pipe.SendOutputState(tick, lastOutputs, kPinCount);
            pipe.SendStatus(tick);
            lastStatusTick = tick;
        }
    }

    if (logFile)
    {
        std::fclose(logFile);
    }
    return 0;
}
