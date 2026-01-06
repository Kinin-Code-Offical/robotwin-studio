#include "BoardProfile.h"
#include "PipeManager.h"
#include "Protocol.h"
#include "VirtualMcu.h"

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
        static LARGE_INTEGER frequency = []()
        {
            LARGE_INTEGER freq;
            QueryPerformanceFrequency(&freq);
            return freq;
        }();
        LARGE_INTEGER now;
        QueryPerformanceCounter(&now);
        return static_cast<double>(now.QuadPart) / static_cast<double>(frequency.QuadPart);
    }

    bool ParseArg(const char *arg, const char *key)
    {
        return std::strcmp(arg, key) == 0;
    }

    bool RunSelfTest()
    {
        BoardProfile profile = GetDefaultBoardProfile();
        VirtualMcu mcu(profile);

        std::printf("[SelfTest] ADC...\n");
        mcu.SetAnalogInput(0, 2.5f);
        mcu.SetIo(AVR_ADMUX, 0);
        mcu.SetIo(AVR_ADCSRA, static_cast<std::uint8_t>((1u << 7) | (1u << 6)));
        mcu.StepCycles(2000);
        std::uint8_t adcsra = mcu.GetIo(AVR_ADCSRA);
        if ((adcsra & (1u << 4)) == 0)
        {
            std::printf("[SelfTest] ADC flag missing\n");
            return false;
        }

        std::printf("[SelfTest] UART...\n");
        mcu.SetIo(AVR_UBRR0L, 0);
        mcu.SetIo(AVR_UBRR0H, 0);
        mcu.SetIo(AVR_UCSR0B, static_cast<std::uint8_t>((1u << 4) | (1u << 3)));
        mcu.QueueSerialInput('A');
        mcu.StepCycles(2000);
        std::uint8_t ucsra = mcu.GetIo(AVR_UCSR0A);
        if ((ucsra & (1u << 7)) == 0)
        {
            std::printf("[SelfTest] UART RXC flag missing\n");
            return false;
        }
        std::uint8_t udr = mcu.GetIo(AVR_UDR0);
        if (udr != static_cast<std::uint8_t>('A'))
        {
            std::printf("[SelfTest] UART data mismatch\n");
            return false;
        }

        std::printf("[SelfTest] Timer0...\n");
        mcu.SetIo(AVR_TCCR0B, 1);
        std::uint8_t before = mcu.GetIo(AVR_TCNT0);
        mcu.StepCycles(200);
        std::uint8_t after = mcu.GetIo(AVR_TCNT0);
        if (after == before)
        {
            std::printf("[SelfTest] Timer0 did not tick\n");
            return false;
        }

        std::printf("[SelfTest] Timer1...\n");
        mcu.SetIo(AVR_TCCR1B, 1);
        std::uint16_t t1Before = static_cast<std::uint16_t>(mcu.GetIo(AVR_TCNT1L)) |
                                 (static_cast<std::uint16_t>(mcu.GetIo(AVR_TCNT1H)) << 8);
        mcu.StepCycles(200);
        std::uint16_t t1After = static_cast<std::uint16_t>(mcu.GetIo(AVR_TCNT1L)) |
                                (static_cast<std::uint16_t>(mcu.GetIo(AVR_TCNT1H)) << 8);
        if (t1After == t1Before)
        {
            std::printf("[SelfTest] Timer1 did not tick\n");
            return false;
        }

        std::printf("[SelfTest] SPI...\n");
        mcu.SetIo(AVR_SPCR, static_cast<std::uint8_t>((1u << 6) | (1u << 4)));
        mcu.SetIo(AVR_SPDR, 0x5A);
        mcu.StepCycles(64);
        std::uint8_t spsr = mcu.GetIo(AVR_SPSR);
        if ((spsr & (1u << 7)) == 0)
        {
            std::printf("[SelfTest] SPI transfer flag missing\n");
            return false;
        }

        std::printf("[SelfTest] TWI...\n");
        mcu.SetIo(AVR_TWBR, 0x20);
        mcu.SetIo(AVR_TWCR, static_cast<std::uint8_t>(1u << 2));
        mcu.SetIo(AVR_TWDR, 0x3C);
        mcu.StepCycles(2000);
        std::uint8_t twcr = mcu.GetIo(AVR_TWCR);
        if ((twcr & (1u << 7)) == 0)
        {
            std::printf("[SelfTest] TWI flag missing\n");
            return false;
        }

        std::printf("[SelfTest] OK\n");
        return true;
    }

    std::string NormalizeId(const std::string &value)
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

    bool IsSupportedMcu(const BoardProfile &profile)
    {
        return profile.mcu == "ATmega328P" || profile.mcu == "ATmega2560";
    }
}

