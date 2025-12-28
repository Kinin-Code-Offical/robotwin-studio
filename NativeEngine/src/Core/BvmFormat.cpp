#include "Core/BvmFormat.hpp"
#include <cstring>

namespace bvm
{
    static bool NameMatches(const char* a, const char* b)
    {
        return std::strncmp(a, b, 8) == 0;
    }

    bool IsAligned8(std::uint64_t value)
    {
        return (value & 0x7ull) == 0;
    }

    bool Open(const std::uint8_t* buffer, std::size_t size, BvmView& view, const char** error)
    {
        if (error) *error = nullptr;
        if (!buffer || size < sizeof(BvmHeader))
        {
            if (error) *error = "Buffer too small";
            return false;
        }

        auto header = reinterpret_cast<const BvmHeader*>(buffer);
        if (header->magic != kMagic)
        {
            if (error) *error = "Invalid magic";
            return false;
        }
        if (header->header_size < sizeof(BvmHeader))
        {
            if (error) *error = "Invalid header size";
            return false;
        }
        if (!IsAligned8(header->section_table_offset))
        {
            if (error) *error = "Section table misaligned";
            return false;
        }
        if (header->section_table_offset + header->section_count * sizeof(BvmSection) > size)
        {
            if (error) *error = "Section table out of bounds";
            return false;
        }

        view.base = buffer;
        view.size = size;
        view.header = header;
        view.sections = reinterpret_cast<const BvmSection*>(buffer + header->section_table_offset);
        return true;
    }

    bool FindSection(const BvmView& view, const char* name, SectionView& section)
    {
        if (!view.sections || !name) return false;
        for (std::uint32_t i = 0; i < view.header->section_count; ++i)
        {
            const auto& sec = view.sections[i];
            if (!NameMatches(sec.name, name)) continue;
            if (sec.offset + sec.size > view.size) return false;
            if (!IsAligned8(sec.offset)) return false;
            section.data = view.base + sec.offset;
            section.size = sec.size;
            section.flags = sec.flags;
            return true;
        }
        return false;
    }
}
