/* RoboTwin ESP32 simulation - esp32-hal subset (GPIO, ADC, DAC, LEDC, touch, timers, time). */
#ifndef RW_ESP32_HAL_H
#define RW_ESP32_HAL_H

#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ---------------------------------------------------------------- GPIO */
void pinMode(uint8_t pin, uint8_t mode);
void digitalWrite(uint8_t pin, uint8_t val);
int digitalRead(uint8_t pin);

typedef void (*voidFuncPtr)(void);
typedef void (*voidFuncPtrArg)(void*);
void attachInterrupt(uint8_t pin, voidFuncPtr handler, int mode);
void attachInterruptArg(uint8_t pin, voidFuncPtrArg handler, void* arg, int mode);
void detachInterrupt(uint8_t pin);
void enableInterrupt(uint8_t pin);
void disableInterrupt(uint8_t pin);
void rw_set_interrupts(int enabled);

int8_t rw_digital_pin_to_analog_channel(uint8_t pin);
int8_t rw_digital_pin_to_touch_channel(uint8_t pin);

/* ---------------------------------------------------------------- time */
unsigned long millis(void);
unsigned long micros(void);
void delay(uint32_t ms);
void delayMicroseconds(uint32_t us);
void yield(void);
uint32_t getCpuFrequencyMhz(void);
bool setCpuFrequencyMhz(uint32_t cpu_freq_mhz);
uint32_t getXtalFrequencyMhz(void);
uint32_t getApbFrequency(void);

/* ---------------------------------------------------------------- ADC */
typedef enum { ADC_0db = 0, ADC_2_5db = 1, ADC_6db = 2, ADC_11db = 3, ADC_ATTENDB_MAX = 4 } adc_attenuation_t;
#define ADC_ATTEN_DB_0 ADC_0db
#define ADC_ATTEN_DB_2_5 ADC_2_5db
#define ADC_ATTEN_DB_6 ADC_6db
#define ADC_ATTEN_DB_11 ADC_11db
#define ADC_ATTEN_DB_12 ADC_11db
typedef adc_attenuation_t adc_atten_t;

uint16_t analogRead(uint8_t pin);
uint32_t analogReadMilliVolts(uint8_t pin);
uint16_t analogReadRaw(uint8_t pin);
void analogReadResolution(uint8_t bits);
void analogSetWidth(uint8_t bits);
void analogSetAttenuation(adc_attenuation_t attenuation);
void analogSetPinAttenuation(uint8_t pin, adc_attenuation_t attenuation);
void analogSetClockDiv(uint8_t clockDiv);
bool adcAttachPin(uint8_t pin);
void analogSetVRefPin(uint8_t pin);

/* ---------------------------------------------------------------- DAC (GPIO25, GPIO26) */
void dacWrite(uint8_t pin, uint8_t value);
void dacDisable(uint8_t pin);

/* ---------------------------------------------------------------- touch */
typedef uint32_t touch_value_t;
touch_value_t touchRead(uint8_t pin);
void touchAttachInterrupt(uint8_t pin, voidFuncPtr userFunc, touch_value_t threshold);
void touchDetachInterrupt(uint8_t pin);
void touchSetCycles(uint16_t measure, uint16_t sleep);

/* ---------------------------------------------------------------- misc */
float temperatureRead(void);
int hallRead(void);

/* ---------------------------------------------------------------- logging
 * Errors and warnings are printed (they appear on UART0 like on a board); info/debug/verbose
 * are dropped, as with the default "Core Debug Level: None/Error" build. */
void rw_sketch_log(int level, const char* format, ...);
void rw_sketch_log_tag(int level, const char* tag, const char* format, ...);
#define log_e(...) rw_sketch_log(1, __VA_ARGS__)
#define log_w(...) rw_sketch_log(2, __VA_ARGS__)
#define log_i(...) ((void)0)
#define log_d(...) ((void)0)
#define log_v(...) ((void)0)
#define log_n(...) rw_sketch_log(1, __VA_ARGS__)
#define isr_log_e(...) rw_sketch_log(1, __VA_ARGS__)
#define isr_log_w(...) rw_sketch_log(2, __VA_ARGS__)
#define isr_log_i(...) ((void)0)
#define isr_log_d(...) ((void)0)
#define isr_log_v(...) ((void)0)
#define ESP_LOGE(tag, ...) rw_sketch_log_tag(1, tag, __VA_ARGS__)
#define ESP_LOGW(tag, ...) rw_sketch_log_tag(2, tag, __VA_ARGS__)
#define ESP_LOGI(tag, ...) ((void)(tag))
#define ESP_LOGD(tag, ...) ((void)(tag))
#define ESP_LOGV(tag, ...) ((void)(tag))
#define ESP_EARLY_LOGE ESP_LOGE
#define ESP_EARLY_LOGW ESP_LOGW
#define ESP_EARLY_LOGI ESP_LOGI
#define ESP_DRAM_LOGE ESP_LOGE

