#pragma once

#include "QemuProcess.h"
#include "RpiShm.h"

#include <cstdint>
#include <functional>
#include <string>

namespace firmware::rpi
{
    struct RpiConfig
    {
        bool enabled = false;
        std::string qemu_path;
        std::string image_path;
        std::string shm_dir;
        std::string net_mode;
        int display_width = 320;
        int display_height = 200;
        int camera_width = 320;
        int camera_height = 200;
        std::uint64_t cpu_affinity_mask = 0;
        std::uint32_t cpu_priority_class = 0;
        std::uint32_t cpu_max_percent = 0;
        std::uint32_t thread_count = 0;
        std::string log_path;
    };

    class RpiBackend
    {
    public:
        bool Start(const RpiConfig &config, const std::function<void(const char *)> &logFn);
        void Stop();
        void Update(double nowSeconds);

        bool Enabled() const { return enabled_; }
        bool Available() const { return available_; }

    private:
        void SetStatus(RpiStatusCode code, const char *message, std::uint32_t detail = 0);
        void HandleQemuExit(std::uint32_t exitCode, double nowSeconds);
        void TickInputs();
        void TickDisplay(double nowSeconds);
        bool StartQemu();

        bool OpenChannel(RpiShmChannel &channel, const std::string &name, std::size_t payloadBytes);

        RpiConfig config_{};
        std::function<void(const char *)> log_;
        bool enabled_ = false;
        bool available_ = false;
        bool started_ = false;
        bool statusDirty_ = true;
        double nextStatusAt_ = 0.0;
        double nextDisplayAt_ = 0.0;
        double restartAt_ = 0.0;
        double restartDelay_ = 1.0;
        bool qemuConfigured_ = false;
        std::uint64_t lastCameraSeq_ = 0;
        std::uint64_t lastGpioSeq_ = 0;
        std::uint64_t lastImuSeq_ = 0;
        std::uint64_t lastTimeSeq_ = 0;
        std::uint64_t lastNetSeq_ = 0;

        RpiStatusCode statusCode_ = RpiStatusCode::Unavailable;
        std::string statusMessage_ = "disabled";

        RpiShmChannel display_;
        RpiShmChannel camera_;
        RpiShmChannel gpio_;
        RpiShmChannel imu_;
        RpiShmChannel time_;
        RpiShmChannel network_;
        RpiShmChannel status_;

        QemuProcess qemu_;
    };
}
