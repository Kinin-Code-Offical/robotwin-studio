#pragma once
#include <cstdint>
#include <cstddef>

namespace bvm
{
    constexpr std::uint32_t kMagic = 0x43534E45; // "CSNE"
    constexpr std::uint16_t kVersionMajor = 1;
    constexpr std::uint16_t kVersionMinor = 0;

    enum SectionFlags : std::uint64_t
    {
        SectionRead = 1ull << 0,
        SectionWrite = 1ull << 1,
        SectionExec = 1ull << 2,
        SectionTextHex = 1ull << 3,
        SectionTextRaw = 1ull << 4
    };

    struct alignas(8) BvmHeader
    {
        std::uint32_t magic;
        std::uint16_t version_major;
        std::uint16_t version_minor;
        std::uint32_t header_size;
        std::uint32_t section_count;
        std::uint64_t entry_offset;
        std::uint64_t section_table_offset;
        std::uint64_t flags;
        std::uint64_t reserved0;
        std::uint64_t reserved1;
        std::uint64_t reserved2;
    };

    struct alignas(8) BvmSection
    {
        char name[8];
        std::uint64_t offset;
        std::uint64_t size;
        std::uint64_t flags;
        std::uint64_t reserved;
    };

    struct BvmView
    {
        const std::uint8_t* base = nullptr;
        std::size_t size = 0;
        const BvmHeader* header = nullptr;
        const BvmSection* sections = nullptr;
    };

    struct SectionView
    {
        const std::uint8_t* data = nullptr;
        std::uint64_t size = 0;
        std::uint64_t flags = 0;
    };

    bool Open(const std::uint8_t* buffer, std::size_t size, BvmView& view, const char** error);
    bool FindSection(const BvmView& view, const char* name, SectionView& section);
    bool IsAligned8(std::uint64_t value);
}
