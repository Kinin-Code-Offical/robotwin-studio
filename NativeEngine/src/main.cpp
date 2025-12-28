#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <chrono>
#include <thread>

#include "Bridge/UnityInterface.h"
#include "Core/MemoryMap.hpp"

namespace
{
    void PrintUsage()
    {
        std::printf("NativeEngineStandalone options:\n");
        std::printf("  --hex <path>     Load Intel HEX file\n");
        std::printf("  --seconds <n>    Run time in seconds (default 1.0)\n");
        std::printf("  --tick-hz <n>    Target tick rate (default 1000)\n");
        std::printf("  --forever        Run until terminated\n");
        std::printf("  --quiet          Suppress periodic output\n");
    }

    bool ParseArg(int& i, int argc, char** argv, const char* flag, const char** value)
    {
        if (std::strcmp(argv[i], flag) != 0) return false;
        if (i + 1 >= argc) return false;
        *value = argv[i + 1];
        i++;
        return true;
    }
}

int main(int argc, char** argv)
{
    const char* hex_path = nullptr;
    double seconds = 1.0;
    double tick_hz = 1000.0;
    bool quiet = false;
    bool forever = false;

    for (int i = 1; i < argc; ++i)
    {
        const char* value = nullptr;
        if (ParseArg(i, argc, argv, "--hex", &value))
        {
            hex_path = value;
        }
        else if (ParseArg(i, argc, argv, "--seconds", &value))
        {
            seconds = std::atof(value);
        }
        else if (ParseArg(i, argc, argv, "--tick-hz", &value))
        {
            tick_hz = std::atof(value);
        }
        else if (std::strcmp(argv[i], "--quiet") == 0)
        {
            quiet = true;
        }
        else if (std::strcmp(argv[i], "--forever") == 0)
        {
            forever = true;
        }
        else if (std::strcmp(argv[i], "--help") == 0)
        {
            PrintUsage();
            return 0;
        }
        else
        {
            std::printf("Unknown argument: %s\n", argv[i]);
            PrintUsage();
            return 1;
        }
    }

    std::size_t mem_bytes = sizeof(core::VirtualMemory) + sizeof(SharedState);
    std::printf("VM static memory: %zu bytes\n", mem_bytes);

    if (hex_path != nullptr)
    {
        int ok = LoadHexFromFile(hex_path);
        std::printf("HEX load: %s\n", ok ? "OK" : "FAILED");
        if (!ok) return 2;
    }

    if (seconds <= 0.0) seconds = 1.0;
    if (tick_hz <= 0.0) tick_hz = 1000.0;

    double elapsed = 0.0;
    double next_print = 0.0;
    double tick_interval = 1.0 / tick_hz;
    auto start = std::chrono::steady_clock::now();
    auto last_tick = start;
    auto next_tick = start;

    while (forever || elapsed < seconds)
    {
        next_tick += std::chrono::duration_cast<std::chrono::steady_clock::duration>(
            std::chrono::duration<double>(tick_interval));

        auto now = std::chrono::steady_clock::now();
        if (now < next_tick)
        {
            auto sleep_for = next_tick - now;
            if (sleep_for > std::chrono::microseconds(200))
            {
                std::this_thread::sleep_for(sleep_for - std::chrono::microseconds(100));
            }
            while (std::chrono::steady_clock::now() < next_tick) { }
        }

        now = std::chrono::steady_clock::now();
        double dt = std::chrono::duration<double>(now - last_tick).count();
        last_tick = now;
        StepSimulation(static_cast<float>(dt));
        elapsed = std::chrono::duration<double>(now - start).count();

        if (!quiet && elapsed >= next_print)
        {
            const SharedState* state = GetSharedState();
            if (state != nullptr)
            {
                std::printf("t=%.3fs tick=%u D13=%.2fV LED=%.2fV I=%.3fA errors=0x%X\n",
                    elapsed,
                    state->tick,
                    state->node_voltages[2],
                    state->node_voltages[3],
                    state->currents[0],
                    state->error_flags);
            }
            next_print += 0.5;
        }
    }

    return 0;
}
