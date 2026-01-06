#pragma once

#include "RpiShmProtocol.h"

#include <cstdint>
#include <string>

namespace firmware::rpi
{
    class RpiShmChannel
    {
    public:
        RpiShmChannel() = default;
        ~RpiShmChannel();

        bool Open(const std::string &path, std::size_t payloadBytes, bool createIfMissing);
        void Close();
        bool IsOpen() const { return view_ != nullptr; }

        bool Write(const void *payload, std::size_t payloadBytes, int width, int height, int stride, std::uint32_t flags = 0);
        bool Read(RpiShmHeader &header, const std::uint8_t *&payload) const;
        bool ReadIfNew(std::uint64_t &lastSequence, RpiShmHeader &header, const std::uint8_t *&payload) const;

        bool WriteStatus(RpiStatusCode status, const char *message, std::uint32_t detail = 0);

        std::size_t PayloadBytes() const { return payloadBytes_; }

    private:
        bool EnsureFileSize(std::uint64_t size);

        void *file_ = nullptr;
        void *mapping_ = nullptr;
        std::uint8_t *view_ = nullptr;
        RpiShmHeader *header_ = nullptr;
        std::uint8_t *payload_ = nullptr;
        std::size_t payloadBytes_ = 0;
        std::uint64_t sequence_ = 0;
        std::string path_;
    };
}
