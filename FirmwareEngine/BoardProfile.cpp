#include "BoardProfile.h"

#include <algorithm>

namespace firmware
{
    namespace
    {
        std::string NormalizeId(const std::string& value)
        {
            std::string out;
            out.reserve(value.size());
            for (char c : value)
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    out.push_back(c);
                }
                else if (c >= 'A' && c <= 'Z')
                {
                    out.push_back(static_cast<char>(c - 'A' + 'a'));
                }
            }
            return out;
        }

        const BoardProfile kDefaultProfile{
            "ArduinoUno",
            "ATmega328P",
            0x8000,
            0x0800,
            0x0400,
            0x0100,
            20,
            16000000.0,
            0x0200,
            false};

        const BoardProfile kNanoProfile{
            "ArduinoNano",
            "ATmega328P",
            0x8000,
            0x0800,
            0x0400,
            0x0100,
            20,
            16000000.0,
            0x0200,
            false};

        const BoardProfile kMegaProfile{
            "ArduinoMega",
            "ATmega2560",
            0x40000,
            0x2000,
            0x1000,
            0x0200,
            70,
            16000000.0,
            0x2000,
            true};

        const BoardProfile kProMiniProfile{
            "ArduinoProMini",
            "ATmega328P",
            0x8000,
            0x0800,
            0x0400,
            0x0100,
            20,
            16000000.0,
            0x0200,
            false};
    }

    const BoardProfile& GetDefaultBoardProfile()
    {
        return kDefaultProfile;
    }

    const BoardProfile& GetBoardProfile(const std::string& id)
    {
        std::string key = NormalizeId(id);
        if (key == "arduinouno" || key == "uno")
        {
            return kDefaultProfile;
        }
        if (key == "arduinonano" || key == "nano")
        {
            return kNanoProfile;
        }
        if (key == "arduinomega" || key == "mega")
        {
            return kMegaProfile;
        }
        if (key == "arduinomega2560" || key == "mega2560")
        {
            return kMegaProfile;
        }
        if (key == "arduinopromini" || key == "promini")
        {
            return kProMiniProfile;
        }
        return kDefaultProfile;
    }
}
