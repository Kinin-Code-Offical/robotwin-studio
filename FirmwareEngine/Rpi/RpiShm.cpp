#include "RpiShm.h"

#include <algorithm>
#include <cstring>
#include <filesystem>
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

    RpiShmChannel::~RpiShmChannel()
    {
        Close();
    }

    bool RpiShmChannel::EnsureFileSize(std::uint64_t size)
    {
        HANDLE fileHandle = ToHandle(file_);
        LARGE_INTEGER target{};
        target.QuadPart = static_cast<LONGLONG>(size);
        if (!SetFilePointerEx(fileHandle, target, nullptr, FILE_BEGIN))
        {
            return false;
        }
        return SetEndOfFile(fileHandle) != 0;
    }

    bool RpiShmChannel::Open(const std::string &path, std::size_t payloadBytes, bool createIfMissing)
    {
        Close();
        if (payloadBytes == 0)
        {
            return false;
        }
        path_ = path;
        payloadBytes_ = payloadBytes;
        std::uint64_t totalBytes = kRpiShmHeaderSize + payloadBytes_;

        std::filesystem::path shmPath(path);
        std::filesystem::create_directories(shmPath.parent_path());

        DWORD desiredAccess = GENERIC_READ | GENERIC_WRITE;
        DWORD shareMode = FILE_SHARE_READ | FILE_SHARE_WRITE;
        DWORD creation = createIfMissing ? OPEN_ALWAYS : OPEN_EXISTING;

        HANDLE fileHandle = CreateFileA(path.c_str(), desiredAccess, shareMode, nullptr, creation, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (fileHandle == INVALID_HANDLE_VALUE)
        {
            return false;
        }
        file_ = fileHandle;

        LARGE_INTEGER size{};
        if (!GetFileSizeEx(fileHandle, &size))
        {
            Close();
            return false;
        }
        if (static_cast<std::uint64_t>(size.QuadPart) < totalBytes)
        {
            if (!EnsureFileSize(totalBytes))
            {
                Close();
                return false;
            }
        }

        HANDLE mappingHandle = CreateFileMappingA(fileHandle, nullptr, PAGE_READWRITE, 0, 0, nullptr);
        if (!mappingHandle)
        {
            Close();
            return false;
        }
        mapping_ = mappingHandle;

        void *view = MapViewOfFile(mappingHandle, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, 0);
        if (!view)
        {
            Close();
            return false;
        }
        view_ = reinterpret_cast<std::uint8_t *>(view);
        header_ = reinterpret_cast<RpiShmHeader *>(view_);
        payload_ = view_ + kRpiShmHeaderSize;
        return true;
    }

    void RpiShmChannel::Close()
    {
        if (view_)
        {
            UnmapViewOfFile(view_);
        }
        view_ = nullptr;
        header_ = nullptr;
        payload_ = nullptr;
        if (mapping_)
        {
            CloseHandle(ToHandle(mapping_));
        }
        mapping_ = nullptr;
        if (file_)
        {
            CloseHandle(ToHandle(file_));
        }
        file_ = nullptr;
        payloadBytes_ = 0;
        sequence_ = 0;
        path_.clear();
    }

    bool RpiShmChannel::Write(const void *payload, std::size_t payloadBytes, int width, int height, int stride, std::uint32_t flags)
    {
        if (!view_ || !payload_ || !header_ || payloadBytes_ == 0)
        {
            return false;
        }
        std::size_t bytes = std::min(payloadBytes, payloadBytes_);
        if (payload && bytes > 0)
        {
            std::memcpy(payload_, payload, bytes);
        }
        if (bytes < payloadBytes_)
        {
            std::memset(payload_ + bytes, 0, payloadBytes_ - bytes);
        }

        header_->magic = kRpiShmMagic;
        header_->version = kRpiShmVersion;
        header_->header_size = kRpiShmHeaderSize;
        header_->width = width;
        header_->height = height;
        header_->stride = stride;
        header_->payload_bytes = static_cast<std::int32_t>(payloadBytes_);
        header_->sequence = ++sequence_;
        header_->timestamp_us = static_cast<std::uint64_t>(GetTickCount64() * 1000ull);
        header_->flags = flags;
        std::memset(header_->reserved, 0, sizeof(header_->reserved));
        return true;
    }

    bool RpiShmChannel::Read(RpiShmHeader &header, const std::uint8_t *&payload) const
    {
        if (!header_ || !payload_)
        {
            return false;
        }
        std::memcpy(&header, header_, sizeof(RpiShmHeader));
        if (header.magic != kRpiShmMagic || header.payload_bytes <= 0)
        {
            return false;
        }
        if (static_cast<std::size_t>(header.payload_bytes) > payloadBytes_)
        {
            return false;
        }
        payload = payload_;
        return true;
    }

    bool RpiShmChannel::ReadIfNew(std::uint64_t &lastSequence, RpiShmHeader &header, const std::uint8_t *&payload) const
    {
        if (!Read(header, payload))
        {
            return false;
        }
        if (header.sequence <= lastSequence)
        {
            return false;
        }
        lastSequence = header.sequence;
        return true;
    }

    bool RpiShmChannel::WriteStatus(RpiStatusCode status, const char *message, std::uint32_t detail)
    {
        RpiStatusPayload payload{};
        payload.status = static_cast<std::uint32_t>(status);
        payload.detail = detail;
        if (message != nullptr)
        {
            std::strncpy(payload.message, message, sizeof(payload.message) - 1);
        }
        return Write(&payload, sizeof(payload), 0, 0, 0, 0);
    }
}
