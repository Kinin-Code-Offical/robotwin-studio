#pragma once

#include <cstdint>
#include <cstddef>

namespace firmware
{
    constexpr std::uint32_t kProtocolMagic = 0x57465452; // "RTFW"
    constexpr std::uint16_t kProtocolMajor = 1;
    constexpr std::uint16_t kProtocolMinor = 0;
    constexpr std::size_t kPinCount = 70;
    constexpr std::uint8_t kPinValueUnknown = 0xFF;
    constexpr std::size_t kAnalogCount = 16;
    constexpr std::size_t kBoardIdSize = 64;

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
        std::uint32_t board_id_size;
        std::uint32_t analog_count;
    };

    struct HelloAckPayload
    {
        std::uint32_t flags;
        std::uint32_t pin_count;
        std::uint32_t board_id_size;
        std::uint32_t analog_count;
    };

    struct StepPayload
    {
        char board_id[kBoardIdSize];
        std::uint64_t step_sequence;
        std::uint32_t delta_micros;
        std::uint8_t pins[kPinCount];
        std::uint16_t analog[kAnalogCount];
    };

    struct LoadBvmHeader
    {
        char board_id[kBoardIdSize];
        char board_profile[kBoardIdSize];
    };

    struct OutputStatePayload
    {
        char board_id[kBoardIdSize];
        std::uint64_t step_sequence;
        std::uint64_t tick_count;
        std::uint8_t pins[kPinCount];
        std::uint64_t cycles;
        std::uint64_t adc_samples;
        std::uint64_t uart_tx_bytes[4];
        std::uint64_t uart_rx_bytes[4];
        std::uint64_t spi_transfers;
        std::uint64_t twi_transfers;
        std::uint64_t wdt_resets;
    };

    struct StatusPayload
    {
        char board_id[kBoardIdSize];
        std::uint64_t tick_count;
    };

    struct ErrorPayload
    {
        char board_id[kBoardIdSize];
        std::uint32_t code;
    };

    struct LogPayload
    {
        char board_id[kBoardIdSize];
        std::uint8_t level;
    };
#pragma pack(pop)
}
