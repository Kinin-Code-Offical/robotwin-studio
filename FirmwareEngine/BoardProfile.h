#pragma once

#include <cstddef>
#include <string>

namespace firmware
{
    struct BoardProfile
    {
        std::string id;
        std::string mcu;
        std::size_t flash_bytes = 0;
        std::size_t sram_bytes = 0;
        std::size_t eeprom_bytes = 0;
        std::size_t io_bytes = 0;
        int pin_count = 0;
        double cpu_hz = 16000000.0;
        std::size_t bootloader_bytes = 0;
        bool core_limited = false;
    };

    const BoardProfile& GetBoardProfile(const std::string& id);
    const BoardProfile& GetDefaultBoardProfile();
}
