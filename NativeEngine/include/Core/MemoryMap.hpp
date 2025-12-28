#pragma once
#include <array>
#include <cstddef>
#include <cstdint>

#if defined(__cpp_lib_span) && __cpp_lib_span >= 202002L
#include <span>
#endif

namespace core
{
    constexpr std::size_t FLASH_SIZE = 32 * 1024;
    constexpr std::size_t SRAM_SIZE = 2 * 1024;
    constexpr std::size_t EEPROM_SIZE = 1024;
    constexpr std::size_t IO_SIZE = 0x80;
    constexpr std::size_t REG_SIZE = 32;

    template <typename T>
#if defined(__cpp_lib_span) && __cpp_lib_span >= 202002L
    using Span = std::span<T>;
#else
    class Span
    {
    public:
        Span(T* data, std::size_t size) : _data(data), _size(size) {}
        T* data() { return _data; }
        const T* data() const { return _data; }
        std::size_t size() const { return _size; }
        T& operator[](std::size_t idx) { return _data[idx]; }
        const T& operator[](std::size_t idx) const { return _data[idx]; }

    private:
        T* _data;
        std::size_t _size;
    };
#endif

    struct alignas(64) VirtualMemory
    {
        alignas(64) std::array<std::uint8_t, FLASH_SIZE> flash{};
        alignas(64) std::array<std::uint8_t, SRAM_SIZE> sram{};
        alignas(64) std::array<std::uint8_t, EEPROM_SIZE> eeprom{};
        alignas(64) volatile std::uint8_t io[IO_SIZE]{};
        alignas(64) std::array<std::uint8_t, REG_SIZE> regs{};

        Span<std::uint8_t> Flash() { return Span<std::uint8_t>(flash.data(), flash.size()); }
        Span<std::uint8_t> Sram() { return Span<std::uint8_t>(sram.data(), sram.size()); }
        Span<std::uint8_t> Eeprom() { return Span<std::uint8_t>(eeprom.data(), eeprom.size()); }
        Span<volatile std::uint8_t> Io() { return Span<volatile std::uint8_t>(io, IO_SIZE); }
        Span<std::uint8_t> Regs() { return Span<std::uint8_t>(regs.data(), regs.size()); }
    };
}
