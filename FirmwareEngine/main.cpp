// Windows.h conflicts with Arduino macros
#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#endif

#include "BoardProfile.h"
#include "PipeManager.h"
#include "Protocol.h"
#include "Rpi/RpiBackend.h"
#include "VirtualMcu.h"

#include <chrono>
#include <algorithm>
#include <atomic>
#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cwctype>
#include <filesystem>
#include <functional>
#include <memory>
#include <mutex>
#include <random> // Fix missing include

#ifdef _WIN32
#include <windows.h>
#undef INPUT
#undef OUTPUT
#undef min
#undef max
#endif

// Arduino/U1 High Level Emulation headers removed for Real Sim optimization.
// #include "Arduino.h"
// #include "Wire.h"
#undef min
#undef max
#undef abs
#include <random>
#include <thread>
#include <unordered_map>
#include <vector>

using namespace firmware;

// Define Global State for U1 HLE - REMOVED for Real Sim
/*
firmware::StepPayload g_inputState;
firmware::OutputStatePayload g_outputState;
uint32_t g_millis = 0;
std::string g_serialBuffer;
SerialMock Serial;
TwoWire Wire;
*/

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

    bool ParseSize(const char *value, int &outW, int &outH)
    {
        if (!value)
            return false;
        const char *x = std::strchr(value, 'x');
        if (!x)
            return false;
        outW = std::atoi(value);
        outH = std::atoi(x + 1);
        return outW > 0 && outH > 0;
    }

    std::filesystem::path LogRoot()
    {
        return std::filesystem::path("logs") / "FirmwareEngine";
    }

    std::wstring ToLower(std::wstring value)
    {
        for (auto &ch : value)
        {
            ch = static_cast<wchar_t>(std::towlower(ch));
        }
        return value;
    }

    bool IsUnderLogRoot(const std::filesystem::path &path)
    {
        auto root = std::filesystem::absolute(LogRoot()).lexically_normal();
        auto full = std::filesystem::absolute(path).lexically_normal();
        auto rootStr = ToLower(root.native());
        auto fullStr = ToLower(full.native());
        if (fullStr.size() < rootStr.size())
            return false;
        if (fullStr.compare(0, rootStr.size(), rootStr) != 0)
            return false;
        if (fullStr.size() == rootStr.size())
            return true;
        wchar_t next = fullStr[rootStr.size()];
        return next == L'\\' || next == L'/';
    }

    std::filesystem::path EnsureLogPath(const std::filesystem::path &requested, const std::filesystem::path &fallback)
    {
        if (!requested.empty() && IsUnderLogRoot(requested))
        {
            return requested;
        }
        return LogRoot() / fallback;
    }

    void FormatOpcodeMnemonic(std::uint16_t opcode, char *out, std::size_t outSize)
    {
        if (!out || outSize == 0)
            return;
        const char *text = nullptr;
        if (opcode == 0x0000)
        {
            text = "NOP";
        }
        else if (opcode == 0x9508)
        {
            text = "RET";
        }
        else if (opcode == 0x9518)
        {
            text = "RETI";
        }
        else if ((opcode & 0xF000) == 0xC000)
        {
            text = "RJMP";
        }
        else if ((opcode & 0xF000) == 0xD000)
        {
            text = "RCALL";
        }
        else if ((opcode & 0xF000) == 0xE000)
        {
            text = "LDI";
        }
        else if ((opcode & 0xF800) == 0xB000)
        {
            text = "IN";
        }
        else if ((opcode & 0xF800) == 0xB800)
        {
            text = "OUT";
        }
        else if ((opcode & 0xFE0F) == 0x900F)
        {
            text = "POP";
        }
        else if ((opcode & 0xFE0F) == 0x920F)
        {
            text = "PUSH";
        }
        else if ((opcode & 0xFE0F) == 0x9000)
        {
            text = "LDS";
        }
        else if ((opcode & 0xFE0F) == 0x9200)
        {
            text = "STS";
        }
        else if ((opcode & 0xFC00) == 0x0C00)
        {
            text = "ADD";
        }
        else if ((opcode & 0xFC00) == 0x1C00)
        {
            text = "ADC";
        }
        else if ((opcode & 0xFC00) == 0x1800)
        {
            text = "SUB";
        }
        else if ((opcode & 0xFC00) == 0x0800)
        {
            text = "SBC";
        }
        else if ((opcode & 0xF000) == 0x6000)
        {
            text = "ORI";
        }
        else if ((opcode & 0xF000) == 0x7000)
        {
            text = "ANDI";
        }
        else if ((opcode & 0xFF00) == 0x9A00)
        {
            text = "SBI";
        }
        else if ((opcode & 0xFF00) == 0x9800)
        {
            text = "CBI";
        }
        else
        {
            text = "OP";
        }
        std::snprintf(out, outSize, "%s", text);
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
    bool traceCpu = false;
    std::uint32_t traceCpuInterval = 1;
    std::uint32_t traceCpuMax = 256;
    std::string ideComPort;
    std::string ideBoardId = "board";
    std::string ideBoardProfile = "ArduinoUno";
    bool rpiEnabled = false;
    bool rpiAllowMock = false;
    std::string rpiQemuPath;
    std::string rpiImagePath;
    std::string rpiShmDir;
    std::string rpiNetMode = "nat";
    std::string rpiLogPath;
    int rpiDisplayW = 320;
    int rpiDisplayH = 200;
    int rpiCameraW = 320;
    int rpiCameraH = 200;
    std::uint64_t rpiCpuAffinity = 0;
    std::uint32_t rpiCpuPercent = 0;
    std::uint32_t rpiThreads = 0;
    std::uint32_t rpiPriorityClass = 0;
    std::string logPathValue;
    std::string rpiShmDirValue;
    std::string rpiLogPathValue;

    // Help flag
    bool showHelp = false;

    for (int i = 1; i < argc; ++i)
    {
        if (ParseArg(argv[i], "--help") || ParseArg(argv[i], "-h") || ParseArg(argv[i], "/?"))
        {
            showHelp = true;
            break;
        }
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
        if (ParseArg(argv[i], "--trace-cpu"))
        {
            traceCpu = true;
            continue;
        }
        if (ParseArg(argv[i], "--trace-cpu-interval") && i + 1 < argc)
        {
            traceCpuInterval = static_cast<std::uint32_t>(std::max(1, std::atoi(argv[i + 1])));
            ++i;
            continue;
        }
        if (ParseArg(argv[i], "--trace-cpu-max") && i + 1 < argc)
        {
            traceCpuMax = static_cast<std::uint32_t>(std::max(1, std::atoi(argv[i + 1])));
            ++i;
            continue;
        }
        if (ParseArg(argv[i], "--ide-com") && i + 1 < argc)
        {
            ideComPort = argv[i + 1];
            ++i;
            continue;
        }
        if (ParseArg(argv[i], "--ide-board") && i + 1 < argc)
        {
            ideBoardId = argv[i + 1];
            ++i;
            continue;
        }
        if (ParseArg(argv[i], "--ide-profile") && i + 1 < argc)
        {
            ideBoardProfile = argv[i + 1];
            ++i;
            continue;
        }
        if (ParseArg(argv[i], "--rpi-enable"))
        {
            rpiEnabled = true;
            continue;
        }
        if (ParseArg(argv[i], "--rpi-allow-mock"))
        {
            rpiAllowMock = true;
            continue;
        }
        if (ParseArg(argv[i], "--rpi-qemu") && i + 1 < argc)
        {
            rpiQemuPath = argv[i + 1];
            ++i;
            continue;
        }
        if (ParseArg(argv[i], "--rpi-image") && i + 1 < argc)
        {
            rpiImagePath = argv[i + 1];
            ++i;
            continue;
        }
        if (ParseArg(argv[i], "--rpi-shm-dir") && i + 1 < argc)
        {
            rpiShmDir = argv[i + 1];
            ++i;
            continue;
        }
        if (ParseArg(argv[i], "--rpi-display") && i + 1 < argc)
        {
            ParseSize(argv[i + 1], rpiDisplayW, rpiDisplayH);
            ++i;
            continue;
        }
        if (ParseArg(argv[i], "--rpi-camera") && i + 1 < argc)
        {
            ParseSize(argv[i + 1], rpiCameraW, rpiCameraH);
            ++i;
            continue;
        }
        if (ParseArg(argv[i], "--rpi-net-mode") && i + 1 < argc)
        {
            rpiNetMode = argv[i + 1];
            ++i;
            continue;
        }
        if (ParseArg(argv[i], "--rpi-log") && i + 1 < argc)
        {
            rpiLogPath = argv[i + 1];
            ++i;
            continue;
        }
        if (ParseArg(argv[i], "--rpi-cpu-affinity") && i + 1 < argc)
        {
            rpiCpuAffinity = std::strtoull(argv[i + 1], nullptr, 0);
            ++i;
            continue;
        }
        if (ParseArg(argv[i], "--rpi-cpu-max-percent") && i + 1 < argc)
        {
            rpiCpuPercent = static_cast<std::uint32_t>(std::atoi(argv[i + 1]));
            ++i;
            continue;
        }
        if (ParseArg(argv[i], "--rpi-threads") && i + 1 < argc)
        {
            rpiThreads = static_cast<std::uint32_t>(std::atoi(argv[i + 1]));
            ++i;
            continue;
        }
        if (ParseArg(argv[i], "--rpi-priority") && i + 1 < argc)
        {
            rpiPriorityClass = static_cast<std::uint32_t>(std::atoi(argv[i + 1]));
            ++i;
            continue;
        }
    }

    if (showHelp)
    {
        std::printf("RoboTwinFirmwareHost - CoreSim Firmware Engine\n\n");
        std::printf("Usage: RoboTwinFirmwareHost [OPTIONS]\n\n");
        std::printf("Options:\n");
        std::printf("  --help, -h, /?           Show this help message\n");
        std::printf("  --self-test              Run hardware self-tests and exit\n");
        std::printf("  --pipe <name>            Named pipe for Unity communication (default: RoboTwin.FirmwareEngine)\n");
        std::printf("  --hz <frequency>         CPU frequency in Hz (default: 16000000)\n");
        std::printf("  --lockstep               Enable lockstep mode (default)\n");
        std::printf("  --realtime               Disable lockstep, run in realtime\n");
        std::printf("  --log <path>             Log file path\n");
        std::printf("  --trace-lockstep         Enable lockstep trace logging\n");
        std::printf("  --trace-cpu              Enable instruction trace logging\n");
        std::printf("  --trace-cpu-interval <n> Instruction trace sampling interval (default: 1)\n");
        std::printf("  --trace-cpu-max <n>      Max trace lines sent per step (default: 256)\n");
        std::printf("\n");
        std::printf("IDE Integration:\n");
        std::printf("  --ide-com <port>         COM port for STK500 protocol (e.g., COM3)\n");
        std::printf("  --ide-board <id>         Board identifier (default: board)\n");
        std::printf("  --ide-profile <name>     Board profile (default: ArduinoUno)\n");
        std::printf("\n");
        std::printf("Raspberry Pi Options:\n");
        std::printf("  --rpi-enable             Enable RPi backend\n");
        std::printf("  --rpi-allow-mock         Allow mock RPi if QEMU unavailable\n");
        std::printf("  --rpi-qemu <path>        Path to QEMU executable\n");
        std::printf("  --rpi-image <path>       Path to RPi disk image\n");
        std::printf("  --rpi-shm-dir <dir>      Shared memory directory (default: logs/FirmwareEngine/rpi/shm)\n");
        std::printf("  --rpi-display <WxH>      Display resolution (default: 320x200)\n");
        std::printf("  --rpi-camera <WxH>       Camera resolution (default: 320x200)\n");
        std::printf("  --rpi-net-mode <mode>    Network mode: nat, bridge, none (default: nat)\n");
        std::printf("  --rpi-log <path>         RPi log file path\n");
        std::printf("  --rpi-cpu-affinity <n>   CPU affinity mask\n");
        std::printf("  --rpi-cpu-max-percent <n> Max CPU percentage\n");
        std::printf("  --rpi-threads <n>        Thread count\n");
        std::printf("  --rpi-priority <n>       Process priority class\n");
        std::printf("\n");
        std::printf("Examples:\n");
        std::printf("  RoboTwinFirmwareHost --self-test\n");
        std::printf("  RoboTwinFirmwareHost --lockstep --hz 16000000\n");
        std::printf("  RoboTwinFirmwareHost --ide-com COM3 --ide-profile ArduinoMega\n");
        std::printf("\n");
        return 0;
    }

    if (!traceLockstep)
    {
        const char *env = std::getenv("RTFW_LOCKSTEP_TRACE");
        if (env != nullptr && env[0] != '\0' && env[0] != '0')
        {
            traceLockstep = true;
        }
    }

    if (!traceCpu)
    {
        const char *env = std::getenv("RTFW_CPU_TRACE");
        if (env != nullptr && env[0] != '\0' && env[0] != '0')
        {
            traceCpu = true;
        }
    }
    const char *traceIntervalEnv = std::getenv("RTFW_CPU_TRACE_INTERVAL");
    if (traceIntervalEnv != nullptr && traceIntervalEnv[0] != '\0')
    {
        traceCpuInterval = static_cast<std::uint32_t>(std::max(1, std::atoi(traceIntervalEnv)));
    }
    const char *traceMaxEnv = std::getenv("RTFW_CPU_TRACE_MAX");
    if (traceMaxEnv != nullptr && traceMaxEnv[0] != '\0')
    {
        traceCpuMax = static_cast<std::uint32_t>(std::max(1, std::atoi(traceMaxEnv)));
    }

    if (selfTest)
    {
        return RunSelfTest() ? 0 : 2;
    }

    if (logPath != nullptr)
    {
        auto safePath = EnsureLogPath(logPath, "firmware.log");
        logPathValue = safePath.string();
        logPath = logPathValue.c_str();
    }

    if (!rpiShmDir.empty())
    {
        auto safePath = EnsureLogPath(rpiShmDir, std::filesystem::path("rpi") / "shm");
        rpiShmDirValue = safePath.string();
        rpiShmDir = rpiShmDirValue;
    }

    if (!rpiLogPath.empty())
    {
        auto safePath = EnsureLogPath(rpiLogPath, std::filesystem::path("rpi") / "rpi.log");
        rpiLogPathValue = safePath.string();
        rpiLogPath = rpiLogPathValue;
    }

    std::FILE *logFile = nullptr;
    if (logPath != nullptr)
    {
        std::filesystem::path logDir = std::filesystem::path(logPath).parent_path();
        if (!logDir.empty())
        {
            std::filesystem::create_directories(logDir);
        }
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

    if (!ideComPort.empty())
    {
        Log("[IDE] STK500 bridge enabled: com=%s board=%s profile=%s", ideComPort.c_str(), ideBoardId.c_str(), ideBoardProfile.c_str());
    }

    PipeManager pipe;
    if (!pipe.Start(pipeName))
    {
        Log("Failed to start pipe server.");
        return 1;
    }

    rpi::RpiBackend rpiBackend;
    if (rpiEnabled)
    {
        rpi::RpiConfig config{};
        config.enabled = true;
        config.allow_mock = rpiAllowMock;
        config.qemu_path = rpiQemuPath;
        config.image_path = rpiImagePath;
        if (rpiShmDir.empty())
        {
            config.shm_dir = (LogRoot() / "rpi" / "shm").string();
        }
        else
        {
            config.shm_dir = rpiShmDir;
        }
        config.net_mode = rpiNetMode;
        config.display_width = rpiDisplayW;
        config.display_height = rpiDisplayH;
        config.camera_width = rpiCameraW;
        config.camera_height = rpiCameraH;
        config.cpu_affinity_mask = rpiCpuAffinity;
        config.cpu_max_percent = rpiCpuPercent;
        config.thread_count = rpiThreads;
        config.cpu_priority_class = rpiPriorityClass;
        if (rpiLogPath.empty())
        {
            config.log_path = (LogRoot() / "rpi" / "rpi.log").string();
        }
        else
        {
            config.log_path = rpiLogPath;
        }
        std::filesystem::create_directories(config.shm_dir);
        if (!config.log_path.empty())
        {
            std::filesystem::create_directories(std::filesystem::path(config.log_path).parent_path());
        }
        auto logFn = [&](const char *msg)
        {
            Log("%s", msg);
        };
        rpiBackend.Start(config, logFn);
        Log("[RPI] backend enabled (mock=%d)", config.allow_mock ? 1 : 0);
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

    std::mutex boardsMutex;

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
            state.mcu->EnableCpuTrace(traceCpu);
            state.mcu->SetCpuTraceInterval(traceCpuInterval);
            state.lastTime = QueryNowSeconds();
            state.driftPpm = driftDist(rng);
            if (!logPath)
            {
                auto eepromDir = LogRoot() / "eeprom";
                std::filesystem::create_directories(eepromDir);
                state.eepromPath = (eepromDir / (key + ".bin")).string();
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
                it->second.mcu->EnableCpuTrace(traceCpu);
                it->second.mcu->SetCpuTraceInterval(traceCpuInterval);
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

    struct IdeBridge
    {
        enum : std::uint8_t
        {
            STK_OK = 0x10,
            STK_FAILED = 0x11,
            STK_INSYNC = 0x14,
            STK_NOSYNC = 0x15,
            CRC_EOP = 0x20,

            STK_GET_SYNC = 0x30,
            STK_GET_PARAMETER = 0x41,
            STK_SET_DEVICE = 0x42,
            STK_SET_DEVICE_EXT = 0x45,
            STK_ENTER_PROGMODE = 0x50,
            STK_LEAVE_PROGMODE = 0x51,
            STK_LOAD_ADDRESS = 0x55,
            STK_PROG_PAGE = 0x64,
            STK_READ_PAGE = 0x74,
            STK_READ_SIGN = 0x75,
        };

        std::thread thread;
        std::atomic<bool> running{false};
        HANDLE port = INVALID_HANDLE_VALUE;
        std::uint32_t addressWords = 0;
        bool inProgMode = false;

        std::function<void(const char *)> log;
        std::function<BoardState &(const std::string &, const std::string &)> getBoard;
        std::mutex *boardsMutex = nullptr;
        std::string boardId;
        std::string boardProfile;

        bool Start(const std::string &com, const std::string &id, const std::string &profile,
                   std::function<void(const char *)> logFn,
                   std::function<BoardState &(const std::string &, const std::string &)> getBoardFn,
                   std::mutex *mutexPtr)
        {
            boardId = id.empty() ? "board" : id;
            boardProfile = profile.empty() ? "ArduinoUno" : profile;
            log = std::move(logFn);
            getBoard = std::move(getBoardFn);
            boardsMutex = mutexPtr;

            std::wstring device = L"\\\\.\\";
            int len = MultiByteToWideChar(CP_UTF8, 0, com.c_str(), -1, nullptr, 0);
            if (len <= 0)
            {
                log("[IDE] Invalid COM port string");
                return false;
            }
            std::wstring comW;
            comW.resize(static_cast<std::size_t>(len));
            MultiByteToWideChar(CP_UTF8, 0, com.c_str(), -1, comW.data(), len);
            if (!comW.empty() && comW.back() == L'\0')
            {
                comW.pop_back();
            }
            device += comW;

            port = CreateFileW(device.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
            if (port == INVALID_HANDLE_VALUE)
            {
                log("[IDE] Failed to open COM port (CreateFileW)");
                return false;
            }

            DCB dcb{};
            dcb.DCBlength = sizeof(DCB);
            if (!GetCommState(port, &dcb))
            {
                log("[IDE] GetCommState failed");
                CloseHandle(port);
                port = INVALID_HANDLE_VALUE;
                return false;
            }

            dcb.BaudRate = CBR_115200;
            dcb.ByteSize = 8;
            dcb.Parity = NOPARITY;
            dcb.StopBits = ONESTOPBIT;
            dcb.fBinary = TRUE;
            dcb.fDtrControl = DTR_CONTROL_ENABLE;
            dcb.fRtsControl = RTS_CONTROL_ENABLE;
            dcb.fOutxCtsFlow = FALSE;
            dcb.fOutxDsrFlow = FALSE;
            dcb.fOutX = FALSE;
            dcb.fInX = FALSE;
            dcb.fParity = FALSE;

            if (!SetCommState(port, &dcb))
            {
                log("[IDE] SetCommState failed");
                CloseHandle(port);
                port = INVALID_HANDLE_VALUE;
                return false;
            }

            COMMTIMEOUTS timeouts{};
            timeouts.ReadIntervalTimeout = 10;
            timeouts.ReadTotalTimeoutMultiplier = 0;
            timeouts.ReadTotalTimeoutConstant = 50;
            timeouts.WriteTotalTimeoutMultiplier = 0;
            timeouts.WriteTotalTimeoutConstant = 200;
            SetCommTimeouts(port, &timeouts);

            PurgeComm(port, PURGE_RXCLEAR | PURGE_TXCLEAR);

            running = true;
            thread = std::thread([this]()
                                 { this->Run(); });
            return true;
        }

        void Stop()
        {
            running = false;
            if (thread.joinable())
            {
                thread.join();
            }
            if (port != INVALID_HANDLE_VALUE)
            {
                CloseHandle(port);
                port = INVALID_HANDLE_VALUE;
            }
        }

        bool ReadExact(std::uint8_t *buffer, std::size_t size)
        {
            std::size_t offset = 0;
            while (running && offset < size)
            {
                DWORD got = 0;
                if (!ReadFile(port, buffer + offset, static_cast<DWORD>(size - offset), &got, nullptr))
                {
                    return false;
                }
                if (got == 0)
                {
                    continue;
                }
                offset += got;
            }
            return offset == size;
        }

        bool WriteAll(const std::uint8_t *buffer, std::size_t size)
        {
            std::size_t offset = 0;
            while (running && offset < size)
            {
                DWORD wrote = 0;
                if (!WriteFile(port, buffer + offset, static_cast<DWORD>(size - offset), &wrote, nullptr))
                {
                    return false;
                }
                if (wrote == 0)
                {
                    continue;
                }
                offset += wrote;
            }
            return offset == size;
        }

        void ReplyOk()
        {
            const std::uint8_t out[2] = {STK_INSYNC, STK_OK};
            WriteAll(out, sizeof(out));
        }

        void ReplyFail()
        {
            const std::uint8_t out[2] = {STK_INSYNC, STK_FAILED};
            WriteAll(out, sizeof(out));
        }

        void ReplyNoSync()
        {
            const std::uint8_t out[1] = {STK_NOSYNC};
            WriteAll(out, sizeof(out));
        }

        void ReplyData(const std::uint8_t *data, std::size_t size)
        {
            std::vector<std::uint8_t> out;
            out.reserve(2 + size);
            out.push_back(STK_INSYNC);
            for (std::size_t i = 0; i < size; ++i)
            {
                out.push_back(data[i]);
            }
            out.push_back(STK_OK);
            WriteAll(out.data(), out.size());
        }

        void Run()
        {
            if (port == INVALID_HANDLE_VALUE)
            {
                return;
            }
            log("[IDE] Listening for STK500v1...");

            while (running)
            {
                std::uint8_t cmd = 0;
                if (!ReadExact(&cmd, 1))
                {
                    std::this_thread::sleep_for(std::chrono::milliseconds(1));
                    continue;
                }

                switch (cmd)
                {
                case STK_GET_SYNC:
                {
                    std::uint8_t eop = 0;
                    if (!ReadExact(&eop, 1) || eop != CRC_EOP)
                    {
                        ReplyNoSync();
                        break;
                    }
                    ReplyOk();
                    break;
                }
                case STK_GET_PARAMETER:
                {
                    std::uint8_t which = 0;
                    std::uint8_t eop = 0;
                    if (!ReadExact(&which, 1) || !ReadExact(&eop, 1) || eop != CRC_EOP)
                    {
                        ReplyNoSync();
                        break;
                    }
                    // Minimal responses.
                    std::uint8_t value = 0;
                    if (which == 0x82)
                        value = 0x02; // HW_VER
                    if (which == 0x81)
                        value = 0x01; // SW_MAJOR
                    if (which == 0x80)
                        value = 0x01; // SW_MINOR
                    ReplyData(&value, 1);
                    break;
                }
                case STK_SET_DEVICE:
                {
                    std::uint8_t payload[20] = {};
                    if (!ReadExact(payload, sizeof(payload)))
                    {
                        ReplyNoSync();
                        break;
                    }
                    std::uint8_t eop = 0;
                    if (!ReadExact(&eop, 1) || eop != CRC_EOP)
                    {
                        ReplyNoSync();
                        break;
                    }
                    ReplyOk();
                    break;
                }
                case STK_SET_DEVICE_EXT:
                {
                    std::uint8_t len = 0;
                    if (!ReadExact(&len, 1))
                    {
                        ReplyNoSync();
                        break;
                    }
                    std::vector<std::uint8_t> payload(len);
                    if (len > 0 && !ReadExact(payload.data(), payload.size()))
                    {
                        ReplyNoSync();
                        break;
                    }
                    std::uint8_t eop = 0;
                    if (!ReadExact(&eop, 1) || eop != CRC_EOP)
                    {
                        ReplyNoSync();
                        break;
                    }
                    ReplyOk();
                    break;
                }
                case STK_ENTER_PROGMODE:
                {
                    std::uint8_t eop = 0;
                    if (!ReadExact(&eop, 1) || eop != CRC_EOP)
                    {
                        ReplyNoSync();
                        break;
                    }
                    inProgMode = true;
                    addressWords = 0;
                    {
                        std::lock_guard<std::mutex> guard(*boardsMutex);
                        auto &state = getBoard(boardId, boardProfile);
                        std::string error;
                        state.hasFirmware = false;
                        state.mcu->EraseFlash(error);
                        state.mcu->SoftReset();
                    }
                    ReplyOk();
                    break;
                }
                case STK_LEAVE_PROGMODE:
                {
                    std::uint8_t eop = 0;
                    if (!ReadExact(&eop, 1) || eop != CRC_EOP)
                    {
                        ReplyNoSync();
                        break;
                    }
                    inProgMode = false;
                    {
                        std::lock_guard<std::mutex> guard(*boardsMutex);
                        auto &state = getBoard(boardId, boardProfile);
                        state.mcu->SoftReset();
                        state.hasFirmware = true;
                    }
                    ReplyOk();
                    break;
                }
                case STK_LOAD_ADDRESS:
                {
                    std::uint8_t addrLo = 0;
                    std::uint8_t addrHi = 0;
                    std::uint8_t eop = 0;
                    if (!ReadExact(&addrLo, 1) || !ReadExact(&addrHi, 1) || !ReadExact(&eop, 1) || eop != CRC_EOP)
                    {
                        ReplyNoSync();
                        break;
                    }
                    addressWords = static_cast<std::uint32_t>(addrLo) | (static_cast<std::uint32_t>(addrHi) << 8);
                    ReplyOk();
                    break;
                }
                case STK_PROG_PAGE:
                {
                    std::uint8_t lenHi = 0;
                    std::uint8_t lenLo = 0;
                    std::uint8_t memType = 0;
                    if (!ReadExact(&lenHi, 1) || !ReadExact(&lenLo, 1) || !ReadExact(&memType, 1))
                    {
                        ReplyNoSync();
                        break;
                    }
                    const std::uint16_t len = static_cast<std::uint16_t>((lenHi << 8) | lenLo);
                    std::vector<std::uint8_t> data(len);
                    if (len > 0 && !ReadExact(data.data(), data.size()))
                    {
                        ReplyNoSync();
                        break;
                    }
                    std::uint8_t eop = 0;
                    if (!ReadExact(&eop, 1) || eop != CRC_EOP)
                    {
                        ReplyNoSync();
                        break;
                    }

                    if (!inProgMode)
                    {
                        ReplyFail();
                        break;
                    }

                    if (memType == static_cast<std::uint8_t>('F'))
                    {
                        std::lock_guard<std::mutex> guard(*boardsMutex);
                        auto &state = getBoard(boardId, boardProfile);
                        std::string error;
                        const std::uint32_t byteAddress = addressWords * 2u;
                        if (!state.mcu->ProgramFlash(byteAddress, data.data(), data.size(), error))
                        {
                            ReplyFail();
                            break;
                        }
                        ReplyOk();
                        // Address auto-increment is handled by the host tool; keep ours stable.
                        break;
                    }

                    // EEPROM not currently supported.
                    ReplyOk();
                    break;
                }
                case STK_READ_PAGE:
                {
                    std::uint8_t lenHi = 0;
                    std::uint8_t lenLo = 0;
                    std::uint8_t memType = 0;
                    if (!ReadExact(&lenHi, 1) || !ReadExact(&lenLo, 1) || !ReadExact(&memType, 1))
                    {
                        ReplyNoSync();
                        break;
                    }
                    const std::uint16_t len = static_cast<std::uint16_t>((lenHi << 8) | lenLo);
                    std::uint8_t eop = 0;
                    if (!ReadExact(&eop, 1) || eop != CRC_EOP)
                    {
                        ReplyNoSync();
                        break;
                    }

                    if (memType != static_cast<std::uint8_t>('F'))
                    {
                        std::vector<std::uint8_t> zeros(len, 0);
                        ReplyData(zeros.data(), zeros.size());
                        break;
                    }

                    std::vector<std::uint8_t> out(len, 0);
                    {
                        std::lock_guard<std::mutex> guard(*boardsMutex);
                        auto &state = getBoard(boardId, boardProfile);
                        std::string error;
                        const std::uint32_t byteAddress = addressWords * 2u;
                        if (!state.mcu->ReadFlash(byteAddress, out.data(), out.size(), error))
                        {
                            std::fill(out.begin(), out.end(), 0);
                        }
                    }
                    ReplyData(out.data(), out.size());
                    break;
                }
                case STK_READ_SIGN:
                {
                    std::uint8_t eop = 0;
                    if (!ReadExact(&eop, 1) || eop != CRC_EOP)
                    {
                        ReplyNoSync();
                        break;
                    }
                    std::uint8_t sig[3] = {0x1E, 0x95, 0x0F};
                    {
                        std::lock_guard<std::mutex> guard(*boardsMutex);
                        auto &state = getBoard(boardId, boardProfile);
                        if (state.profile.mcu == "ATmega2560")
                        {
                            sig[0] = 0x1E;
                            sig[1] = 0x98;
                            sig[2] = 0x01;
                        }
                    }
                    ReplyData(sig, sizeof(sig));
                    break;
                }
                default:
                {
                    // Drain until EOP when possible.
                    std::uint8_t b = 0;
                    for (int i = 0; i < 64; ++i)
                    {
                        if (!ReadExact(&b, 1))
                        {
                            break;
                        }
                        if (b == CRC_EOP)
                        {
                            break;
                        }
                    }
                    ReplyNoSync();
                    break;
                }
                }
            }
        }
    };

    IdeBridge ide;
    if (!ideComPort.empty())
    {
        // Ensure the board exists up-front so IDE can upload without Unity.
        {
            std::lock_guard<std::mutex> guard(boardsMutex);
            (void)GetBoardState(ideBoardId, ideBoardProfile);
        }

        auto logFn = [&](const char *msg)
        { Log("%s", msg); };
        if (!ide.Start(ideComPort, ideBoardId, ideBoardProfile, logFn, GetBoardState, &boardsMutex))
        {
            Log("[IDE] Failed to start IDE bridge.");
        }
    }

    while (true)
    {
        if (rpiBackend.Enabled())
        {
            rpiBackend.Update(QueryNowSeconds());
        }

        PipeCommand cmd;
        while (pipe.PopCommand(cmd))
        {
            std::lock_guard<std::mutex> guard(boardsMutex);
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
            if (cmd.type == PipeCommand::Type::Patch)
            {
                auto &state = GetBoardState(cmd.boardId, cmd.boardProfile);
                std::string error;
                if (state.mcu->PatchMemory(cmd.memoryType, cmd.address, cmd.data.data(), cmd.data.size(), error))
                {
                    Log("Patched memory type=%u %zu bytes at 0x%08X for %s", static_cast<unsigned int>(cmd.memoryType),
                        cmd.data.size(),
                        static_cast<unsigned int>(cmd.address), state.id.c_str());
                    pipe.SendLog(state.id, LogLevel::Info, "Memory patch injected");
                    state.mcu->SaveEepromToFile(state.eepromPath);
                }
                else
                {
                    Log("Patch failed for %s: %s", state.id.c_str(), error.c_str());
                    pipe.SendError(state.id, 131, error);
                }
                continue;
            }
            if (cmd.type == PipeCommand::Type::SerialInput)
            {
                auto &state = GetBoardState(cmd.boardId, cmd.boardProfile);
                if (!state.supported)
                {
                    continue;
                }
                for (std::uint8_t value : cmd.data)
                {
                    state.mcu->QueueSerialInput(value);
                }
                continue;
            }
            if (cmd.type == PipeCommand::Type::Step)
            {
                auto &state = GetBoardState(cmd.boardId, cmd.boardProfile);

                // === U1 High Level Emulation Integration Removed ===
                /*
                // If the profile ID contains "U1" or specific tag, run HLE.
                bool isHle = (cmd.boardProfile.find("U1") != std::string::npos);

                if (isHle)
                {
                   ...
                   continue;
                }
                */
                // ==========================================

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

                std::uint8_t serialByte = 0;
                while (state.mcu->ConsumeSerialByte(serialByte))
                {
                    pipe.SendSerial(state.id, &serialByte, 1);
                }

                const auto &perf = state.mcu->GetPerfCounters();
                const auto tick = state.mcu->TickCount();
                OutputDebugState debug{};
                debug.flashBytes = static_cast<std::uint32_t>(state.profile.flash_bytes);
                debug.sramBytes = static_cast<std::uint32_t>(state.profile.sram_bytes);
                debug.eepromBytes = static_cast<std::uint32_t>(state.profile.eeprom_bytes);
                debug.ioBytes = static_cast<std::uint32_t>(state.profile.io_bytes);
                debug.cpuHz = static_cast<std::uint32_t>(state.profile.cpu_hz);
                debug.pc = static_cast<std::uint16_t>(state.mcu->GetPC());
                debug.sp = static_cast<std::uint16_t>(
                    static_cast<std::uint16_t>(state.mcu->GetIo(AVR_SPL)) |
                    (static_cast<std::uint16_t>(state.mcu->GetIo(AVR_SPH)) << 8));
                debug.sreg = state.mcu->GetIo(AVR_SREG);
                debug.stackHighWater = perf.stackHighWaterMark;
                debug.heapTopAddress = perf.heapTopAddress;
                debug.stackMinAddress = perf.stackMinAddress;
                debug.dataSegmentEnd = perf.dataSegmentEnd;
                debug.stackOverflows = perf.stackOverflows;
                debug.invalidMemoryAccesses = perf.invalidMemoryAccesses;
                debug.interruptCount = perf.interruptCount;
                debug.interruptLatencyMax = perf.interruptLatencyMax;
                debug.timingViolations = perf.timingViolations;
                debug.criticalSectionCycles = perf.criticalSectionCycles;
                debug.sleepCycles = perf.sleepCycles;
                debug.flashAccessCycles = perf.flashAccessCycles;
                debug.uartOverflows = perf.uartOverflows;
                debug.timerOverflows = perf.timerOverflows;
                debug.brownOutResets = perf.brownOutResets;
                debug.gpioStateChanges = perf.gpioStateChanges;
                debug.pwmCycles = perf.pwmCycles;
                debug.i2cTransactions = perf.i2cTransactions;
                debug.spiTransactions = perf.spiTransactions;
                const bool sent = pipe.SendOutputState(state.id, cmd.stepSequence, tick, state.lastOutputs, kPinCount,
                                                       perf.cycles, perf.adcSamples,
                                                       perf.uartTxBytes, perf.uartRxBytes,
                                                       perf.spiTransfers, perf.twiTransfers, perf.wdtResets,
                                                       debug);

                if (traceLockstep)
                {
                    Log("[Lockstep] OutputState tx board=%s seq=%llu tick=%llu pc=%04X tx=%llu ok=%d connected=%d", state.id.c_str(),
                        static_cast<unsigned long long>(cmd.stepSequence),
                        static_cast<unsigned long long>(tick),
                        state.mcu->GetPC(),
                        static_cast<unsigned long long>(perf.uartTxBytes[0]),
                        sent ? 1 : 0, pipe.IsConnected() ? 1 : 0);
                    if (!sent)
                    {
                        Log("[Lockstep] OutputState write error=%lu", static_cast<unsigned long>(pipe.LastWriteError()));
                    }
                }
                if (traceCpu)
                {
                    VirtualMcu::CpuTraceEvent evt{};
                    std::uint32_t sentTrace = 0;
                    while (sentTrace < traceCpuMax && state.mcu->PopCpuTrace(evt))
                    {
                        char line[160];
                        char mnemonic[12];
                        FormatOpcodeMnemonic(evt.opcode, mnemonic, sizeof(mnemonic));
                        std::snprintf(line, sizeof(line),
                                      "TRACE pc=0x%04X op=0x%04X mnem=%s sp=0x%04X sreg=0x%02X tick=%llu",
                                      evt.pc, evt.opcode, mnemonic, evt.sp, evt.sreg,
                                      static_cast<unsigned long long>(evt.tick));
                        pipe.SendLog(state.id, LogLevel::Info, line);
                        ++sentTrace;
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
            std::lock_guard<std::mutex> guard(boardsMutex);
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
            std::lock_guard<std::mutex> guard(boardsMutex);
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
                OutputDebugState debug{};
                debug.flashBytes = static_cast<std::uint32_t>(state.profile.flash_bytes);
                debug.sramBytes = static_cast<std::uint32_t>(state.profile.sram_bytes);
                debug.eepromBytes = static_cast<std::uint32_t>(state.profile.eeprom_bytes);
                debug.ioBytes = static_cast<std::uint32_t>(state.profile.io_bytes);
                debug.cpuHz = static_cast<std::uint32_t>(state.profile.cpu_hz);
                debug.pc = static_cast<std::uint16_t>(state.mcu->GetPC());
                debug.sp = static_cast<std::uint16_t>(
                    static_cast<std::uint16_t>(state.mcu->GetIo(AVR_SPL)) |
                    (static_cast<std::uint16_t>(state.mcu->GetIo(AVR_SPH)) << 8));
                debug.sreg = state.mcu->GetIo(AVR_SREG);
                debug.stackHighWater = perf.stackHighWaterMark;
                debug.heapTopAddress = perf.heapTopAddress;
                debug.stackMinAddress = perf.stackMinAddress;
                debug.dataSegmentEnd = perf.dataSegmentEnd;
                debug.stackOverflows = perf.stackOverflows;
                debug.invalidMemoryAccesses = perf.invalidMemoryAccesses;
                debug.interruptCount = perf.interruptCount;
                debug.interruptLatencyMax = perf.interruptLatencyMax;
                debug.timingViolations = perf.timingViolations;
                debug.criticalSectionCycles = perf.criticalSectionCycles;
                debug.sleepCycles = perf.sleepCycles;
                debug.flashAccessCycles = perf.flashAccessCycles;
                debug.uartOverflows = perf.uartOverflows;
                debug.timerOverflows = perf.timerOverflows;
                debug.brownOutResets = perf.brownOutResets;
                debug.gpioStateChanges = perf.gpioStateChanges;
                debug.pwmCycles = perf.pwmCycles;
                debug.i2cTransactions = perf.i2cTransactions;
                debug.spiTransactions = perf.spiTransactions;
                pipe.SendOutputState(state.id, 0, tick, state.lastOutputs, kPinCount,
                                     perf.cycles, perf.adcSamples,
                                     perf.uartTxBytes, perf.uartRxBytes,
                                     perf.spiTransfers, perf.twiTransfers, perf.wdtResets,
                                     debug);
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
