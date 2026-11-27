// RobotWin Studio: the Arduino-ESP32 3.x LEDC calls on a 2.x core (Bluepad32's ESP32 core is built on 2.x).
// Added only to the sketch's own file when it is built with that core, so the sketch reads the same in both modes and
// on the 3.x core: ledcAttach(pin, freq, bits) takes the next free channel for the pin, ledcWrite(pin, duty) /
// ledcRead(pin) / ledcWriteTone(pin, freq) / ledcDetach(pin) go to the pin's channel. Libraries and the core keep the
// 2.x calls (the names are only redefined in the sketch).
#pragma once
#include <Arduino.h>

#if defined(ESP_ARDUINO_VERSION_MAJOR) && ESP_ARDUINO_VERSION_MAJOR < 3

#include "soc/soc_caps.h"
#if defined(CONFIG_IDF_TARGET_ESP32)
#define RW_LEDC_CHANNELS (SOC_LEDC_CHANNEL_NUM * 2)   // high and low speed groups: channels 0–15, as the 2.x core counts them
#else
#define RW_LEDC_CHANNELS SOC_LEDC_CHANNEL_NUM
#endif

static int8_t rw_ledc_of[SOC_GPIO_PIN_COUNT];
static uint8_t rw_ledc_used = 0;
static bool rw_ledc_ready = false;

static inline int rw_ledc_channel(uint8_t pin)
{
    if (!rw_ledc_ready)
    {
        for (int i = 0; i < SOC_GPIO_PIN_COUNT; i++) rw_ledc_of[i] = -1;
        rw_ledc_ready = true;
    }
    return pin < SOC_GPIO_PIN_COUNT ? rw_ledc_of[pin] : -1;
}

static inline bool rw_ledcAttach(uint8_t pin, uint32_t freq, uint8_t resolution)
{
    int ch = rw_ledc_channel(pin);
    if (ch < 0)
    {
        if (pin >= SOC_GPIO_PIN_COUNT || rw_ledc_used >= RW_LEDC_CHANNELS) return false;
        ch = rw_ledc_used++;
        rw_ledc_of[pin] = (int8_t)ch;
    }
    if (ledcSetup((uint8_t)ch, freq, resolution) == 0) return false;
    ledcAttachPin(pin, (uint8_t)ch);
    return true;
}

static inline bool rw_ledcWrite(uint8_t pin, uint32_t duty)
{
    int ch = rw_ledc_channel(pin);
    if (ch < 0) return false;
    ledcWrite((uint8_t)ch, duty);
    return true;
}

static inline uint32_t rw_ledcRead(uint8_t pin)
{
    int ch = rw_ledc_channel(pin);
    return ch < 0 ? 0 : ledcRead((uint8_t)ch);
}

static inline uint32_t rw_ledcWriteTone(uint8_t pin, uint32_t freq)
{
    int ch = rw_ledc_channel(pin);
    return ch < 0 ? 0 : (uint32_t)ledcWriteTone((uint8_t)ch, freq);
}

static inline bool rw_ledcDetach(uint8_t pin)
{
    if (rw_ledc_channel(pin) < 0) return false;
    ledcDetachPin(pin);
    rw_ledc_of[pin] = -1;
    return true;
}

#define ledcAttach rw_ledcAttach
#define ledcWrite rw_ledcWrite
#define ledcRead rw_ledcRead
#define ledcWriteTone rw_ledcWriteTone
#define ledcDetach rw_ledcDetach

#endif
