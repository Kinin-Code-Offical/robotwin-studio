/*
 * [neopixel] RobotWin's WS2812 strip driver with the Adafruit_NeoPixel interface, written on Arduino-ESP32 3.x's RMT
 * API (rmtInit/rmtWrite at 10 MHz, as the core's rgbLedWrite sends), so a sketch written for the library runs in both
 * ESP32 modes: in the fast simulation the HAL hands the frame to the strip wired to the pin, on the real chip (and on
 * RobotWin's Xtensa emulator) the RMT sends it. Pixels are kept in the strip's own byte order, scaled by the brightness
 * when they are set, as the library does.
 */
#ifndef RW_ADAFRUIT_NEOPIXEL_H
#define RW_ADAFRUIT_NEOPIXEL_H

#include <Arduino.h>
#include <math.h>
#include <stdlib.h>
#include <string.h>

typedef uint16_t neoPixelType;

/* The type: where red, green, blue (and white) sit in a pixel's bytes, two bits each (white = red: no white LED). */
#define NEO_RGB ((0 << 6) | (0 << 4) | (1 << 2) | (2))
#define NEO_RBG ((0 << 6) | (0 << 4) | (2 << 2) | (1))
#define NEO_GRB ((1 << 6) | (1 << 4) | (0 << 2) | (2))
#define NEO_GBR ((2 << 6) | (2 << 4) | (0 << 2) | (1))
#define NEO_BRG ((1 << 6) | (1 << 4) | (2 << 2) | (0))
#define NEO_BGR ((2 << 6) | (2 << 4) | (1 << 2) | (0))
#define NEO_WRGB ((0 << 6) | (1 << 4) | (2 << 2) | (3))
#define NEO_RGBW ((3 << 6) | (0 << 4) | (1 << 2) | (2))
#define NEO_RBGW ((3 << 6) | (0 << 4) | (2 << 2) | (1))
#define NEO_GRBW ((3 << 6) | (1 << 4) | (0 << 2) | (2))
#define NEO_GBRW ((3 << 6) | (2 << 4) | (0 << 2) | (1))
#define NEO_BRGW ((3 << 6) | (1 << 4) | (2 << 2) | (0))
#define NEO_BGRW ((3 << 6) | (2 << 4) | (1 << 2) | (0))
#define NEO_KHZ800 0x0000
#define NEO_KHZ400 0x0100

class Adafruit_NeoPixel {
public:
    Adafruit_NeoPixel(uint16_t n, int16_t p = 6, neoPixelType t = NEO_GRB + NEO_KHZ800)
        : begun(false), brightness(0), pixels(NULL), items(NULL), endTime(0), pin(-1), rmtReady(false), numLEDs(0), numBytes(0),
          type(0), rOffset(0), gOffset(0), bOffset(0), wOffset(0) {
        updateType(t);
        updateLength(n);
        setPin(p);
    }

    Adafruit_NeoPixel()
        : begun(false), brightness(0), pixels(NULL), items(NULL), endTime(0), pin(-1), rmtReady(false), numLEDs(0), numBytes(0),
          type(0), rOffset(0), gOffset(0), bOffset(0), wOffset(0) {
        updateType(NEO_GRB + NEO_KHZ800);
    }

    ~Adafruit_NeoPixel() {
        free(pixels);
        free(items);
        if (rmtReady) rmtDeinit(pin);
    }

    void begin(void) {
        if (pin >= 0) {
            pinMode(pin, OUTPUT);
            digitalWrite(pin, LOW);
        }

        begun = true;
    }

    /* Sends the pixels: one frame, latched by the strip after the line stays low. */
    void show(void) {
        if (!pixels || pin < 0) return;
        uint32_t since = micros() - endTime;
        if (endTime != 0 && since < 300) delayMicroseconds(300 - since);   // the previous frame's latch
        if (!rmtReady) {
            rmtReady = rmtInit(pin, RMT_TX_MODE, RMT_MEM_NUM_BLOCKS_1, 10000000);
            if (!rmtReady) return;
        }

        const bool slow = (type & NEO_KHZ400) != 0;
        const uint16_t h0 = slow ? 5 : 4, l0 = slow ? 20 : 8, h1 = slow ? 12 : 8, l1 = slow ? 13 : 4;   // 0.1 us ticks
        size_t k = 0;
        for (uint16_t i = 0; i < numBytes; i++)
            for (int b = 7; b >= 0; b--, k++) {
                bool one = (pixels[i] >> b) & 1;
                items[k].level0 = 1;
                items[k].duration0 = one ? h1 : h0;
                items[k].level1 = 0;
                items[k].duration1 = one ? l1 : l0;
            }

        rmtWrite(pin, items, k, RMT_WAIT_FOR_EVER);
        endTime = micros();
    }

