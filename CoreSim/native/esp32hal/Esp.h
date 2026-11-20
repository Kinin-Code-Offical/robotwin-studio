/* EspClass (ESP.xxx()). Chip facts are those of an ESP32-D0WDQ6 on a 4 MB DevKit, marked as simulated. */
#ifndef RW_ESP_H
#define RW_ESP_H
#ifdef __cplusplus
#include <stdint.h>
#include <stddef.h>
#include "WString.h"

typedef enum { FM_QIO = 0x00, FM_QOUT = 0x01, FM_DIO = 0x02, FM_DOUT = 0x03, FM_FAST_READ = 0x04, FM_SLOW_READ = 0x05, FM_UNKNOWN = 0xff } FlashMode_t;

class EspClass {
public:
    uint32_t getHeapSize();
    uint32_t getFreeHeap();
    uint32_t getMinFreeHeap();
    uint32_t getMaxAllocHeap();
    uint32_t getPsramSize() { return 0; }
    uint32_t getFreePsram() { return 0; }
    uint32_t getMinFreePsram() { return 0; }
    uint32_t getMaxAllocPsram() { return 0; }

    uint16_t getChipRevision() { return 1; }
    const char* getChipModel() { return "ESP32-D0WDQ6 (simulated)"; }
    uint8_t getChipCores() { return 2; }
    uint32_t getCpuFreqMHz() { return 240; }
    uint32_t getCycleCount();
    const char* getSdkVersion() { return "v5.1.4-robotwin-sim"; }
    const char* getCoreVersion() { return "3.0.7-robotwin-sim"; }

    void deepSleep(uint64_t time_us);
    void restart();

    uint32_t getFlashChipSize() { return 4u * 1024u * 1024u; }
    uint32_t getFlashChipSpeed() { return 80000000u; }
    FlashMode_t getFlashChipMode() { return FM_DIO; }
    uint32_t getSketchSize() { return 280000u; }
    uint32_t getFreeSketchSpace() { return 1310720u - 280000u; }
    String getSketchMD5() { return String("00000000000000000000000000000000"); }
    uint64_t getEfuseMac();
};

extern EspClass ESP;

#endif
#endif
