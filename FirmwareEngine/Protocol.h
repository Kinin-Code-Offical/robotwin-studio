#pragma once

#include <cstdint>
#include <cstddef>

namespace firmware
{
    constexpr std::uint32_t kProtocolMagic = 0x57465452; // "RTFW"
    constexpr std::uint16_t kProtocolMajor = 1;
    constexpr std::uint16_t kProtocolMinor = 3;
    constexpr std::uint32_t kFeatureTimestampMicros = 1u << 0;
    constexpr std::uint32_t kFeaturePerfCounters = 1u << 1;
    constexpr std::uint32_t kMaxPayloadBytes = 8 * 1024 * 1024;
    constexpr std::size_t kPinCount = 70;
    constexpr std::uint8_t kPinValueUnknown = 0xFF;
    constexpr std::size_t kAnalogCount = 16;
    constexpr std::size_t kBoardIdSize = 64;
    constexpr std::uint16_t kDebugBitCount = 768;
    constexpr std::size_t kDebugBitBytes = (kDebugBitCount + 7) / 8;

    constexpr std::uint16_t kDbgBitPc = 0;
    constexpr std::uint16_t kDbgBitSp = 16;
    constexpr std::uint16_t kDbgBitSreg = 32;
    constexpr std::uint16_t kDbgBitFlashBytes = 40;
    constexpr std::uint16_t kDbgBitSramBytes = 72;
    constexpr std::uint16_t kDbgBitEepromBytes = 104;
    constexpr std::uint16_t kDbgBitIoBytes = 136;
    constexpr std::uint16_t kDbgBitCpuHz = 168;
    constexpr std::uint16_t kDbgBitStackHighWater = 200;
    constexpr std::uint16_t kDbgBitHeapTop = 216;
    constexpr std::uint16_t kDbgBitStackMin = 232;
    constexpr std::uint16_t kDbgBitDataSegmentEnd = 248;
    constexpr std::uint16_t kDbgBitStackOverflows = 264;
    constexpr std::uint16_t kDbgBitInvalidMem = 296;
    constexpr std::uint16_t kDbgBitInterruptCount = 328;
    constexpr std::uint16_t kDbgBitInterruptLatencyMax = 360;
    constexpr std::uint16_t kDbgBitTimingViolations = 392;
    constexpr std::uint16_t kDbgBitCriticalSectionCycles = 424;
    constexpr std::uint16_t kDbgBitSleepCycles = 456;
    constexpr std::uint16_t kDbgBitFlashAccessCycles = 488;
    constexpr std::uint16_t kDbgBitUartOverflows = 520;
    constexpr std::uint16_t kDbgBitTimerOverflows = 552;
    constexpr std::uint16_t kDbgBitBrownOutResets = 584;
    constexpr std::uint16_t kDbgBitGpioStateChanges = 616;
    constexpr std::uint16_t kDbgBitPwmCycles = 648;
    constexpr std::uint16_t kDbgBitI2cTransactions = 680;
    constexpr std::uint16_t kDbgBitSpiTransactions = 712;

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
        Error = 9,
        MemoryPatch = 10
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
        std::uint32_t flash_bytes;
        std::uint32_t sram_bytes;
        std::uint32_t eeprom_bytes;
        std::uint32_t io_bytes;
        std::uint32_t cpu_hz;
    };

    struct StepPayload
    {
        char board_id[kBoardIdSize];
        std::uint64_t step_sequence;
        std::uint32_t delta_micros;
        std::uint8_t pins[kPinCount];
        std::uint16_t analog[kAnalogCount];
        std::uint64_t sent_micros;
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
        std::uint64_t timestamp_micros;
        std::uint32_t flash_bytes;
        std::uint32_t sram_bytes;
        std::uint32_t eeprom_bytes;
        std::uint32_t io_bytes;
        std::uint32_t cpu_hz;
        std::uint16_t pc;
        std::uint16_t sp;
        std::uint8_t sreg;
        std::uint8_t reserved0;
        std::uint16_t stack_high_water;
        std::uint16_t heap_top_address;
        std::uint16_t stack_min_address;
        std::uint16_t data_segment_end;
        std::uint64_t stack_overflows;
        std::uint64_t invalid_memory_accesses;
        std::uint64_t interrupt_count;
        std::uint64_t interrupt_latency_max;
        std::uint64_t timing_violations;
        std::uint64_t critical_section_cycles;
        std::uint64_t sleep_cycles;
        std::uint64_t flash_access_cycles;
        std::uint64_t uart_overflows;
        std::uint64_t timer_overflows;
        std::uint64_t brown_out_resets;
        std::uint64_t gpio_state_changes;
        std::uint64_t pwm_cycles;
        std::uint64_t i2c_transactions;
        std::uint64_t spi_transactions;
        std::uint16_t debug_bit_count;
        std::uint16_t reserved1;
        std::uint8_t debug_bits[kDebugBitBytes];
    };

    enum class MemoryType : std::uint8_t
    {
        Flash = 1,
        Sram = 2,
        Io = 3,
        Eeprom = 4
    };

    struct MemoryPatchHeader
    {
        char board_id[kBoardIdSize];
        std::uint8_t memory_type;
        std::uint8_t reserved[3];
        std::uint32_t address;
        std::uint32_t length;
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
