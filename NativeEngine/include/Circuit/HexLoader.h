#pragma once
#include <cstdint>
#include <fstream>
#include <iostream>
#include <string>
#include <vector>


namespace NativeEngine::Utils {
class HexLoader {
public:
  static bool ParseHexNibble(char c, std::uint8_t &out) {
    if (c >= '0' && c <= '9') {
      out = static_cast<std::uint8_t>(c - '0');
      return true;
    }
    if (c >= 'A' && c <= 'F') {
      out = static_cast<std::uint8_t>(c - 'A' + 10);
      return true;
    }
    if (c >= 'a' && c <= 'f') {
      out = static_cast<std::uint8_t>(c - 'a' + 10);
      return true;
    }
    return false;
  }

  static bool ParseHexByte(const char *ptr, std::uint8_t &out) {
    std::uint8_t hi = 0;
    std::uint8_t lo = 0;
    if (!ParseHexNibble(ptr[0], hi))
      return false;
    if (!ParseHexNibble(ptr[1], lo))
      return false;
    out = static_cast<std::uint8_t>((hi << 4) | lo);
    return true;
  }

  static bool LoadHexText(std::vector<std::uint8_t> &flash, const char *text) {
    if (text == nullptr)
      return false;
    std::uint32_t upper = 0;
    const char *line = text;
    while (*line != '\0') {
      if (*line == '\r' || *line == '\n') {
        ++line;
        continue;
      }
      if (*line != ':')
        return false; // Invalid start
      const char *ptr = line + 1;
      std::uint8_t len = 0;
      std::uint8_t addr_hi = 0;
      std::uint8_t addr_lo = 0;
      std::uint8_t type = 0;
      if (!ParseHexByte(ptr, len))
        return false;
      ptr += 2;
      if (!ParseHexByte(ptr, addr_hi))
        return false;
      ptr += 2;
      if (!ParseHexByte(ptr, addr_lo))
        return false;
      ptr += 2;
      if (!ParseHexByte(ptr, type))
        return false;
      ptr += 2;
      std::uint32_t addr = (static_cast<std::uint32_t>(addr_hi) << 8) | addr_lo;
      std::uint8_t checksum =
          static_cast<std::uint8_t>(len + addr_hi + addr_lo + type);

      if (type == 0x00) // Data
      {
        for (std::uint8_t i = 0; i < len; ++i) {
          std::uint8_t data = 0;
          if (!ParseHexByte(ptr, data))
            return false;
          ptr += 2;
          checksum += data;
          std::uint32_t final_addr = (upper << 16) + addr + i;
          if (final_addr < flash.size()) {
            flash[final_addr] = data;
          }
        }
      } else if (type == 0x04) // Ext Linear Addr
      {
        std::uint8_t up_hi = 0, up_lo = 0;
        if (!ParseHexByte(ptr, up_hi))
          return false;
        ptr += 2;
        if (!ParseHexByte(ptr, up_lo))
          return false;
        ptr += 2;
        checksum += up_hi + up_lo;
        upper = (static_cast<std::uint32_t>(up_hi) << 8) | up_lo;
      } else if (type == 0x01) {
      } // EOF

      std::uint8_t read_checksum = 0;
      if (!ParseHexByte(ptr, read_checksum))
        return false;
      checksum += read_checksum;
      if (checksum != 0)
        return false; // Fail checksum

      while (*ptr != '\0' && *ptr != '\n' && *ptr != '\r')
        ++ptr;
      line = ptr;
      if (type == 0x01)
        break;
    }
    return true;
  }
};
} // namespace NativeEngine::Utils
