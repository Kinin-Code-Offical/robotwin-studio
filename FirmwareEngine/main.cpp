#include "BoardProfile.h"
#include "PipeManager.h"
#include "Protocol.h"
#include "VirtualArduino.h"

#include <chrono>
#include <algorithm>
#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <memory>
#include <random>
#include <thread>
#include <unordered_map>
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

    std::string NormalizeId(const std::string& value)
    {
        std::string out;
        out.reserve(value.size());
        for (char c : value)
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                out.push_back(c);
            }
            else if (c >= 'A' && c <= 'Z')
            {
                out.push_back(static_cast<char>(c - 'A' + 'a'));
            }
        }
        return out;
    }
}

int main(int argc, char** argv)
{
    std::wstring pipeName = L"RoboTwin.FirmwareEngine";
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

    struct BoardState
    {
        std::string id;
        BoardProfile profile;
        std::unique_ptr<VirtualArduino> mcu;
        bool hasFirmware = false;
        std::uint64_t lastStatusTick = 0;
        double lastTime = 0.0;
        double remainder = 0.0;
        double driftPpm = 0.0;
        std::string eepromPath;
        std::uint8_t lastOutputs[kPinCount]{};
    };

    std::unordered_map<std::string, BoardState> boards;
    std::vector<std::string> boardOrder;
    std::size_t boardOrderIndex = 0;
    std::mt19937 rng(static_cast<unsigned int>(GetTickCount()));
    std::uniform_real_distribution<double> driftDist(-50.0, 50.0);

    auto GetBoardState = [&](const std::string& boardId, const std::string& profileId) -> BoardState& {
        std::string key = boardId.empty() ? "board" : boardId;
        auto it = boards.find(key);
        if (it == boards.end())
        {
            BoardState state{};
            state.id = key;
            state.profile = profileId.empty() ? GetDefaultBoardProfile() : GetBoardProfile(profileId);
            if (state.profile.core_limited && state.profile.pin_count > static_cast<int>(kPinCount))
            {
                Log("Board %s uses %s; pin count limited to %zu by core.", key.c_str(), state.profile.mcu.c_str(), kPinCount);
            }
            state.mcu = std::make_unique<VirtualArduino>(state.profile);
            state.lastTime = QueryNowSeconds();
            state.driftPpm = driftDist(rng);
            if (!logPath)
            {
                std::filesystem::create_directories("logs/firmware/eeprom");
                state.eepromPath = "logs/firmware/eeprom/" + key + ".bin";
            }
            else
            {
                std::filesystem::path base(logPath);
                auto dir = base.parent_path() / "eeprom";
                std::filesystem::create_directories(dir);
                state.eepromPath = (dir / (key + ".bin")).string();
            }
            state.mcu->LoadEepromFromFile(state.eepromPath);
            std::fill(state.lastOutputs, state.lastOutputs + kPinCount, 0xFF);
            auto insert = boards.emplace(key, std::move(state));
            it = insert.first;
            boardOrder.push_back(key);
        }
        else if (!profileId.empty())
        {
            std::string current = NormalizeId(it->second.profile.id);
            std::string desired = NormalizeId(profileId);
            if (!desired.empty() && current != desired)
            {
                it->second.profile = GetBoardProfile(profileId);
                it->second.mcu = std::make_unique<VirtualArduino>(it->second.profile);
                it->second.hasFirmware = false;
                it->second.remainder = 0.0;
                it->second.lastTime = QueryNowSeconds();
                it->second.driftPpm = driftDist(rng);
                it->second.mcu->LoadEepromFromFile(it->second.eepromPath);
                std::fill(it->second.lastOutputs, it->second.lastOutputs + kPinCount, 0xFF);
            }
        }
        return it->second;
    };

    auto UpdateOutputs = [&](BoardState& state) {
        std::uint8_t portb = 0;
        std::uint8_t portc = 0;
        std::uint8_t portd = 0;
        std::uint8_t ddrb = 0;
        std::uint8_t ddrc = 0;
        std::uint8_t ddrd = 0;
        state.mcu->SnapshotPorts(portb, portc, portd, ddrb, ddrc, ddrd);

        for (std::size_t i = 0; i < kPinCount; ++i)
        {
            state.lastOutputs[i] = 0xFF;
        }

        for (int bit = 0; bit < 8; ++bit)
        {
            std::uint8_t mask = static_cast<std::uint8_t>(1u << bit);
            if ((ddrd & mask) == 0) continue;
            bool value = (portd & mask) != 0;
            state.lastOutputs[bit] = value ? 1 : 0;
        }

        for (int bit = 0; bit < 6; ++bit)
        {
            std::uint8_t mask = static_cast<std::uint8_t>(1u << bit);
            if ((ddrb & mask) == 0) continue;
            bool value = (portb & mask) != 0;
            state.lastOutputs[8 + bit] = value ? 1 : 0;
        }

        for (int bit = 0; bit < 6; ++bit)
        {
            std::uint8_t mask = static_cast<std::uint8_t>(1u << bit);
            if ((ddrc & mask) == 0) continue;
            bool value = (portc & mask) != 0;
            state.lastOutputs[14 + bit] = value ? 1 : 0;
        }
    };

    while (true)
    {
        PipeCommand cmd;
        while (pipe.PopCommand(cmd))
        {
            if (cmd.type == PipeCommand::Type::Load)
            {
                auto& state = GetBoardState(cmd.boardId, cmd.boardProfile);
                std::string error;
                if (state.mcu->LoadBvm(cmd.data, error))
                {
                    state.hasFirmware = true;
                    std::fill(state.lastOutputs, state.lastOutputs + kPinCount, 0xFF);
                    Log("Binary Loaded (%zu bytes) for %s", cmd.data.size(), state.id.c_str());
                    pipe.SendLog(state.id, LogLevel::Info, "Binary loaded");
                    state.mcu->SaveEepromToFile(state.eepromPath);
                }
                else
                {
                    Log("Load failed for %s: %s", state.id.c_str(), error.c_str());
                    state.hasFirmware = false;
                    pipe.SendError(state.id, 100, error);
                }
                continue;
            }
            if (cmd.type == PipeCommand::Type::Step)
            {
                auto& state = GetBoardState(cmd.boardId, cmd.boardProfile);
                for (std::size_t i = 0; i < kPinCount; ++i)
                {
                    state.mcu->SetInputPin(static_cast<int>(i), cmd.pins[i] != 0 ? 1 : 0);
                }
                std::size_t analogCount = cmd.analogCount > 0 ? cmd.analogCount : kAnalogCount;
                if (analogCount > kAnalogCount) analogCount = kAnalogCount;
                for (std::size_t i = 0; i < analogCount; ++i)
                {
                    float voltage = static_cast<float>(cmd.analog[i]) * (5.0f / 1023.0f);
                    state.mcu->SetAnalogInput(static_cast<int>(i), voltage);
                }
                if (cmd.deltaMicros > 0)
                {
                    double boardHz = state.profile.cpu_hz > 0.0 ? state.profile.cpu_hz : cpuHz;
                    double cyclesExact = (static_cast<double>(cmd.deltaMicros) * boardHz / 1e6) + state.remainder;
                    std::uint64_t cycles = static_cast<std::uint64_t>(cyclesExact);
                    state.remainder = cyclesExact - static_cast<double>(cycles);
                    state.mcu->SyncInputs();
                    state.mcu->StepCycles(cycles);
                    UpdateOutputs(state);
                    pipe.SendOutputState(state.id, state.mcu->TickCount(), state.lastOutputs, kPinCount);
                }
                continue;
            }
        }

        if (boards.empty())
        {
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
            continue;
        }

        if (lockstep)
        {
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
            continue;
        }

        if (boardOrder.size() != boards.size())
        {
            boardOrder.clear();
            boardOrder.reserve(boards.size());
            for (const auto& entry : boards)
            {
                boardOrder.push_back(entry.first);
            }
            std::sort(boardOrder.begin(), boardOrder.end());
            boardOrderIndex = 0;
        }

        double now = QueryNowSeconds();
        bool anyStepped = false;
        std::size_t count = boardOrder.size();
        for (std::size_t i = 0; i < count; ++i)
        {
            std::size_t index = (boardOrderIndex + i) % count;
            const auto& key = boardOrder[index];
            auto it = boards.find(key);
            if (it == boards.end()) continue;
            auto& state = it->second;
            if (!state.hasFirmware) continue;

            double elapsed = now - state.lastTime;
            state.lastTime = now;
            if (elapsed < 0.0) elapsed = 0.0;

            double driftScale = 1.0 + (state.driftPpm * 1e-6);
            double boardHz = state.profile.cpu_hz > 0.0 ? state.profile.cpu_hz : cpuHz;
            boardHz *= driftScale;
            double cyclesExact = elapsed * boardHz + state.remainder;
            std::uint64_t cycles = static_cast<std::uint64_t>(cyclesExact);
            state.remainder = cyclesExact - static_cast<double>(cycles);
            if (cycles == 0)
            {
                continue;
            }

            state.mcu->SyncInputs();
            state.mcu->StepCycles(cycles);
            UpdateOutputs(state);

            std::uint8_t serialByte = 0;
            if (state.mcu->ConsumeSerialByte(serialByte))
            {
                pipe.SendSerial(state.id, &serialByte, 1);
            }

            std::uint64_t tick = state.mcu->TickCount();
            if (tick - state.lastStatusTick >= StatusInterval)
            {
                pipe.SendOutputState(state.id, tick, state.lastOutputs, kPinCount);
                pipe.SendStatus(state.id, tick);
                state.lastStatusTick = tick;
            }
            anyStepped = true;
        }
        boardOrderIndex = (boardOrderIndex + 1) % count;

        if (!anyStepped)
        {
            std::this_thread::sleep_for(std::chrono::microseconds(200));
        }
    }

    if (logFile)
    {
        std::fclose(logFile);
    }
    return 0;
}