#ifdef __cplusplus
}

/* ---------------------------------------------------------------- analogWrite (LEDC based) */
void analogWrite(uint8_t pin, int value);
void analogWriteResolution(uint8_t bits);
void analogWriteResolution(uint8_t pin, uint8_t bits);
void analogWriteFrequency(double freq);
void analogWriteFrequency(uint8_t pin, uint32_t freq);

/* ---------------------------------------------------------------- LEDC
 * Both APIs are accepted:
 *   core 2.x: ledcSetup(channel, freq, bits); ledcAttachPin(pin, channel); ledcWrite(channel, duty)
 *   core 3.x: ledcAttach(pin, freq, bits) / ledcAttachChannel(...); ledcWrite(pin, duty)
 * ledcWrite/ledcWriteTone/ledcRead... take a pin once a 3.x attach has been made for it,
 * otherwise a channel configured with ledcSetup(). */
typedef enum { NOTE_C, NOTE_Cs, NOTE_D, NOTE_Eb, NOTE_E, NOTE_F, NOTE_Fs, NOTE_G, NOTE_Gs, NOTE_A, NOTE_Bb, NOTE_B, NOTE_MAX } note_t;

double ledcSetup(uint8_t channel, double freq, uint8_t resolution_bits);
void ledcAttachPin(uint8_t pin, uint8_t channel);
void ledcDetachPin(uint8_t pin);
bool ledcAttach(uint8_t pin, uint32_t freq, uint8_t resolution);
bool ledcAttachChannel(uint8_t pin, uint32_t freq, uint8_t resolution, uint8_t channel);
bool ledcDetach(uint8_t pin);
bool ledcWrite(uint8_t chanOrPin, uint32_t duty);
double ledcWriteTone(uint8_t chanOrPin, double freq);
double ledcWriteNote(uint8_t chanOrPin, note_t note, uint8_t octave);
uint32_t ledcRead(uint8_t chanOrPin);
double ledcReadFreq(uint8_t chanOrPin);
double ledcChangeFrequency(uint8_t chanOrPin, double freq, uint8_t resolution);
bool ledcFade(uint8_t pin, uint32_t start_duty, uint32_t target_duty, int max_fade_time_ms);
bool ledcOutputInvert(uint8_t pin, bool out_invert);

/* ---------------------------------------------------------------- hardware timers
 * core 2.x: timerBegin(num, divider, countUp); timerAttachInterrupt(t, fn, edge);
 *           timerAlarmWrite(t, value, autoreload); timerAlarmEnable(t)
 * core 3.x: timerBegin(frequency); timerAttachInterrupt(t, fn); timerAlarm(t, value, autoreload, count) */
typedef struct rw_hw_timer hw_timer_t;
hw_timer_t* timerBegin(uint32_t frequency);
hw_timer_t* timerBegin(uint8_t num, uint16_t divider, bool countUp);
void timerEnd(hw_timer_t* timer);
void timerStart(hw_timer_t* timer);
void timerStop(hw_timer_t* timer);
void timerRestart(hw_timer_t* timer);
void timerWrite(hw_timer_t* timer, uint64_t val);
uint64_t timerRead(hw_timer_t* timer);
uint64_t timerReadMicros(hw_timer_t* timer);
uint64_t timerReadMilis(hw_timer_t* timer);
double timerReadSeconds(hw_timer_t* timer);
uint32_t timerGetFrequency(hw_timer_t* timer);
void timerAttachInterrupt(hw_timer_t* timer, void (*userFunc)(void), bool edge = false);
void timerAttachInterruptArg(hw_timer_t* timer, void (*userFunc)(void*), void* arg);
void timerDetachInterrupt(hw_timer_t* timer);
void timerAlarm(hw_timer_t* timer, uint64_t alarm_value, bool autoreload, uint64_t reload_count);
void timerAlarmWrite(hw_timer_t* timer, uint64_t alarm_value, bool autoreload);
void timerAlarmEnable(hw_timer_t* timer);
void timerAlarmDisable(hw_timer_t* timer);
bool timerAlarmEnabled(hw_timer_t* timer);

/* ADC overloads taking the ESP-IDF style attenuation constants. */
void analogSetAttenuation(int attenuation);
void analogSetPinAttenuation(uint8_t pin, int attenuation);

#endif /* __cplusplus */

#endif
