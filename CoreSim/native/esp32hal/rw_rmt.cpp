/*
 * [neopixel] The RMT (Arduino-ESP32 3.x API) and the RGB LED helpers of the fast simulation. What a TX pin sends is
 * decoded as a WS2812 strip decodes it (a high pulse over 0.55 us is a 1, most significant bit first) and the bytes go to
 * the host (Esp32RadioOp.LedStripWrite), which hands them to the addressable LED strip wired to that pin, the same frames
 * the Xtensa emulator's RMT delivers in the hardware-compatible mode. A blocking write takes as long as its pulses. The
 * eight channels share eight memory blocks, as on the chip. Reception (IR receivers) is not simulated.
 */
#include "Arduino.h"
#include "rw_internal.h"

using namespace rw;

namespace {

struct RmtPin {
    bool used;
    bool tx;
    int blocks;
    uint32_t hz;
    uint64_t busy_until_ns;   // an asynchronous transmission is on the line until then
};

RmtPin g_rmt[40];
uint8_t g_frame[4 + 4096];   // i32 gpio, then the bytes (1365 RGB LEDs a frame)

const int kRmtBlocks = 8;
const int kLedStripWrite = 0x63;   // keep in sync with Esp32RadioOp.LedStripWrite

void rmt_log(int level, int line, const char* func, const char* format, ...) {
    char msg[256];
    va_list ap;
    va_start(ap, format);
    vsnprintf(msg, sizeof msg, format, ap);
    va_end(ap);
    rw::log(level, "[%6lu][%c][esp32-hal-rmt.c:%d] %s(): %s", (unsigned long)(uptime_ns() / 1000000ull),
            level == RW_LOG_ERROR ? 'E' : 'W', line, func, msg);
}

int blocks_in_use() {
    int n = 0;
    for (const auto& p : g_rmt)
        if (p.used) n += p.blocks;
    return n;
}

bool tx_pin(int pin, const char* func) {
    if (pin < 0 || pin >= 40 || !g_rmt[pin].used) {
        rmt_log(RW_LOG_ERROR, 118, func, "GPIO %d - RMT channel not found", pin);
        return false;
    }

    if (!g_rmt[pin].tx) {
        rmt_log(RW_LOG_ERROR, 124, func, "GPIO %d - RMT channel is not TX", pin);
        return false;
    }

    return true;
}

/* Decodes the symbols and sends the frame; returns how long the pulses last (ns). */
uint64_t send_frame(int pin, const rmt_data_t* data, size_t n) {
    const double tick_ns = 1e9 / (double)g_rmt[pin].hz;
    uint64_t ticks = 0;
    size_t bit = 0;
    const size_t max_bits = (sizeof g_frame - 4) * 8;
    memset(g_frame + 4, 0, sizeof g_frame - 4);
    for (size_t i = 0; i < n; i++) {
        uint32_t d0 = data[i].duration0, d1 = data[i].duration1;
        if (d0 == 0) break;   // the end marker
        uint32_t high = data[i].level0 ? d0 : (data[i].level1 ? d1 : 0);
        if (bit < max_bits) {
            if (high * tick_ns > 550.0) g_frame[4 + bit / 8] |= (uint8_t)(0x80 >> (bit % 8));
            bit++;
        }

        ticks += d0 + d1;
        if (d1 == 0) break;   // the end marker in the second half
    }

    if (bit >= max_bits) log_once(0x900 + pin, RW_LOG_WARN, "[%6lu][W][esp32-hal-rmt.c] rmtWrite(): frame longer than 1365 LEDs, the rest is not shown",
                                  (unsigned long)(uptime_ns() / 1000000ull));
    int32_t gpio = pin;
    memcpy(g_frame, &gpio, 4);
    size_t bytes = bit / 8;
    if (bytes > 0) radio(kLedStripWrite, g_frame, (int)(4 + bytes), nullptr, 0);
    return (uint64_t)(ticks * tick_ns);
}

}  // namespace

namespace rw {
void hal_rmt_reset() { memset(g_rmt, 0, sizeof g_rmt); }
}  // namespace rw

