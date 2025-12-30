#pragma once

#include <cstdint>
#include <cstddef>

namespace firmware
{
    constexpr std::uint32_t kProtocolMagic = 0x57465452; // "RTFW"
    constexpr std::uint16_t kProtocolMajor = 1;
    constexpr std::uint16_t kProtocolMinor = 0;
    constexpr std::size_t kPinCount = 20;

    enum class MessageType : std::uint16_t
    {
        Hello = 1,
        HelloAck = 2,
        LoadBvm = 3,
        Step = 4,
        OutputState = 5,
        Serial = 6,
        Status = 7,
        Log = 8,
        Error = 9
    };

    enum class LogLevel : std::uint8_t
    {
        Info = 1,
        Warning = 2,
        Error = 3
    };

#pragma pack(push, 1)
    struct PacketHeader
    {
        std::uint32_t magic;
        std::uint16_t version_major;
        std::uint16_t version_minor;
        std::uint16_t type;
        std::uint16_t flags;
        std::uint32_t payload_size;
        std::uint32_t sequence;
    };

    struct HelloPayload
    {
        std::uint32_t flags;
        std::uint32_t pin_count;
    };

    struct HelloAckPayload
    {
        std::uint32_t flags;
        std::uint32_t pin_count;
    };

    struct StepPayload
    {
        std::uint32_t delta_micros;
        std::uint8_t pins[kPinCount];
    };

    struct OutputStatePayload
    {
        std::uint64_t tick_count;
        std::uint8_t pins[kPinCount];
    };

    struct StatusPayload
    {
        std::uint64_t tick_count;
    };

    struct ErrorPayload
    {
        std::uint32_t code;
    };

    struct LogPayload
    {
        std::uint8_t level;
    };
#pragma pack(pop)
}
