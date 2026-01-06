#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace firmware::rpi
{
    class QemuProcess
    {
    public:
        QemuProcess() = default;
        ~QemuProcess();

        bool Start(const std::wstring &exePath, const std::wstring &args, const std::wstring &workingDir,
                   const std::wstring &logPath);
        void Stop();
        bool IsRunning() const;
        bool HasExited(std::uint32_t &exitCode) const;
        std::uint32_t Pid() const { return pid_; }

        bool ApplyAffinity(std::uint64_t mask);
        bool ApplyPriority(std::uint32_t priorityClass);
        bool ApplyCpuLimit(std::uint32_t percent);

    private:
        void CloseHandles();

        void *process_ = nullptr;
        void *thread_ = nullptr;
        void *logHandle_ = nullptr;
        std::uint32_t pid_ = 0;
    };
}