int main(int argc, char **argv)
{
    std::wstring pipeName = L"RoboTwin.FirmwareEngine";
    double cpuHz = DefaultCpuHz;
    bool lockstep = true;
    const char *logPath = nullptr;
    bool selfTest = false;
    bool traceLockstep = false;

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
            if (cpuHz <= 0.0)
                cpuHz = DefaultCpuHz;
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
        if (ParseArg(argv[i], "--self-test"))
        {
            selfTest = true;
            continue;
        }
        if (ParseArg(argv[i], "--trace-lockstep"))
        {
            traceLockstep = true;
            continue;
        }
    }

    if (!traceLockstep)
    {
        const char *env = std::getenv("RTFW_LOCKSTEP_TRACE");
        if (env != nullptr && env[0] != '\0' && env[0] != '0')
        {
            traceLockstep = true;
        }
    }

    if (selfTest)
    {
        return RunSelfTest() ? 0 : 2;
    }

    std::FILE *logFile = nullptr;
    if (logPath != nullptr)
    {
        logFile = std::fopen(logPath, "a");
    }

    auto Log = [&](const char *fmt, ...)
    {
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

    Log("RoboTwinFirmwareHost - CoreSim Standalone");

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
        std::unique_ptr<VirtualMcu> mcu;
        bool supported = true;
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

    auto GetBoardState = [&](const std::string &boardId, const std::string &profileId) -> BoardState &
    {
        std::string key = boardId.empty() ? "board" : boardId;
        auto it = boards.find(key);
        if (it == boards.end())
        {
            BoardState state{};
            state.id = key;
            state.profile = profileId.empty() ? GetDefaultBoardProfile() : GetBoardProfile(profileId);
            state.supported = IsSupportedMcu(state.profile);
            if (!state.supported)
            {
                Log("Board %s uses unsupported MCU profile %s.", key.c_str(), state.profile.mcu.c_str());
            }
            if (state.profile.core_limited && state.profile.pin_count > static_cast<int>(kPinCount))
            {
                Log("Board %s uses %s; pin count limited to %zu by core.", key.c_str(), state.profile.mcu.c_str(), kPinCount);
            }
            state.mcu = std::make_unique<VirtualMcu>(state.profile);
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
                it->second.supported = IsSupportedMcu(it->second.profile);
                if (!it->second.supported)
                {
                    Log("Board %s switched to unsupported MCU profile %s.", key.c_str(), it->second.profile.mcu.c_str());
                }
                it->second.mcu = std::make_unique<VirtualMcu>(it->second.profile);
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

    auto UpdateOutputs = [&](BoardState &state)
    {
        state.mcu->SamplePinOutputs(state.lastOutputs, kPinCount);
    };

    while (true)
    {
        PipeCommand cmd;
        while (pipe.PopCommand(cmd))
        {
            if (cmd.type == PipeCommand::Type::Load)
            {
                auto &state = GetBoardState(cmd.boardId, cmd.boardProfile);
                if (!state.supported)
                {
                    std::string message = "Unsupported MCU profile.";
                    Log("Load rejected for %s: %s", state.id.c_str(), message.c_str());
                    pipe.SendError(state.id, 120, message);
                    continue;
                }
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
                auto &state = GetBoardState(cmd.boardId, cmd.boardProfile);
                if (!state.supported)
                {
                    pipe.SendError(state.id, 121, "Step rejected for unsupported MCU profile.");
                    continue;
                }

                if (traceLockstep)
                {
                    Log("[Lockstep] Step rx board=%s seq=%llu dt_us=%u sent_us=%llu has_fw=%d", state.id.c_str(),
                        static_cast<unsigned long long>(cmd.stepSequence), static_cast<unsigned int>(cmd.deltaMicros),
                        static_cast<unsigned long long>(cmd.sentMicros), state.hasFirmware ? 1 : 0);
                }

                for (std::size_t i = 0; i < kPinCount; ++i)
                {
                    state.mcu->SetInputPin(static_cast<int>(i), cmd.pins[i] != 0 ? 1 : 0);
                }
                std::size_t analogCount = cmd.analogCount > 0 ? cmd.analogCount : kAnalogCount;
                if (analogCount > kAnalogCount)
                    analogCount = kAnalogCount;
                for (std::size_t i = 0; i < analogCount; ++i)
                {
                    float voltage = static_cast<float>(cmd.analog[i]) * (5.0f / 1023.0f);
                    state.mcu->SetAnalogInput(static_cast<int>(i), voltage);
                }
                // Lockstep contract: every Step must emit an OutputState.
                // (Previously this was gated on deltaMicros>0, which can stall recorders/tests.)
                // Note: avoid invoking MCU stepping paths when no time advances or when firmware isn't loaded.
                if (cmd.deltaMicros > 0 && state.hasFirmware)
                {
                    state.mcu->SyncInputs();
                    double boardHz = state.profile.cpu_hz > 0.0 ? state.profile.cpu_hz : cpuHz;
                    double cyclesExact = (static_cast<double>(cmd.deltaMicros) * boardHz / 1e6) + state.remainder;
                    std::uint64_t cycles = static_cast<std::uint64_t>(cyclesExact);
                    state.remainder = cyclesExact - static_cast<double>(cycles);
                    if (cycles > 0)
                    {
                        state.mcu->StepCycles(cycles);
                    }
                }
                UpdateOutputs(state);
                const auto &perf = state.mcu->GetPerfCounters();
                const auto tick = state.mcu->TickCount();
                const bool sent = pipe.SendOutputState(state.id, cmd.stepSequence, tick, state.lastOutputs, kPinCount,
                                                       perf.cycles, perf.adcSamples,
                                                       perf.uartTxBytes, perf.uartRxBytes,
                                                       perf.spiTransfers, perf.twiTransfers, perf.wdtResets);

                if (traceLockstep)
                {
                    Log("[Lockstep] OutputState tx board=%s seq=%llu tick=%llu ok=%d connected=%d", state.id.c_str(),
                        static_cast<unsigned long long>(cmd.stepSequence),
                        static_cast<unsigned long long>(tick), sent ? 1 : 0, pipe.IsConnected() ? 1 : 0);
                    if (!sent)
                    {
                        Log("[Lockstep] OutputState write error=%lu", static_cast<unsigned long>(pipe.LastWriteError()));
                    }
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
            for (const auto &entry : boards)
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
            const auto &key = boardOrder[index];
            auto it = boards.find(key);
            if (it == boards.end())
                continue;
            auto &state = it->second;
            if (!state.hasFirmware)
                continue;

            double elapsed = now - state.lastTime;
            state.lastTime = now;
            if (elapsed < 0.0)
                elapsed = 0.0;

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
                const auto &perf = state.mcu->GetPerfCounters();
                pipe.SendOutputState(state.id, 0, tick, state.lastOutputs, kPinCount,
                                     perf.cycles, perf.adcSamples,
                                     perf.uartTxBytes, perf.uartRxBytes,
                                     perf.spiTransfers, perf.twiTransfers, perf.wdtResets);
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