    void setPin(int16_t p) {
        if (rmtReady) {
            rmtDeinit(pin);
            rmtReady = false;
        }

        if (begun && pin >= 0) pinMode(pin, INPUT);
        pin = p;
        if (begun && pin >= 0) {
            pinMode(pin, OUTPUT);
            digitalWrite(pin, LOW);
        }
    }

    void setPixelColor(uint16_t n, uint8_t r, uint8_t g, uint8_t b) {
        if (n >= numLEDs) return;
        if (brightness) {
            r = (uint8_t)((r * brightness) >> 8);
            g = (uint8_t)((g * brightness) >> 8);
            b = (uint8_t)((b * brightness) >> 8);
        }

        uint8_t* p = pixel(n);
        if (wOffset != rOffset) p[wOffset] = 0;
        p[rOffset] = r;
        p[gOffset] = g;
        p[bOffset] = b;
    }

    void setPixelColor(uint16_t n, uint8_t r, uint8_t g, uint8_t b, uint8_t w) {
        if (n >= numLEDs) return;
        if (brightness) {
            r = (uint8_t)((r * brightness) >> 8);
            g = (uint8_t)((g * brightness) >> 8);
            b = (uint8_t)((b * brightness) >> 8);
            w = (uint8_t)((w * brightness) >> 8);
        }

        uint8_t* p = pixel(n);
        if (wOffset != rOffset) p[wOffset] = w;
        p[rOffset] = r;
        p[gOffset] = g;
        p[bOffset] = b;
    }

    void setPixelColor(uint16_t n, uint32_t c) {
        setPixelColor(n, (uint8_t)(c >> 16), (uint8_t)(c >> 8), (uint8_t)c, (uint8_t)(c >> 24));
    }

    void fill(uint32_t c = 0, uint16_t first = 0, uint16_t count = 0) {
        if (first >= numLEDs) return;
        uint16_t end = count == 0 ? numLEDs : (uint16_t)((uint32_t)first + count > numLEDs ? numLEDs : first + count);
        for (uint16_t i = first; i < end; i++) setPixelColor(i, c);
    }

    /* 0 (dark) to 255 (full); the stored pixels are rescaled, so colours lose some precision as with the library. */
    void setBrightness(uint8_t b) {
        uint8_t next = (uint8_t)(b + 1);
        if (next == brightness) return;
        uint8_t old = (uint8_t)(brightness - 1);
        uint16_t scale;
        if (old == 0) scale = 0;
        else if (b == 255) scale = (uint16_t)(65535 / old);
        else scale = (uint16_t)((((uint16_t)next << 8) - 1) / old);
        for (uint16_t i = 0; i < numBytes; i++) pixels[i] = (uint8_t)((pixels[i] * scale) >> 8);
        brightness = next;
    }

    void clear(void) {
        if (pixels) memset(pixels, 0, numBytes);
    }

    void updateLength(uint16_t n) {
        free(pixels);
        free(items);
        numBytes = (uint16_t)(n * (wOffset == rOffset ? 3 : 4));
        pixels = (uint8_t*)calloc(numBytes ? numBytes : 1, 1);
        items = (rmt_data_t*)malloc(sizeof(rmt_data_t) * 8u * (numBytes ? numBytes : 1));
        if (pixels && items) {
            numLEDs = n;
        } else {
            free(pixels);
            free(items);
            pixels = NULL;
            items = NULL;
            numLEDs = numBytes = 0;
        }
    }

    void updateType(neoPixelType t) {
        bool hadWhite = wOffset != rOffset;
        type = t;
        wOffset = (t >> 6) & 3;
        rOffset = (t >> 4) & 3;
        gOffset = (t >> 2) & 3;
        bOffset = t & 3;
        if (pixels && hadWhite != (wOffset != rOffset)) updateLength(numLEDs);
    }

