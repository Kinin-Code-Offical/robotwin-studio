#include "RpiBackend.h"

#include <algorithm>
#include <filesystem>
#include <sstream>
#include <vector>
#include <windows.h>

#ifdef _WIN32
#undef min
#undef max
#endif

namespace firmware::rpi
{
    namespace
    {
        std::wstring ToWide(const std::string &value)
        {
            if (value.empty())
            {
                return {};
            }
            int len = MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, nullptr, 0);
            if (len <= 0)
            {
                return {};
            }
            std::wstring out;
            out.resize(static_cast<std::size_t>(len - 1));
            MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, out.data(), len);
            return out;
        }

        std::string JoinPath(const std::string &root, const std::string &file)
        {
            std::filesystem::path base(root);
            return (base / file).string();
        }

        std::string BuildMessage(const std::string &prefix, const std::string &detail)
        {
            if (detail.empty())
                return prefix;
            return prefix + ": " + detail;
        }

        std::string BuildMockPattern(int width, int height, int tick)
        {
            std::string buffer;
            buffer.resize(static_cast<std::size_t>(width * height * 4), '\0');
            for (int y = 0; y < height; ++y)
            {
                int row = y * width * 4;
                for (int x = 0; x < width; ++x)
                {
                    int idx = row + x * 4;
                    buffer[idx] = static_cast<char>((x + tick) & 0xFF);
                    buffer[idx + 1] = static_cast<char>((y + tick * 2) & 0xFF);
                    buffer[idx + 2] = static_cast<char>((x + y + tick * 3) & 0xFF);
                    buffer[idx + 3] = static_cast<char>(0xFF);
                }
            }
            return buffer;
        }
    }

    bool RpiBackend::Start(const RpiConfig &config, const std::function<void(const char *)> &logFn)
    {
        Stop();
        config_ = config;
        log_ = logFn;
        enabled_ = config.enabled;
        if (!enabled_)
        {
            return false;
        }

        std::string shmDir = config_.shm_dir.empty() ? "logs/rpi/shm" : config_.shm_dir;
        config_.shm_dir = shmDir;
        std::filesystem::create_directories(shmDir);
        if (config_.log_path.empty())
        {
            std::filesystem::path logDir("logs/rpi");
            std::filesystem::create_directories(logDir);
            config_.log_path = (logDir / "rpi_qemu.log").string();
        }

        if (!OpenChannel(display_, "rpi_display.shm", static_cast<std::size_t>(config_.display_width * config_.display_height * 4)) ||
            !OpenChannel(camera_, "rpi_camera.shm", static_cast<std::size_t>(config_.camera_width * config_.camera_height * 4)) ||
            !OpenChannel(gpio_, "rpi_gpio.shm", kRpiGpioPayloadBytes) ||
            !OpenChannel(imu_, "rpi_imu.shm", kRpiImuPayloadBytes) ||
            !OpenChannel(time_, "rpi_time.shm", kRpiTimePayloadBytes) ||
            !OpenChannel(network_, "rpi_net.shm", kRpiNetworkPayloadBytes) ||
            !OpenChannel(status_, "rpi_status.shm", kRpiStatusPayloadBytes))
        {
            SetStatus(RpiStatusCode::ShmError, "shared memory init failed");
            enabled_ = false;
            return false;
        }

        StartQemu();
        started_ = true;
        return true;
    }

    void RpiBackend::Stop()
    {
        enabled_ = false;
        available_ = false;
        started_ = false;
        qemu_.Stop();
        display_.Close();
        camera_.Close();
        gpio_.Close();
        imu_.Close();
        time_.Close();
        network_.Close();
        status_.Close();
        statusCode_ = RpiStatusCode::Unavailable;
        statusMessage_.assign("stopped");
    }

    void RpiBackend::SetStatus(RpiStatusCode code, const char *message, std::uint32_t detail)
    {
        statusCode_ = code;
        statusMessage_ = message ? message : "";
        status_.WriteStatus(code, statusMessage_.c_str(), detail);
        statusDirty_ = false;
    }

    bool RpiBackend::OpenChannel(RpiShmChannel &channel, const std::string &name, std::size_t payloadBytes)
    {
        std::string path = JoinPath(config_.shm_dir, name);
        return channel.Open(path, payloadBytes, true);
    }

    bool RpiBackend::StartQemu()
    {
        bool hasQemu = !config_.qemu_path.empty() && std::filesystem::exists(config_.qemu_path);
        qemuConfigured_ = false;
        if (!hasQemu && !config_.allow_mock)
        {
            SetStatus(RpiStatusCode::QemuMissing, "qemu missing");
            available_ = false;
            return false;
        }

        if (!hasQemu && config_.allow_mock)
        {
            available_ = true;
            SetStatus(RpiStatusCode::Ok, "mock display");
            return true;
        }

        if (config_.image_path.empty() || !std::filesystem::exists(config_.image_path))
        {
            SetStatus(RpiStatusCode::ImageMissing, "image missing");
            available_ = false;
            return false;
        }
        qemuConfigured_ = true;

        std::ostringstream args;
        args << "-display none";
        args << " -drive file=" << config_.image_path << ",format=raw";
        if (!config_.net_mode.empty())
        {
            if (config_.net_mode == "down")
            {
                args << " -nic none";
            }
            else
            {
                args << " -nic user";
            }
        }

        std::wstring exeW = ToWide(config_.qemu_path);
        std::wstring argsW = ToWide(args.str());
        std::wstring dirW = ToWide(std::filesystem::absolute(".").string());
        std::wstring logW = ToWide(config_.log_path);
        if (!qemu_.Start(exeW, argsW, dirW, logW))
        {
            SetStatus(RpiStatusCode::QemuFailed, "qemu start failed");
            available_ = false;
            return false;
        }
        available_ = true;
        if (config_.cpu_affinity_mask != 0)
        {
            qemu_.ApplyAffinity(config_.cpu_affinity_mask);
        }
        if (config_.cpu_priority_class != 0)
        {
            qemu_.ApplyPriority(config_.cpu_priority_class);
        }
        if (config_.cpu_max_percent > 0 && config_.cpu_max_percent <= 100)
        {
            qemu_.ApplyCpuLimit(config_.cpu_max_percent);
        }
        SetStatus(RpiStatusCode::Ok, "qemu running");
        return true;
    }

    void RpiBackend::HandleQemuExit(std::uint32_t exitCode, double nowSeconds)
    {
        available_ = false;
        std::ostringstream msg;
        msg << "qemu exited (" << exitCode << ")";
        SetStatus(RpiStatusCode::QemuFailed, msg.str().c_str());
        if (log_)
        {
            log_(msg.str().c_str());
        }
        restartDelay_ = std::min(restartDelay_ * 2.0, 5.0);
        restartAt_ = nowSeconds + restartDelay_;
    }

    void RpiBackend::TickInputs()
    {
        RpiShmHeader header{};
        const std::uint8_t *payload = nullptr;

        if (camera_.ReadIfNew(lastCameraSeq_, header, payload))
        {
            if (config_.allow_mock && log_)
                log_("[RPI] Camera input");
        }
        if (gpio_.ReadIfNew(lastGpioSeq_, header, payload))
        {
            if (config_.allow_mock && log_)
                log_("[RPI] GPIO update");
        }
        if (imu_.ReadIfNew(lastImuSeq_, header, payload))
        {
            if (config_.allow_mock && log_)
                log_("[RPI] IMU update");
        }
        if (time_.ReadIfNew(lastTimeSeq_, header, payload))
        {
            if (config_.allow_mock && log_)
                log_("[RPI] Time sync");
        }
        if (network_.ReadIfNew(lastNetSeq_, header, payload))
        {
            if (config_.allow_mock && log_)
                log_("[RPI] Network update");
        }
    }

    void RpiBackend::TickDisplay(double nowSeconds)
    {
        if (!config_.allow_mock)
        {
            return;
        }
        if (nowSeconds < nextDisplayAt_)
        {
            return;
        }
        nextDisplayAt_ = nowSeconds + 0.1;
        static int tick = 0;
        std::string frame = BuildMockPattern(config_.display_width, config_.display_height, tick++);
        display_.Write(frame.data(), frame.size(), config_.display_width, config_.display_height, config_.display_width * 4);
    }

    void RpiBackend::Update(double nowSeconds)
    {
        if (!enabled_ || !started_)
        {
            return;
        }

        if (qemu_.IsRunning())
        {
            std::uint32_t exitCode = 0;
            if (qemu_.HasExited(exitCode))
            {
                HandleQemuExit(exitCode, nowSeconds);
            }
        }
        else if (!config_.allow_mock && qemuConfigured_)
        {
            if (restartAt_ > 0.0 && nowSeconds >= restartAt_)
            {
                restartAt_ = 0.0;
                StartQemu();
            }
        }

        TickInputs();
        TickDisplay(nowSeconds);

        if (statusDirty_ || nowSeconds >= nextStatusAt_)
        {
            status_.WriteStatus(statusCode_, statusMessage_.c_str());
            nextStatusAt_ = nowSeconds + 1.0;
            statusDirty_ = false;
        }
    }
}
