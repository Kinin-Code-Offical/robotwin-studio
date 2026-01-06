#include "QemuProcess.h"

#include <windows.h>

namespace firmware::rpi
{
    namespace
    {
        HANDLE ToHandle(void *value)
        {
            return reinterpret_cast<HANDLE>(value);
        }
    }

    QemuProcess::~QemuProcess()
    {
        Stop();
    }

    bool QemuProcess::Start(const std::wstring &exePath, const std::wstring &args, const std::wstring &workingDir,
                            const std::wstring &logPath)
    {
        Stop();

        HANDLE logHandle = INVALID_HANDLE_VALUE;
        if (!logPath.empty())
        {
            logHandle = CreateFileW(logPath.c_str(), FILE_APPEND_DATA,
                                    FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS,
                                    FILE_ATTRIBUTE_NORMAL, nullptr);
            if (logHandle != INVALID_HANDLE_VALUE)
            {
                SetFilePointer(logHandle, 0, nullptr, FILE_END);
            }
        }

        STARTUPINFOW startInfo{};
        startInfo.cb = sizeof(startInfo);
        if (logHandle != INVALID_HANDLE_VALUE)
        {
            startInfo.dwFlags |= STARTF_USESTDHANDLES;
            startInfo.hStdOutput = logHandle;
            startInfo.hStdError = logHandle;
        }

        PROCESS_INFORMATION procInfo{};
        std::wstring commandLine = L"\"" + exePath + L"\" " + args;
        wchar_t *mutableCmd = commandLine.empty() ? nullptr : commandLine.data();

        BOOL ok = CreateProcessW(
            exePath.c_str(),
            mutableCmd,
            nullptr,
            nullptr,
            logHandle != INVALID_HANDLE_VALUE,
            CREATE_NO_WINDOW,
            nullptr,
            workingDir.empty() ? nullptr : workingDir.c_str(),
            &startInfo,
            &procInfo);

        if (logHandle != INVALID_HANDLE_VALUE)
        {
            CloseHandle(logHandle);
        }

        if (!ok)
        {
            return false;
        }

        process_ = procInfo.hProcess;
        thread_ = procInfo.hThread;
        pid_ = procInfo.dwProcessId;
        return true;
    }

    void QemuProcess::Stop()
    {
        if (process_)
        {
            HANDLE handle = ToHandle(process_);
            DWORD exitCode = 0;
            if (GetExitCodeProcess(handle, &exitCode) && exitCode == STILL_ACTIVE)
            {
                TerminateProcess(handle, 1);
                WaitForSingleObject(handle, 2000);
            }
        }
        CloseHandles();
    }

    bool QemuProcess::IsRunning() const
    {
        if (!process_) return false;
        DWORD exitCode = 0;
        if (!GetExitCodeProcess(ToHandle(process_), &exitCode)) return false;
        return exitCode == STILL_ACTIVE;
    }

    bool QemuProcess::HasExited(std::uint32_t &exitCode) const
    {
        exitCode = 0;
        if (!process_) return true;
        DWORD code = 0;
        if (!GetExitCodeProcess(ToHandle(process_), &code)) return true;
        exitCode = static_cast<std::uint32_t>(code);
        return code != STILL_ACTIVE;
    }

    bool QemuProcess::ApplyAffinity(std::uint64_t mask)
    {
        if (!process_ || mask == 0)
        {
            return false;
        }
        return SetProcessAffinityMask(ToHandle(process_), static_cast<DWORD_PTR>(mask)) != 0;
    }

    bool QemuProcess::ApplyPriority(std::uint32_t priorityClass)
    {
        if (!process_ || priorityClass == 0)
        {
            return false;
        }
        return SetPriorityClass(ToHandle(process_), priorityClass) != 0;
    }

    bool QemuProcess::ApplyCpuLimit(std::uint32_t percent)
    {
        if (!process_ || percent == 0 || percent > 100)
        {
            return false;
        }
        HANDLE job = CreateJobObjectW(nullptr, nullptr);
        if (!job)
        {
            return false;
        }
        JOBOBJECT_CPU_RATE_CONTROL_INFORMATION info{};
        info.ControlFlags = JOB_OBJECT_CPU_RATE_CONTROL_ENABLE | JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP;
        info.CpuRate = static_cast<DWORD>(percent * 100);
        bool ok = SetInformationJobObject(job, JobObjectCpuRateControlInformation, &info, sizeof(info)) != 0;
        if (ok)
        {
            ok = AssignProcessToJobObject(job, ToHandle(process_)) != 0;
        }
        CloseHandle(job);
        return ok;
    }

    void QemuProcess::CloseHandles()
    {
        if (thread_)
        {
            CloseHandle(ToHandle(thread_));
        }
        if (process_)
        {
            CloseHandle(ToHandle(process_));
        }
        thread_ = nullptr;
        process_ = nullptr;
        pid_ = 0;
    }
}