    bool canShow(void) { return endTime == 0 || micros() - endTime >= 300; }
    uint8_t* getPixels(void) const { return pixels; }
    uint8_t getBrightness(void) const { return (uint8_t)(brightness - 1); }
    int16_t getPin(void) const { return pin; }
    uint16_t numPixels(void) const { return numLEDs; }

    uint32_t getPixelColor(uint16_t n) const {
        if (n >= numLEDs) return 0;
        const uint8_t* p = pixels + n * (wOffset == rOffset ? 3 : 4);
        uint32_t r = p[rOffset], g = p[gOffset], b = p[bOffset], w = wOffset != rOffset ? p[wOffset] : 0;
        if (brightness) {
            r = (r << 8) / brightness;
            g = (g << 8) / brightness;
            b = (b << 8) / brightness;
            w = (w << 8) / brightness;
        }

        return (w << 24) | (r << 16) | (g << 8) | b;
    }

    void rainbow(uint16_t first_hue = 0, int8_t reps = 1, uint8_t saturation = 255, uint8_t bright = 255, bool gammify = true) {
        if (numLEDs == 0) return;
        for (uint16_t i = 0; i < numLEDs; i++) {
            uint16_t hue = (uint16_t)(first_hue + (i * reps * 65536L) / numLEDs);
            uint32_t color = ColorHSV(hue, saturation, bright);
            if (gammify) color = gamma32(color);
            setPixelColor(i, color);
        }
    }

    static uint32_t Color(uint8_t r, uint8_t g, uint8_t b) { return ((uint32_t)r << 16) | ((uint32_t)g << 8) | b; }
    static uint32_t Color(uint8_t r, uint8_t g, uint8_t b, uint8_t w) {
        return ((uint32_t)w << 24) | ((uint32_t)r << 16) | ((uint32_t)g << 8) | b;
    }

    /* hue 0–65535 once around the wheel (red, yellow, green, cyan, blue, magenta, red), then saturation and value. */
    static uint32_t ColorHSV(uint16_t hue, uint8_t sat = 255, uint8_t val = 255) {
        uint32_t h = ((uint32_t)hue * 1530u + 32768u) / 65536u;
        if (h >= 1530) h -= 1530;
        uint32_t r, g, b;
        if (h < 255) { r = 255; g = h; b = 0; }
        else if (h < 510) { r = 510 - h; g = 255; b = 0; }
        else if (h < 765) { r = 0; g = 255; b = h - 510; }
        else if (h < 1020) { r = 0; g = 1020 - h; b = 255; }
        else if (h < 1275) { r = h - 1020; g = 0; b = 255; }
        else { r = 255; g = 0; b = 1530 - h; }
        uint32_t v1 = 1u + val, s1 = 1u + sat, s2 = 255u - sat;
        r = ((((r * s1) >> 8) + s2) * v1) >> 8;
        g = ((((g * s1) >> 8) + s2) * v1) >> 8;
        b = ((((b * s1) >> 8) + s2) * v1) >> 8;
        return (r << 16) | (g << 8) | b;
    }

    /* Perceived brightness to LED duty (gamma 2.6). */
    static uint8_t gamma8(uint8_t x) { return (uint8_t)(pow(x / 255.0, 2.6) * 255.0 + 0.5); }
    static uint32_t gamma32(uint32_t x) {
        return ((uint32_t)gamma8((uint8_t)(x >> 24)) << 24) | ((uint32_t)gamma8((uint8_t)(x >> 16)) << 16) |
               ((uint32_t)gamma8((uint8_t)(x >> 8)) << 8) | gamma8((uint8_t)x);
    }

    /* One sine period over 0–255, 0–255 out (128 at 0, 255 at 64). */
    static uint8_t sine8(uint8_t x) {
        double v = floor(sin(x * (2.0 * 3.14159265358979323846 / 256.0)) * 127.5 + 128.0);
        return (uint8_t)(v > 255 ? 255 : v < 0 ? 0 : v);
    }

protected:
    uint8_t* pixel(uint16_t n) { return pixels + n * (wOffset == rOffset ? 3 : 4); }

    bool begun;
    uint8_t brightness;   // the set brightness + 1 (0: full, nothing scaled)
    uint8_t* pixels;
    rmt_data_t* items;
    uint32_t endTime;
    int16_t pin;
    bool rmtReady;
    uint16_t numLEDs;
    uint16_t numBytes;
    neoPixelType type;
    uint8_t rOffset, gOffset, bOffset, wOffset;
};

#endif