extern "C" bool rmtInit(int pin, rmt_ch_dir_t dir, rmt_reserve_memsize_t memsize, uint32_t frequency_Hz) {
    charge(cost(20000.0));
    if (pin < 0 || pin >= 40) {
        rmt_log(RW_LOG_ERROR, 435, "rmtInit", "GPIO %d is not valid", pin);
        return false;
    }

    if (dir == RMT_TX_MODE && pin >= 34) {
        rmt_log(RW_LOG_ERROR, 440, "rmtInit", "GPIO %d is only valid for input", pin);
        return false;
    }

    // The channel clock: APB (80 MHz) or REF_TICK (1 MHz) over an 8-bit divider.
    if (frequency_Hz < 3922 || frequency_Hz > 80000000u) {
        rmt_log(RW_LOG_ERROR, 452, "rmtInit", "GPIO %d - resolution %u Hz is out of range", pin, (unsigned)frequency_Hz);
        return false;
    }

    int blocks = (int)memsize < 1 ? 1 : (int)memsize > kRmtBlocks ? kRmtBlocks : (int)memsize;
    int taken = blocks_in_use() - (g_rmt[pin].used ? g_rmt[pin].blocks : 0);
    if (taken + blocks > kRmtBlocks) {
        rmt_log(RW_LOG_ERROR, 470, "rmtInit", "GPIO %d - no free RMT channel (memory blocks in use: %d of 8)", pin, taken);
        return false;
    }

    g_rmt[pin].used = true;
    g_rmt[pin].tx = dir == RMT_TX_MODE;
    g_rmt[pin].blocks = blocks;
    g_rmt[pin].hz = frequency_Hz;
    if (g_rmt[pin].tx) {
        pinMode((uint8_t)pin, OUTPUT);
        digitalWrite((uint8_t)pin, LOW);   // the line idles low between frames
    }

    return true;
}

extern "C" bool rmtDeinit(int pin) {
    if (pin < 0 || pin >= 40 || !g_rmt[pin].used) return false;
    g_rmt[pin] = RmtPin{};
    return true;
}

extern "C" bool rmtSetEOT(int pin, uint8_t) { return tx_pin(pin, "rmtSetEOT"); }

extern "C" bool rmtSetCarrier(int pin, bool carrier_en, bool, uint32_t, float) {
    if (pin < 0 || pin >= 40 || !g_rmt[pin].used) return false;
    if (carrier_en)
        log_once(0x940 + pin, RW_LOG_WARN, "[%6lu][W][esp32-hal-rmt.c] rmtSetCarrier(): the carrier is not simulated (frames still reach LED strips)",
                 (unsigned long)(uptime_ns() / 1000000ull));
    return true;
}

extern "C" bool rmtSetRxMinThreshold(int pin, uint8_t) { return pin >= 0 && pin < 40 && g_rmt[pin].used; }
extern "C" bool rmtSetRxMaxThreshold(int pin, uint16_t) { return pin >= 0 && pin < 40 && g_rmt[pin].used; }

extern "C" bool rmtWrite(int pin, rmt_data_t* data, size_t num_rmt_symbols, uint32_t timeout_ms) {
    charge(cost(5000.0));
    if (!tx_pin(pin, "rmtWrite")) return false;
    if (data == nullptr) {
        rmt_log(RW_LOG_ERROR, 312, "rmtWrite", "GPIO %d - RMT Write Data NULL pointer", pin);
        return false;
    }

    uint64_t now = uptime_ns();
    if (g_rmt[pin].busy_until_ns > now) charge(g_rmt[pin].busy_until_ns - now);   // the previous frame finishes first
    uint64_t length = send_frame(pin, data, num_rmt_symbols);
    if (timeout_ms != RMT_WAIT_FOR_EVER && length > (uint64_t)timeout_ms * 1000000ull) {
        charge((uint64_t)timeout_ms * 1000000ull);
        g_rmt[pin].busy_until_ns = uptime_ns() + (length - (uint64_t)timeout_ms * 1000000ull);
        return false;   // still sending when the wait ran out
    }

    charge(length);
    return true;
}

extern "C" bool rmtWriteAsync(int pin, rmt_data_t* data, size_t num_rmt_symbols) {
    charge(cost(5000.0));
    if (!tx_pin(pin, "rmtWriteAsync") || data == nullptr) return false;
    uint64_t now = uptime_ns();
    if (g_rmt[pin].busy_until_ns > now) return false;   // the channel is still sending
    g_rmt[pin].busy_until_ns = now + send_frame(pin, data, num_rmt_symbols);
    return true;
}

