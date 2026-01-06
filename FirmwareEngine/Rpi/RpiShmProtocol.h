#pragma once

#include <cstdint>
#include <cstddef>

namespace firmware::rpi
{
    constexpr std::uint32_t kRpiShmMagic = 0x4D495052; // "RPIM"
    constexpr std::uint16_t kRpiShmVersion = 1;
    constexpr std::uint16_t kRpiShmHeaderSize = 64;

    constexpr std::uint32_t kRpiFlagUnavailable = 1u << 0;
    constexpr std::uint32_t kRpiFlagError = 1u << 1;

#pragma pack(push, 1)
    struct RpiShmHeader
    {
        std::uint32_t magic;
        std::uint16_t version;
        std::uint16_t header_size;
        std::int32_t width;
        std::int32_t height;
        std::int32_t stride;
        std::int32_t payload_bytes;
        std::uint64_t sequence;
        std::uint64_t timestamp_us;
        std::uint32_t flags;
        std::uint8_t reserved[20];
    };

    struct RpiGpioEntry
    {
        std::int32_t pin;
        std::int32_t value;
    };

    struct RpiGpioPayload
    {
        std::uint32_t count;
        RpiGpioEntry entries[32];
    };

    struct RpiImuPayload
    {
        float ax;
        float ay;
        float az;
        float gx;
        float gy;
        float gz;
        float mx;
        float my;
        float mz;
        float padding[7];
    };

    struct RpiTimePayload
    {
        double sim_seconds;
        std::int64_t utc_ticks;
    };

    struct RpiNetworkPayload
    {
        std::uint32_t mode;
        std::uint8_t reserved[12];
    };

    enum class RpiStatusCode : std::uint32_t
    {
        Ok = 0,
        Unavailable = 1,
        QemuMissing = 2,
        ImageMissing = 3,
        ShmError = 4,
        QemuFailed = 5
    };

    struct RpiStatusPayload
    {
        std::uint32_t status;
        std::uint32_t detail;
        char message[248];
    };
#pragma pack(pop)

    constexpr std::size_t kRpiStatusPayloadBytes = sizeof(RpiStatusPayload);
    constexpr std::size_t kRpiGpioPayloadBytes = sizeof(RpiGpioPayload);
    constexpr std::size_t kRpiImuPayloadBytes = sizeof(RpiImuPayload);
    constexpr std::size_t kRpiTimePayloadBytes = sizeof(RpiTimePayload);
    constexpr std::size_t kRpiNetworkPayloadBytes = sizeof(RpiNetworkPayload);

    static_assert(sizeof(RpiShmHeader) == kRpiShmHeaderSize, "RpiShmHeader size mismatch");
    static_assert(sizeof(RpiGpioPayload) == 260, "RpiGpioPayload size mismatch");
    static_assert(sizeof(RpiImuPayload) == 64, "RpiImuPayload size mismatch");
    static_assert(sizeof(RpiTimePayload) == 16, "RpiTimePayload size mismatch");
    static_assert(sizeof(RpiNetworkPayload) == 16, "RpiNetworkPayload size mismatch");
    static_assert(sizeof(RpiStatusPayload) == 256, "RpiStatusPayload size mismatch");
}