extern "C" bool rmtWriteLooping(int pin, rmt_data_t* data, size_t num_rmt_symbols) {
    // A looping frame shows the same colours over and over: the strip latches it once.
    return rmtWriteAsync(pin, data, num_rmt_symbols);
}

extern "C" bool rmtWriteRepeated(int pin, rmt_data_t* data, size_t num_rmt_symbols, uint32_t) {
    return rmtWriteAsync(pin, data, num_rmt_symbols);
}

extern "C" bool rmtTransmitCompleted(int pin) {
    charge(cost(1000.0));
    return pin >= 0 && pin < 40 && g_rmt[pin].used && g_rmt[pin].busy_until_ns <= uptime_ns();
}

static bool no_reception(int pin, const char* func) {
    log_once(0x980 + (pin & 0x3F), RW_LOG_WARN, "[%6lu][W][esp32-hal-rmt.c] %s(): RMT reception is not simulated", (unsigned long)(uptime_ns() / 1000000ull), func);
    return false;
}

extern "C" bool rmtRead(int pin, rmt_data_t*, size_t* num_rmt_symbols, uint32_t timeout_ms) {
    if (num_rmt_symbols) *num_rmt_symbols = 0;
    if (timeout_ms != RMT_WAIT_FOR_EVER) charge((uint64_t)timeout_ms * 1000000ull);
    return no_reception(pin, "rmtRead");
}

extern "C" bool rmtReadAsync(int pin, rmt_data_t*, size_t* num_rmt_symbols) {
    if (num_rmt_symbols) *num_rmt_symbols = 0;
    return no_reception(pin, "rmtReadAsync");
}

extern "C" bool rmtReceiveCompleted(int) { return false; }

/* ---- RGB LED: one WS2812 through the RMT at 10 MHz, as the core's esp32-hal-rgb-led.c sends it */
extern "C" void rgbLedWriteOrdered(uint8_t pin, rgb_led_color_order_t order, uint8_t red_val, uint8_t green_val, uint8_t blue_val) {
    if (!rmtInit(pin, RMT_TX_MODE, RMT_MEM_NUM_BLOCKS_1, 10000000)) {
        rmt_log(RW_LOG_ERROR, 40, "rgbLedWrite", "RGB LED driver initialization failed for GPIO%d!", pin);
        return;
    }

    uint8_t first, second, third;
    switch (order) {
    case LED_COLOR_ORDER_RGB: first = red_val; second = green_val; third = blue_val; break;
    case LED_COLOR_ORDER_BGR: first = blue_val; second = green_val; third = red_val; break;
    case LED_COLOR_ORDER_BRG: first = blue_val; second = red_val; third = green_val; break;
    case LED_COLOR_ORDER_RBG: first = red_val; second = blue_val; third = green_val; break;
    case LED_COLOR_ORDER_GBR: first = green_val; second = blue_val; third = red_val; break;
    default: first = green_val; second = red_val; third = blue_val; break;   // GRB, the WS2812's own order
    }

    const uint8_t colors[3] = {first, second, third};
    rmt_data_t led[24];
    int i = 0;
    for (int c = 0; c < 3; c++)
        for (int b = 7; b >= 0; b--, i++) {
            bool one = (colors[c] >> b) & 1;
            led[i].level0 = 1;
            led[i].duration0 = one ? 8 : 4;
            led[i].level1 = 0;
            led[i].duration1 = one ? 4 : 8;
        }

    rmtWrite(pin, led, 24, RMT_WAIT_FOR_EVER);
}

extern "C" void rgbLedWrite(uint8_t pin, uint8_t red_val, uint8_t green_val, uint8_t blue_val) {
    rgbLedWriteOrdered(pin, LED_COLOR_ORDER_GRB, red_val, green_val, blue_val);
}

extern "C" void neopixelWrite(uint8_t pin, uint8_t red_val, uint8_t green_val, uint8_t blue_val) {
    rgbLedWrite(pin, red_val, green_val, blue_val);
}
