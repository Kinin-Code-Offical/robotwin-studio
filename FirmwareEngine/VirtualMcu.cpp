#ifndef __NO_FLOAT128__
#define __NO_FLOAT128__
#endif

#include "VirtualMcu.h"

#include <cstring>
#include <fstream>

#include "Circuit/HexLoader.h"

namespace firmware
{
    namespace
    {
        constexpr std::uint32_t BvmMagic = 0x43534E45; // "CSNE"
        constexpr std::uint64_t SectionTextHex = 1ull << 3;
        constexpr std::uint64_t SectionTextRaw = 1ull << 4;
        constexpr std::size_t RegSize = 32;
        constexpr int AvrPinCount = 20;
        constexpr int AnalogCount = 16;
        constexpr std::uint16_t UdrAddress[4] = {AVR_UDR0, AVR_UDR1, AVR_UDR2, AVR_UDR3};
        constexpr std::uint16_t UcsrAAddress[4] = {AVR_UCSR0A, AVR_UCSR1A, AVR_UCSR2A, AVR_UCSR3A};
        constexpr std::uint16_t UcsrBAddress[4] = {AVR_UCSR0B, AVR_UCSR1B, AVR_UCSR2B, AVR_UCSR3B};
        constexpr std::uint16_t UcsrCAddress[4] = {AVR_UCSR0C, AVR_UCSR1C, AVR_UCSR2C, AVR_UCSR3C};
        constexpr std::uint16_t UbrrLAddress[4] = {AVR_UBRR0L, AVR_UBRR1L, AVR_UBRR2L, AVR_UBRR3L};
        constexpr std::uint16_t UbrrHAddress[4] = {AVR_UBRR0H, AVR_UBRR1H, AVR_UBRR2H, AVR_UBRR3H};
        constexpr std::uint8_t UartRxCompleteBit = 7;
        constexpr std::uint8_t UartTxCompleteBit = 6;
        constexpr std::uint8_t UartDataRegisterEmptyBit = 5;
        constexpr std::uint8_t UartFrameErrorBit = 4;
        constexpr std::uint8_t UartDataOverrunBit = 3;
        constexpr std::uint8_t UartParityErrorBit = 2;

        struct BvmHeader
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

        struct BvmSection
        {
            char name[8];
            std::uint64_t offset;
            std::uint64_t size;
            std::uint64_t flags;
            std::uint64_t reserved;
        };

        bool SectionNameEquals(const BvmSection &section, const char *name)
        {
            return std::strncmp(section.name, name, 8) == 0;
        }
    }

    VirtualMcu::VirtualMcu(const BoardProfile &profile)
        : _profile(profile)
    {
        _state.flash.resize(_profile.flash_bytes, 0);
        _state.sram.resize(_profile.sram_bytes, 0);
        _state.eeprom.resize(_profile.eeprom_bytes, 0);
        _state.io.resize(_profile.io_bytes, 0);
        _state.regs.resize(RegSize, 0);
        _pinCount = _profile.pin_count;
        if (_pinCount <= 0)
            _pinCount = AvrPinCount;
        if (_profile.core_limited && _pinCount > AvrPinCount)
        {
            _pinCount = AvrPinCount;
        }
        _pinInputs.assign(static_cast<std::size_t>(_pinCount), -1);
        _pinValueScratch.assign(_state.io.size(), 0);
        _pinValueTouchedFlags.assign(_state.io.size(), 0);
        _pinValueTouched.clear();
        _pinValueTouched.reserve(static_cast<std::size_t>(_pinCount));
        _analogInputs.assign(AnalogCount, 0.0f);
        Reset();
    }

    void VirtualMcu::Reset()
    {
        ResetState(true);
    }

    void VirtualMcu::SoftReset()
    {
        ResetState(false);
    }

    void VirtualMcu::ResetState(bool clearFlash)
    {
        if (clearFlash)
        {
            std::fill(_state.flash.begin(), _state.flash.end(), 0);
        }
        std::fill(_state.sram.begin(), _state.sram.end(), 0);
        // EEPROM persists across resets by design.
        std::fill(_state.io.begin(), _state.io.end(), 0);
        std::fill(_state.regs.begin(), _state.regs.end(), 0);
        _tickCount = 0;
        _perf = {};
        _adcNoiseSeed = static_cast<std::uint32_t>(_tickCount ^ 0x9E3779B9u);
        _wdtCyclesRemaining = 0.0;
        _wdtResetArmed = false;
        _spiActive = false;
        _spiCyclesRemaining = 0.0;
        _spiData = 0;
        _spiSpsrRead = false;
        _twiActive = false;
        _twiCyclesRemaining = 0.0;
        _twiData = 0;
        _twiStatus = 0xF8;
        _timer0Up = true;
        _timer1Up = true;
        _timer2Up = true;
        _timer3Up = true;
        _timer4Up = true;
        _timer5Up = true;
        _lastPinb = 0;
        _lastPinc = 0;
        _lastPind = 0;
        _lastPine = 0;
        for (int i = 0; i < static_cast<int>(_uarts.size()); ++i)
        {
            auto &uart = _uarts[static_cast<std::size_t>(i)];
            uart.rxQueue.clear();
            uart.rxReady = false;
            uart.rxCyclesRemaining = 0.0;
            uart.rxCount = 0;
            uart.errorSeed = static_cast<std::uint32_t>(_tickCount ^ (0xC001D00Du + i * 101u));
            uart.txQueue.clear();
            uart.txPending.clear();
            uart.txActive = false;
            uart.txCyclesRemaining = 0.0;
            uart.udrEmptyCyclesRemaining = 0.0;
            uart.txByte = 0;
            uart.cachedUbr = 0;
            uart.cachedU2x = false;
            uart.cycleCacheValid = false;
            uart.cyclesPerBitCache = 0.0;
        }

        AVR_Init(&_state.core,
                 _state.flash.data(), _state.flash.size(),
                 _state.sram.data(), _state.sram.size(),
                 _state.io.data(), _state.io.size(),
                 _state.regs.data(), _state.regs.size());
        if (_profile.mcu == "ATmega2560")
        {
            AVR_SetMcuKind(&_state.core, AVR_MCU_2560);
        }
        AVR_SetIoWriteHook(&_state.core, IoWriteHook, this);
        AVR_SetIoReadHook(&_state.core, IoReadHook, this);
        for (int i = 0; i < static_cast<int>(_uarts.size()); ++i)
        {
            if (!HasUart(i))
            {
                continue;
            }
            std::uint8_t ucsra = static_cast<std::uint8_t>((1u << UartDataRegisterEmptyBit));
            AVR_IoWrite(&_state.core, UcsrAAddress[i], ucsra);
        }
    }

    bool VirtualMcu::EraseFlash(std::string &error)
    {
        (void)error;
        if (_state.flash.empty())
        {
            error = "Flash not allocated";
            return false;
        }
        std::size_t limit = _state.flash.size();
        if (_profile.bootloader_bytes > 0 && _profile.bootloader_bytes < limit)
        {
            limit -= _profile.bootloader_bytes;
        }
        std::fill(_state.flash.begin(), _state.flash.begin() + static_cast<std::ptrdiff_t>(limit), 0xFF);
        return true;
    }

    bool VirtualMcu::ProgramFlash(std::uint32_t byteAddress, const std::uint8_t *data, std::size_t size, std::string &error)
    {
        if (!data || size == 0)
        {
            error = "No flash data";
            return false;
        }
        if (_state.flash.empty())
        {
            error = "Flash not allocated";
            return false;
        }

        std::size_t limit = _state.flash.size();
        if (_profile.bootloader_bytes > 0 && _profile.bootloader_bytes < limit)
        {
            limit -= _profile.bootloader_bytes;
        }

        const std::size_t start = static_cast<std::size_t>(byteAddress);
        if (start >= limit)
        {
            error = "Flash address out of range";
            return false;
        }
        if (start + size > limit)
        {
            error = "Flash write exceeds application region";
            return false;
        }
        std::memcpy(_state.flash.data() + start, data, size);
        return true;
    }

    bool VirtualMcu::ReadFlash(std::uint32_t byteAddress, std::uint8_t *outData, std::size_t size, std::string &error) const
    {
        if (!outData || size == 0)
        {
            error = "No output buffer";
            return false;
        }
        if (_state.flash.empty())
        {
            error = "Flash not allocated";
            return false;
        }

        std::size_t limit = _state.flash.size();
        if (_profile.bootloader_bytes > 0 && _profile.bootloader_bytes < limit)
        {
            limit -= _profile.bootloader_bytes;
        }
        const std::size_t start = static_cast<std::size_t>(byteAddress);
        if (start >= limit)
        {
            error = "Flash address out of range";
            return false;
        }
        if (start + size > limit)
        {
            error = "Flash read exceeds application region";
            return false;
        }
        std::memcpy(outData, _state.flash.data() + start, size);
        return true;
    }

    void VirtualMcu::SamplePinOutputs(std::uint8_t *outPins, std::size_t outCount) const
    {
        if (!outPins || outCount == 0)
        {
            return;
        }

        // Default everything to "unknown / not driving".
        std::memset(outPins, static_cast<int>(kPinValueUnknown), outCount);

        auto clampByte = [](int value) -> std::uint8_t
        {
            if (value < 0) return 0;
            if (value > 255) return 255;
            return static_cast<std::uint8_t>(value);
        };

        auto computePwmDuty = [&](int pin, std::uint8_t &duty) -> bool
        {
            if (_profile.mcu != "ATmega328P")
            {
                return false;
            }

            if (pin == 5 || pin == 6)
            {
                std::uint8_t tccr0a = GetIo(AVR_TCCR0A);
                std::uint8_t tccr0b = GetIo(AVR_TCCR0B);
                std::uint8_t wgm = static_cast<std::uint8_t>((tccr0a & 0x03) | ((tccr0b & 0x08) >> 1));
                bool pwmMode = (wgm == 0x01 || wgm == 0x03 || wgm == 0x05 || wgm == 0x07);
                if (!pwmMode)
                {
                    return false;
                }
                std::uint8_t ocr0a = GetIo(AVR_OCR0A);
                std::uint8_t ocr0b = GetIo(AVR_OCR0B);
                std::uint8_t top = 0xFF;
                if (wgm == 0x05 || wgm == 0x07)
                {
                    top = ocr0a;
                }
                if (top == 0)
                {
                    duty = 0;
                    return true;
                }
                if (pin == 6)
                {
                    bool com0a = (tccr0a & 0x80) != 0;
                    if (!com0a) return false;
                    duty = clampByte(static_cast<int>((static_cast<double>(ocr0a) / top) * 255.0 + 0.5));
                    return true;
                }
                bool com0b = (tccr0a & 0x20) != 0;
                if (!com0b) return false;
                duty = clampByte(static_cast<int>((static_cast<double>(ocr0b) / top) * 255.0 + 0.5));
                return true;
            }

            if (pin == 3 || pin == 11)
            {
                std::uint8_t tccr2a = GetIo(AVR_TCCR2A);
                std::uint8_t tccr2b = GetIo(AVR_TCCR2B);
                std::uint8_t wgm = static_cast<std::uint8_t>((tccr2a & 0x03) | ((tccr2b & 0x08) >> 1));
                bool pwmMode = (wgm == 0x01 || wgm == 0x03 || wgm == 0x05 || wgm == 0x07);
                if (!pwmMode)
                {
                    return false;
                }
                std::uint8_t ocr2a = GetIo(AVR_OCR2A);
                std::uint8_t ocr2b = GetIo(AVR_OCR2B);
                std::uint8_t top = 0xFF;
                if (wgm == 0x05 || wgm == 0x07)
                {
                    top = ocr2a;
                }
                if (top == 0)
                {
                    duty = 0;
                    return true;
                }
                if (pin == 11)
                {
                    bool com2a = (tccr2a & 0x80) != 0;
                    if (!com2a) return false;
                    duty = clampByte(static_cast<int>((static_cast<double>(ocr2a) / top) * 255.0 + 0.5));
                    return true;
                }
                bool com2b = (tccr2a & 0x20) != 0;
                if (!com2b) return false;
                duty = clampByte(static_cast<int>((static_cast<double>(ocr2b) / top) * 255.0 + 0.5));
                return true;
            }

            if (pin == 9 || pin == 10)
            {
                std::uint8_t tccr1a = GetIo(AVR_TCCR1A);
                std::uint8_t tccr1b = GetIo(AVR_TCCR1B);
                std::uint8_t wgm = static_cast<std::uint8_t>((tccr1a & 0x03) | ((tccr1b & 0x18) >> 1));
                bool pwmMode = (wgm == 1 || wgm == 2 || wgm == 3 || wgm == 5 || wgm == 6 || wgm == 7 ||
                                wgm == 8 || wgm == 9 || wgm == 10 || wgm == 11 || wgm == 14 || wgm == 15);
                if (!pwmMode)
                {
                    return false;
                }
                std::uint16_t ocr1a = static_cast<std::uint16_t>(
                    static_cast<std::uint16_t>(GetIo(AVR_OCR1AL)) |
                    (static_cast<std::uint16_t>(GetIo(AVR_OCR1AH)) << 8));
                std::uint16_t ocr1b = static_cast<std::uint16_t>(
                    static_cast<std::uint16_t>(GetIo(AVR_OCR1BL)) |
                    (static_cast<std::uint16_t>(GetIo(AVR_OCR1BH)) << 8));
                std::uint16_t top = 0xFFFF;
                switch (wgm)
                {
                case 1:
                case 5:
                    top = 0x00FF;
                    break;
                case 2:
                case 6:
                    top = 0x01FF;
                    break;
                case 3:
                case 7:
                    top = 0x03FF;
                    break;
                default:
                    top = 0xFFFF;
                    break;
                }
                if (top == 0)
                {
                    duty = 0;
                    return true;
                }
                if (pin == 9)
                {
                    bool com1a = (tccr1a & 0x80) != 0;
                    if (!com1a) return false;
                    duty = clampByte(static_cast<int>((static_cast<double>(ocr1a) / top) * 255.0 + 0.5));
                    return true;
                }
                bool com1b = (tccr1a & 0x20) != 0;
                if (!com1b) return false;
                duty = clampByte(static_cast<int>((static_cast<double>(ocr1b) / top) * 255.0 + 0.5));
                return true;
            }

            return false;
        };

        std::size_t limit = static_cast<std::size_t>(_pinCount);
        if (limit > outCount)
        {
            limit = outCount;
        }

        for (std::size_t pin = 0; pin < limit; ++pin)
        {
            std::uint16_t ddr = 0;
            std::uint16_t port = 0;
            std::uint16_t pinReg = 0;
            std::uint8_t bit = 0;
            if (!PinToPort(static_cast<int>(pin), ddr, port, pinReg, bit))
            {
                continue;
            }

            const std::uint8_t ddrValue = GetIo(ddr);
            const std::uint8_t mask = static_cast<std::uint8_t>(1u << bit);
            if ((ddrValue & mask) == 0)
            {
                // INPUT -> not driving.
                outPins[pin] = kPinValueUnknown;
                continue;
            }

            const std::uint8_t portValue = GetIo(port);
            std::uint8_t pwmDuty = 0;
            if (computePwmDuty(static_cast<int>(pin), pwmDuty))
            {
                outPins[pin] = pwmDuty;
            }
            else
            {
                outPins[pin] = (portValue & mask) != 0 ? 1 : 0;
            }
        }
    }

    bool VirtualMcu::LoadBvm(const std::vector<std::uint8_t> &buffer, std::string &error)
    {
        const std::uint8_t *data = nullptr;
        std::size_t size = 0;
        std::uint64_t flags = 0;
        if (!ParseBvmText(buffer, data, size, flags, error))
        {
            return false;
        }
        Reset();
        return LoadTextSection(data, size, flags, error);
    }

    bool VirtualMcu::ParseBvmText(const std::vector<std::uint8_t> &buffer, const std::uint8_t *&data,
                                  std::size_t &size, std::uint64_t &flags, std::string &error)
    {
        if (buffer.size() < sizeof(BvmHeader))
        {
            error = "BVM buffer too small";
            return false;
        }
        auto *header = reinterpret_cast<const BvmHeader *>(buffer.data());
        if (header->magic != BvmMagic)
        {
            error = "Invalid BVM magic";
            return false;
        }
        if (header->section_table_offset + header->section_count * sizeof(BvmSection) > buffer.size())
        {
            error = "BVM section table out of bounds";
            return false;
        }

        auto *sections = reinterpret_cast<const BvmSection *>(buffer.data() + header->section_table_offset);
        for (std::uint32_t i = 0; i < header->section_count; ++i)
        {
            const auto &section = sections[i];
            if (!SectionNameEquals(section, ".text"))
                continue;
            if (section.offset + section.size > buffer.size())
            {
                error = "BVM text section out of bounds";
                return false;
            }
            data = buffer.data() + section.offset;
            size = static_cast<std::size_t>(section.size);
            flags = section.flags;
            return true;
        }

        error = "BVM missing .text section";
        return false;
    }

    bool VirtualMcu::LoadTextSection(const std::uint8_t *data, std::size_t size, std::uint64_t flags, std::string &error)
    {
        if (!data || size == 0)
        {
            error = "Empty firmware section";
            return false;
        }

        if ((flags & SectionTextHex) != 0)
        {
            std::string hex(reinterpret_cast<const char *>(data), size);
            std::size_t maxAddr = 0;
            if (!MeasureHexMaxAddress(hex, maxAddr))
            {
                error = "Invalid Intel HEX section";
                return false;
            }
            std::size_t limit = _state.flash.size();
            if (_profile.bootloader_bytes > 0 && _profile.bootloader_bytes < limit)
            {
                limit -= _profile.bootloader_bytes;
            }
            if (maxAddr > limit)
            {
                error = "Firmware exceeds flash size";
                return false;
            }
            if (!NativeEngine::Utils::HexLoader::LoadHexText(_state.flash, hex.c_str()))
            {
                error = "Failed to parse Intel HEX section";
                return false;
            }
            return true;
        }

        if ((flags & SectionTextRaw) != 0 || flags == 0)
        {
            std::size_t limit = _state.flash.size();
            if (_profile.bootloader_bytes > 0 && _profile.bootloader_bytes < limit)
            {
                limit -= _profile.bootloader_bytes;
            }
            if (size > limit)
            {
                error = "Firmware exceeds flash size";
                return false;
            }
            std::size_t copySize = size > _state.flash.size() ? _state.flash.size() : size;
            std::memcpy(_state.flash.data(), data, copySize);
            return true;
        }

        error = "Unsupported BVM text flags";
        return false;
    }

    void VirtualMcu::StepCycles(std::uint64_t cycles)
    {
        std::uint64_t tickStart = _tickCount;
        while (cycles > 0)
        {
            std::uint8_t cost = AVR_ExecuteNext(&_state.core);
            if (cost == 0)
                cost = 1;
            cycles = (cost > cycles) ? 0 : (cycles - cost);
            _tickCount += cost;
        }

        std::uint64_t executed = _tickCount - tickStart;
        if (executed > 0)
        {
            _perf.cycles += executed;
            SimulateTimer0(executed);
            SimulateTimer1(executed);
            SimulateTimer2(executed);
            if (_profile.mcu == "ATmega2560")
            {
                SimulateTimer3(executed);
                SimulateTimer4(executed);
                SimulateTimer5(executed);
            }
        }

        std::uint8_t adcsra = GetIo(AVR_ADCSRA);
        if ((adcsra & (1u << 6)) != 0)
        {
            if (_adcCyclesRemaining <= 0.0)
            {
                int prescaler = 2;
                std::uint8_t adps = static_cast<std::uint8_t>(adcsra & 0x07);
                switch (adps)
                {
                case 0:
                    prescaler = 2;
                    break;
                case 1:
                    prescaler = 2;
                    break;
                case 2:
                    prescaler = 4;
                    break;
                case 3:
                    prescaler = 8;
                    break;
                case 4:
                    prescaler = 16;
                    break;
                case 5:
                    prescaler = 32;
                    break;
                case 6:
                    prescaler = 64;
                    break;
                case 7:
                    prescaler = 128;
                    break;
                }
                _adcCyclesRemaining = 13.0 * prescaler;
            }
        }

        if (_adcCyclesRemaining > 0.0)
        {
            _adcCyclesRemaining -= static_cast<double>(executed);
            if (_adcCyclesRemaining <= 0.0)
            {
                std::uint8_t admux = GetIo(AVR_ADMUX);
                std::uint8_t adcsrb = GetIo(AVR_ADCSRB);
                std::uint8_t channel = 0;
                if (_profile.mcu == "ATmega2560")
                {
                    std::uint8_t mux5 = (adcsrb & (1u << 3)) != 0 ? 8 : 0;
                    channel = static_cast<std::uint8_t>(mux5 | (admux & 0x07));
                }
                else
                {
                    channel = static_cast<std::uint8_t>(admux & 0x0F);
                }
                float voltage = 0.0f;
                if (channel < _analogInputs.size())
                {
                    voltage = _analogInputs[channel];
                }
                if (voltage < 0.0f)
                    voltage = 0.0f;
                float refVoltage = 5.0f;
                std::uint8_t refs = static_cast<std::uint8_t>(admux & 0xC0);
                if (refs == 0xC0)
                {
                    refVoltage = 1.1f;
                }
                if (refVoltage <= 0.001f)
                    refVoltage = 5.0f;
                if (voltage > refVoltage)
                    voltage = refVoltage;
                double scaled = (static_cast<double>(voltage) / refVoltage) * 1023.0;
                _adcNoiseSeed = _adcNoiseSeed * 1664525u + 1013904223u;
                int noise = static_cast<int>((_adcNoiseSeed >> 30) & 0x03) - 1;
                if (noise > 1)
                    noise = 1;
                int value = static_cast<int>(scaled + 0.5) + noise;
                if (value < 0)
                    value = 0;
                if (value > 1023)
                    value = 1023;
                bool adlar = (admux & (1u << 5)) != 0;
                if (adlar)
                {
                    std::uint8_t adcl = static_cast<std::uint8_t>((value & 0x03) << 6);
                    std::uint8_t adch = static_cast<std::uint8_t>((value >> 2) & 0xFF);
                    AVR_IoWrite(&_state.core, AVR_ADCL, adcl);
                    AVR_IoWrite(&_state.core, AVR_ADCH, adch);
                }
                else
                {
                    AVR_IoWrite(&_state.core, AVR_ADCL, static_cast<std::uint8_t>(value & 0xFF));
                    AVR_IoWrite(&_state.core, AVR_ADCH, static_cast<std::uint8_t>((value >> 8) & 0x03));
                }
                _perf.adcSamples++;
                adcsra = GetIo(AVR_ADCSRA);
                adcsra = static_cast<std::uint8_t>(adcsra & ~(1u << 6));
                AVR_IoWrite(&_state.core, AVR_ADCSRA, adcsra);
                std::size_t adcsraIndex = static_cast<std::size_t>(AVR_ADCSRA - AVR_IO_BASE);
                if (adcsraIndex < _state.io.size())
                {
                    _state.io[adcsraIndex] = static_cast<std::uint8_t>(_state.io[adcsraIndex] | (1u << 4));
                }
            }
        }

        double elapsed = static_cast<double>(executed);

        for (int channel = 0; channel < static_cast<int>(_uarts.size()); ++channel)
        {
            if (!HasUart(channel))
            {
                continue;
            }

            auto &uart = _uarts[static_cast<std::size_t>(channel)];
            if (!IsUartTxEnabled(channel))
            {
                uart.txActive = false;
                uart.txCyclesRemaining = 0.0;
                uart.udrEmptyCyclesRemaining = 0.0;
                std::uint8_t ucsra = GetIo(UcsrAAddress[channel]);
                ucsra = static_cast<std::uint8_t>(ucsra | (1u << UartDataRegisterEmptyBit));
                AVR_IoWrite(&_state.core, UcsrAAddress[channel], ucsra);
            }
            else
            {
                double cyclesPerBit = ComputeUartCyclesPerBit(channel);
                double cyclesPerByte = cyclesPerBit * 10.0;

                if (uart.udrEmptyCyclesRemaining > 0.0)
                {
                    uart.udrEmptyCyclesRemaining -= elapsed;
                    if (uart.udrEmptyCyclesRemaining < 0.0)
                    {
                        uart.udrEmptyCyclesRemaining = 0.0;
                    }
                }

                double txElapsed = elapsed;
                while (txElapsed > 0.0)
                {
                    if (uart.txActive)
                    {
                        if (uart.txCyclesRemaining > txElapsed)
                        {
                            uart.txCyclesRemaining -= txElapsed;
                            txElapsed = 0.0;
                            break;
                        }

                        txElapsed -= uart.txCyclesRemaining;
                        uart.txCyclesRemaining = 0.0;
                        uart.txActive = false;
                        HandleUartWrite(channel, uart.txByte);
                        std::uint8_t ucsra = GetIo(UcsrAAddress[channel]);
                        if (uart.txPending.empty())
                        {
                            ucsra = static_cast<std::uint8_t>(ucsra | (1u << UartTxCompleteBit));
                        }
                        AVR_IoWrite(&_state.core, UcsrAAddress[channel], ucsra);
                    }

                    if (!uart.txActive && !uart.txPending.empty())
                    {
                        uart.txByte = uart.txPending.front();
                        uart.txPending.pop_front();
                        uart.txActive = true;
                        uart.txCyclesRemaining = cyclesPerByte;
                        uart.udrEmptyCyclesRemaining = cyclesPerBit;
                        std::uint8_t ucsra = GetIo(UcsrAAddress[channel]);
                        ucsra = static_cast<std::uint8_t>(ucsra & ~(1u << UartTxCompleteBit));
                        AVR_IoWrite(&_state.core, UcsrAAddress[channel], ucsra);
                        continue;
                    }

                    break;
                }

                std::uint8_t ucsra = GetIo(UcsrAAddress[channel]);
                if (uart.txPending.empty() && uart.udrEmptyCyclesRemaining <= 0.0)
                {
                    ucsra = static_cast<std::uint8_t>(ucsra | (1u << UartDataRegisterEmptyBit));
                }
                else
                {
                    ucsra = static_cast<std::uint8_t>(ucsra & ~(1u << UartDataRegisterEmptyBit));
                }
                AVR_IoWrite(&_state.core, UcsrAAddress[channel], ucsra);
            }

            if (!IsUartRxEnabled(channel))
            {
                uart.rxReady = false;
                uart.rxCyclesRemaining = 0.0;
                uart.rxQueue.clear();
                std::uint8_t ucsra = GetIo(UcsrAAddress[channel]);
                ucsra = static_cast<std::uint8_t>(ucsra & ~(1u << UartRxCompleteBit));
                ucsra = static_cast<std::uint8_t>(ucsra & ~(1u << UartFrameErrorBit));
                ucsra = static_cast<std::uint8_t>(ucsra & ~(1u << UartParityErrorBit));
                AVR_IoWrite(&_state.core, UcsrAAddress[channel], ucsra);
            }
            else if (!uart.rxReady)
            {
                double cyclesPerByte = ComputeUartCyclesPerByte(channel);
                double rxElapsed = elapsed;
                while (rxElapsed > 0.0 && !uart.rxReady)
                {
                    if (uart.rxCyclesRemaining <= 0.0)
                    {
                        if (uart.rxQueue.empty())
                        {
                            break;
                        }
                        uart.rxCyclesRemaining = cyclesPerByte;
                    }

                    if (uart.rxCyclesRemaining > rxElapsed)
                    {
                        uart.rxCyclesRemaining -= rxElapsed;
                        rxElapsed = 0.0;
                        break;
                    }

                    rxElapsed -= uart.rxCyclesRemaining;
                    uart.rxCyclesRemaining = 0.0;
                    if (!uart.rxQueue.empty())
                    {
                        std::uint8_t next = uart.rxQueue.front();
                        uart.rxQueue.pop_front();
                        _perf.uartRxBytes[static_cast<std::size_t>(channel)]++;

                        std::uint8_t ucsrc = GetIo(UcsrCAddress[channel]);
                        bool parityEnabled = IsUartParityEnabled(channel);
                        bool twoStopBits = (ucsrc & (1u << 3)) != 0;

                        ++uart.rxCount;
                        std::uint32_t seed = NextUartErrorSeed(channel, next);
                        bool frameError = (seed & (twoStopBits ? 0x3FFu : 0x1FFu)) == 0;
                        bool parityError = false;
                        if (parityEnabled)
                        {
                            parityError = (((seed >> 10) & 0x7Fu) == 0);
                            if (parityError)
                            {
                                next = static_cast<std::uint8_t>(next ^ 0x01);
                            }
                        }

                        AVR_IoWrite(&_state.core, UdrAddress[channel], next);
                        uart.rxReady = true;
                        std::uint8_t ucsra = GetIo(UcsrAAddress[channel]);
                        ucsra = static_cast<std::uint8_t>(ucsra | (1u << UartRxCompleteBit));
                        ucsra = static_cast<std::uint8_t>(ucsra & ~(1u << UartFrameErrorBit));
                        ucsra = static_cast<std::uint8_t>(ucsra & ~(1u << UartParityErrorBit));
                        if (frameError)
                        {
                            ucsra = static_cast<std::uint8_t>(ucsra | (1u << UartFrameErrorBit));
                        }
                        if (parityError)
                        {
                            ucsra = static_cast<std::uint8_t>(ucsra | (1u << UartParityErrorBit));
                        }
                        AVR_IoWrite(&_state.core, UcsrAAddress[channel], ucsra);
                    }
                }
            }
        }

        std::uint8_t spcr = GetIo(AVR_SPCR);
        bool spiEnabled = (spcr & (1u << 6)) != 0;
        if (!spiEnabled)
        {
            _spiActive = false;
            _spiCyclesRemaining = 0.0;
        }
        if (_spiActive)
        {
            _spiCyclesRemaining -= elapsed;
            if (_spiCyclesRemaining <= 0.0)
            {
                _spiActive = false;
                _spiCyclesRemaining = 0.0;
                AVR_IoWrite(&_state.core, AVR_SPDR, _spiData);
                std::uint8_t spsr = GetIo(AVR_SPSR);
                spsr = static_cast<std::uint8_t>(spsr | (1u << 7));
                AVR_IoWrite(&_state.core, AVR_SPSR, spsr);
                _perf.spiTransfers++;
            }
        }

        std::uint8_t twcr = GetIo(AVR_TWCR);
        bool twiEnabled = (twcr & (1u << 2)) != 0;
        if (!twiEnabled)
        {
            _twiActive = false;
            _twiCyclesRemaining = 0.0;
        }
        if (_twiActive)
        {
            _twiCyclesRemaining -= elapsed;
            if (_twiCyclesRemaining <= 0.0)
            {
                _twiActive = false;
                _twiCyclesRemaining = 0.0;
                AVR_IoWrite(&_state.core, AVR_TWDR, _twiData);
                std::uint8_t twcr = GetIo(AVR_TWCR);
                bool ack = (twcr & (1u << 6)) != 0;
                if (_twiStatus == 0xF8)
                {
                    _twiStatus = ack ? 0x28 : 0x30;
                }
                std::uint8_t twsr = GetIo(AVR_TWSR);
                twsr = static_cast<std::uint8_t>((twsr & 0x03) | (_twiStatus & 0xF8));
                AVR_IoWrite(&_state.core, AVR_TWSR, twsr);
                twcr = static_cast<std::uint8_t>(twcr | (1u << 7));
                std::size_t twcrIdx = static_cast<std::size_t>(AVR_TWCR - AVR_IO_BASE);
                if (twcrIdx < _state.io.size())
                {
                    _state.io[twcrIdx] = twcr;
                }
                _perf.twiTransfers++;
                _twiStatus = 0xF8;
            }
        }

        std::uint8_t wdtcsr = GetIo(AVR_WDTCSR);
        bool wdtEnable = (wdtcsr & (1u << 3)) != 0 || (wdtcsr & (1u << 6)) != 0;
        if (wdtEnable)
        {
            if (_wdtCyclesRemaining <= 0.0)
            {
                int wdp = (wdtcsr & 0x07) | ((wdtcsr >> 5) & 0x01) * 8;
                static const double kTimeouts[] = {0.016, 0.032, 0.064, 0.125, 0.25, 0.5, 1.0, 2.0, 4.0, 8.0};
                int idx = wdp;
                if (idx < 0)
                    idx = 0;
                if (idx > 9)
                    idx = 9;
                _wdtCyclesRemaining = kTimeouts[idx] * _profile.cpu_hz;
            }
            _wdtCyclesRemaining -= elapsed;
            if (_wdtCyclesRemaining <= 0.0)
            {
                wdtcsr = static_cast<std::uint8_t>(wdtcsr | (1u << 7));
                AVR_IoWrite(&_state.core, AVR_WDTCSR, wdtcsr);
                if (wdtcsr & (1u << 3))
                {
                    _wdtResetArmed = true;
                }
                _wdtCyclesRemaining = 0.0;
            }
        }

        if (_wdtResetArmed)
        {
            _perf.wdtResets++;
            Reset();
        }
    }

    void VirtualMcu::SetInputPin(int pin, int value)
    {
        if (pin < 0 || pin >= static_cast<int>(_pinInputs.size()))
            return;
        if (value < 0)
        {
            _pinInputs[pin] = -1;
        }
        else
        {
            _pinInputs[pin] = value ? 1 : 0;
        }
    }

    void VirtualMcu::SyncInputs()
    {
        for (int pin = 0; pin < _pinCount; ++pin)
        {
            std::uint16_t ddr = 0;
            std::uint16_t port = 0;
            std::uint16_t pinReg = 0;
            std::uint8_t bit = 0;
            if (!PinToPort(pin, ddr, port, pinReg, bit))
            {
                continue;
            }
            if (pinReg >= _pinValueScratch.size())
            {
                continue;
            }
            std::uint8_t ddrValue = GetIo(ddr);
            std::uint8_t portValue = GetIo(port);
            bool isOutput = (ddrValue & (1u << bit)) != 0;
            int inputValue = _pinInputs[pin];
            bool value = isOutput ? ((portValue & (1u << bit)) != 0)
                                  : (inputValue >= 0 ? (inputValue != 0) : ((portValue & (1u << bit)) != 0));
            if (value)
            {
                if (_pinValueTouchedFlags[pinReg] == 0)
                {
                    _pinValueTouchedFlags[pinReg] = 1;
                    _pinValueTouched.push_back(pinReg);
                    _pinValueScratch[pinReg] = 0;
                }
                _pinValueScratch[pinReg] = static_cast<std::uint8_t>(_pinValueScratch[pinReg] | (1u << bit));
            }
        }

        for (const auto pinReg : _pinValueTouched)
        {
            AVR_IoWrite(&_state.core, pinReg, _pinValueScratch[pinReg]);
            _pinValueScratch[pinReg] = 0;
            _pinValueTouchedFlags[pinReg] = 0;
        }
        _pinValueTouched.clear();

        std::uint8_t pinb = GetIo(AVR_PINB);
        std::uint8_t pinc = GetIo(AVR_PINC);
        std::uint8_t pind = GetIo(AVR_PIND);
        std::uint8_t pine = GetIo(AVR_PINE);
        std::uint8_t pcicr = GetIo(AVR_PCICR);
        std::uint8_t pcifr = GetIo(AVR_PCIFR);
        std::uint8_t pcmsk0 = GetIo(AVR_PCMSK0);
        std::uint8_t pcmsk1 = GetIo(AVR_PCMSK1);
        std::uint8_t pcmsk2 = GetIo(AVR_PCMSK2);

        if ((pcicr & (1u << 0)) && ((pinb ^ _lastPinb) & pcmsk0))
        {
            pcifr = static_cast<std::uint8_t>(pcifr | (1u << 0));
        }
        if ((pcicr & (1u << 1)) && ((pinc ^ _lastPinc) & pcmsk1))
        {
            pcifr = static_cast<std::uint8_t>(pcifr | (1u << 1));
        }
        if ((pcicr & (1u << 2)) && ((pind ^ _lastPind) & pcmsk2))
        {
            pcifr = static_cast<std::uint8_t>(pcifr | (1u << 2));
        }
        AVR_IoWrite(&_state.core, AVR_PCIFR, pcifr);

        std::uint8_t eimsk = GetIo(AVR_EIMSK);
        std::uint8_t eifr = GetIo(AVR_EIFR);
        if (_profile.mcu == "ATmega2560")
        {
            if ((eimsk & 0x01) && ((pine ^ _lastPine) & (1u << 4)))
            {
                eifr = static_cast<std::uint8_t>(eifr | 0x01);
            }
            if ((eimsk & 0x02) && ((pine ^ _lastPine) & (1u << 5)))
            {
                eifr = static_cast<std::uint8_t>(eifr | 0x02);
            }
        }
        else
        {
            if ((eimsk & 0x01) && ((pind ^ _lastPind) & (1u << 2)))
            {
                eifr = static_cast<std::uint8_t>(eifr | 0x01);
            }
            if ((eimsk & 0x02) && ((pind ^ _lastPind) & (1u << 3)))
            {
                eifr = static_cast<std::uint8_t>(eifr | 0x02);
            }
        }
        AVR_IoWrite(&_state.core, AVR_EIFR, eifr);

        _lastPinb = pinb;
        _lastPinc = pinc;
        _lastPind = pind;
        _lastPine = pine;
    }

    std::uint8_t VirtualMcu::GetIo(std::uint16_t address) const
    {
        return AVR_IoRead(const_cast<AvrCore *>(&_state.core), address);
    }

    void VirtualMcu::SetIo(std::uint16_t address, std::uint8_t value)
    {
        AVR_IoWrite(&_state.core, address, value);
    }

    bool VirtualMcu::IsUartRxEnabled(int channel) const
    {
        std::uint8_t ucsrb = GetIo(UcsrBAddress[channel]);
        return (ucsrb & (1u << 4)) != 0;
    }

    bool VirtualMcu::IsUartTxEnabled(int channel) const
    {
        std::uint8_t ucsrb = GetIo(UcsrBAddress[channel]);
        return (ucsrb & (1u << 3)) != 0;
    }

    bool VirtualMcu::IsUartParityEnabled(int channel) const
    {
        std::uint8_t ucsrc = GetIo(UcsrCAddress[channel]);
        return (ucsrc & (1u << 5)) != 0 || (ucsrc & (1u << 4)) != 0;
    }

    bool VirtualMcu::HasUart(int channel) const
    {
        if (channel < 0 || channel >= static_cast<int>(_uarts.size()))
        {
            return false;
        }
        if (_profile.mcu == "ATmega2560")
        {
            return true;
        }
        return channel == 0;
    }

    double VirtualMcu::ComputeUartCyclesPerByte(int channel)
    {
        return ComputeUartCyclesPerBit(channel) * 10.0;
    }

    double VirtualMcu::ComputeUartCyclesPerBit(int channel)
    {
        auto &uart = _uarts[static_cast<std::size_t>(channel)];
        std::uint8_t ucsra = GetIo(UcsrAAddress[channel]);
        bool u2x = (ucsra & (1u << 1)) != 0;
        std::uint16_t ubrr = static_cast<std::uint16_t>(
            static_cast<std::uint16_t>(GetIo(UbrrHAddress[channel])) << 8 |
            static_cast<std::uint16_t>(GetIo(UbrrLAddress[channel])));
        if (uart.cycleCacheValid && uart.cachedU2x == u2x && uart.cachedUbr == ubrr)
        {
            return uart.cyclesPerBitCache;
        }
        double divisor = u2x ? 8.0 : 16.0;
        double cyclesPerBit = divisor * static_cast<double>(ubrr + 1);
        if (cyclesPerBit < 1.0)
        {
            cyclesPerBit = 1.0;
        }
        uart.cachedU2x = u2x;
        uart.cachedUbr = ubrr;
        uart.cyclesPerBitCache = cyclesPerBit;
        uart.cycleCacheValid = true;
        return cyclesPerBit;
    }

    double VirtualMcu::ComputeSpiCyclesPerBit() const
    {
        std::uint8_t spcr = GetIo(AVR_SPCR);
        std::uint8_t spsr = GetIo(AVR_SPSR);
        std::uint8_t spr = static_cast<std::uint8_t>(spcr & 0x03);
        bool spi2x = (spsr & (1u << 0)) != 0;
        int divisor = 4;
        switch (spr)
        {
        case 0:
            divisor = spi2x ? 2 : 4;
            break;
        case 1:
            divisor = spi2x ? 8 : 16;
            break;
        case 2:
            divisor = spi2x ? 32 : 64;
            break;
        case 3:
            divisor = spi2x ? 64 : 128;
            break;
        }
        if (divisor < 1)
            divisor = 1;
        return static_cast<double>(divisor);
    }

    double VirtualMcu::ComputeTwiCyclesPerBit() const
    {
        std::uint8_t twbr = GetIo(AVR_TWBR);
        std::uint8_t twsr = GetIo(AVR_TWSR);
        std::uint8_t twps = static_cast<std::uint8_t>(twsr & 0x03);
        int prescaler = 1;
        switch (twps)
        {
        case 0:
            prescaler = 1;
            break;
        case 1:
            prescaler = 4;
            break;
        case 2:
            prescaler = 16;
            break;
        case 3:
            prescaler = 64;
            break;
        }
        double cycles = 16.0 + 2.0 * static_cast<double>(twbr) * static_cast<double>(prescaler);
        if (cycles < 4.0)
            cycles = 4.0;
        return cycles;
    }

    std::uint32_t VirtualMcu::NextUartErrorSeed(int channel, std::uint8_t data)
    {
        auto &uart = _uarts[static_cast<std::size_t>(channel)];
        uart.errorSeed = uart.errorSeed * 1664525u + 1013904223u;
        std::uint32_t seed = uart.errorSeed ^ static_cast<std::uint32_t>(data);
        seed ^= static_cast<std::uint32_t>(uart.rxCount & 0xFFFFFFFFu);
        seed ^= seed << 13;
        seed ^= seed >> 17;
        seed ^= seed << 5;
        return seed;
    }

    void VirtualMcu::SnapshotPorts(std::uint8_t &portb, std::uint8_t &portc, std::uint8_t &portd,
                                   std::uint8_t &ddrb, std::uint8_t &ddrc, std::uint8_t &ddrd) const
    {
        portb = GetIo(AVR_PORTB);
        portc = GetIo(AVR_PORTC);
        portd = GetIo(AVR_PORTD);
        ddrb = GetIo(AVR_DDRB);
        ddrc = GetIo(AVR_DDRC);
        ddrd = GetIo(AVR_DDRD);
    }

    bool VirtualMcu::ConsumeSerialByte(std::uint8_t &outByte)
    {
        return ConsumeSerialByte(0, outByte);
    }

    bool VirtualMcu::ConsumeSerialByte(int channel, std::uint8_t &outByte)
    {
        if (channel < 0 || channel >= static_cast<int>(_uarts.size()))
            return false;
        auto &uart = _uarts[static_cast<std::size_t>(channel)];
        if (uart.txQueue.empty())
            return false;
        outByte = uart.txQueue.front();
        uart.txQueue.pop_front();
        return true;
    }

    void VirtualMcu::LoadEepromFromFile(const std::string &path)
    {
        if (path.empty())
            return;
        std::ifstream file(path, std::ios::binary);
        if (!file.is_open())
            return;
        file.read(reinterpret_cast<char *>(_state.eeprom.data()), static_cast<std::streamsize>(_state.eeprom.size()));
    }

    void VirtualMcu::SaveEepromToFile(const std::string &path) const
    {
        if (path.empty())
            return;
        std::ofstream file(path, std::ios::binary | std::ios::trunc);
        if (!file.is_open())
            return;
        file.write(reinterpret_cast<const char *>(_state.eeprom.data()), static_cast<std::streamsize>(_state.eeprom.size()));
    }

    void VirtualMcu::SetAnalogInput(int channel, float voltage)
    {
        if (channel < 0 || channel >= static_cast<int>(_analogInputs.size()))
            return;
        _analogInputs[channel] = voltage;
    }

    void VirtualMcu::QueueSerialInput(std::uint8_t value)
    {
        QueueSerialInput(0, value);
    }

    void VirtualMcu::QueueSerialInput(int channel, std::uint8_t value)
    {
        if (!HasUart(channel) || !IsUartRxEnabled(channel))
        {
            return;
        }
        auto &uart = _uarts[static_cast<std::size_t>(channel)];
        if (uart.rxQueue.size() >= _uartQueueLimit)
        {
            uart.rxQueue.pop_front();
            std::uint8_t ucsra = GetIo(UcsrAAddress[channel]);
            ucsra = static_cast<std::uint8_t>(ucsra | (1u << UartDataOverrunBit));
            AVR_IoWrite(&_state.core, UcsrAAddress[channel], ucsra);
        }
        uart.rxQueue.push_back(value);
    }

    bool VirtualMcu::PinToPort(int pin, std::uint16_t &ddr, std::uint16_t &port, std::uint16_t &pinReg, std::uint8_t &bit) const
    {
        if (pin < 0)
            return false;

        if (_profile.mcu == "ATmega2560")
        {
            // Arduino Mega2560 pin map (D0-D53, A0-A15)
            if (pin >= 0 && pin <= 53)
            {
                switch (pin)
                {
                case 0:
                    ddr = AVR_DDRE;
                    port = AVR_PORTE;
                    pinReg = AVR_PINE;
                    bit = 0;
                    return true;
                case 1:
                    ddr = AVR_DDRE;
                    port = AVR_PORTE;
                    pinReg = AVR_PINE;
                    bit = 1;
                    return true;
                case 2:
                    ddr = AVR_DDRE;
                    port = AVR_PORTE;
                    pinReg = AVR_PINE;
                    bit = 4;
                    return true;
                case 3:
                    ddr = AVR_DDRE;
                    port = AVR_PORTE;
                    pinReg = AVR_PINE;
                    bit = 5;
                    return true;
                case 4:
                    ddr = AVR_DDRG;
                    port = AVR_PORTG;
                    pinReg = AVR_PING;
                    bit = 5;
                    return true;
                case 5:
                    ddr = AVR_DDRE;
                    port = AVR_PORTE;
                    pinReg = AVR_PINE;
                    bit = 3;
                    return true;
                case 6:
                    ddr = AVR_DDRH;
                    port = AVR_PORTH;
                    pinReg = AVR_PINH;
                    bit = 3;
                    return true;
                case 7:
                    ddr = AVR_DDRH;
                    port = AVR_PORTH;
                    pinReg = AVR_PINH;
                    bit = 4;
                    return true;
                case 8:
                    ddr = AVR_DDRH;
                    port = AVR_PORTH;
                    pinReg = AVR_PINH;
                    bit = 5;
                    return true;
                case 9:
                    ddr = AVR_DDRH;
                    port = AVR_PORTH;
                    pinReg = AVR_PINH;
                    bit = 6;
                    return true;
                case 10:
                    ddr = AVR_DDRB;
                    port = AVR_PORTB;
                    pinReg = AVR_PINB;
                    bit = 4;
                    return true;
                case 11:
                    ddr = AVR_DDRB;
                    port = AVR_PORTB;
                    pinReg = AVR_PINB;
                    bit = 5;
                    return true;
                case 12:
                    ddr = AVR_DDRB;
                    port = AVR_PORTB;
                    pinReg = AVR_PINB;
                    bit = 6;
                    return true;
                case 13:
                    ddr = AVR_DDRB;
                    port = AVR_PORTB;
                    pinReg = AVR_PINB;
                    bit = 7;
                    return true;
                case 14:
                    ddr = AVR_DDRJ;
                    port = AVR_PORTJ;
                    pinReg = AVR_PINJ;
                    bit = 1;
                    return true;
                case 15:
                    ddr = AVR_DDRJ;
                    port = AVR_PORTJ;
                    pinReg = AVR_PINJ;
                    bit = 0;
                    return true;
                case 16:
                    ddr = AVR_DDRH;
                    port = AVR_PORTH;
                    pinReg = AVR_PINH;
                    bit = 1;
                    return true;
                case 17:
                    ddr = AVR_DDRH;
                    port = AVR_PORTH;
                    pinReg = AVR_PINH;
                    bit = 0;
                    return true;
                case 18:
                    ddr = AVR_DDRD;
                    port = AVR_PORTD;
                    pinReg = AVR_PIND;
                    bit = 3;
                    return true;
                case 19:
                    ddr = AVR_DDRD;
                    port = AVR_PORTD;
                    pinReg = AVR_PIND;
                    bit = 2;
                    return true;
                case 20:
                    ddr = AVR_DDRD;
                    port = AVR_PORTD;
                    pinReg = AVR_PIND;
                    bit = 1;
                    return true;
                case 21:
                    ddr = AVR_DDRD;
                    port = AVR_PORTD;
                    pinReg = AVR_PIND;
                    bit = 0;
                    return true;
                default:
                    break;
                }

                if (pin >= 22 && pin <= 29)
                {
                    ddr = AVR_DDRA;
                    port = AVR_PORTA;
                    pinReg = AVR_PINA;
                    bit = static_cast<std::uint8_t>(pin - 22);
                    return true;
                }
                if (pin >= 30 && pin <= 37)
                {
                    ddr = AVR_DDRC;
                    port = AVR_PORTC;
                    pinReg = AVR_PINC;
                    bit = static_cast<std::uint8_t>(37 - pin);
                    return true;
                }
                if (pin >= 38 && pin <= 41)
                {
                    static const std::uint16_t ports[] = {AVR_DDRD, AVR_DDRG, AVR_DDRG, AVR_DDRG};
                    static const std::uint16_t pinRegs[] = {AVR_PIND, AVR_PING, AVR_PING, AVR_PING};
                    static const std::uint16_t portRegs[] = {AVR_PORTD, AVR_PORTG, AVR_PORTG, AVR_PORTG};
                    static const std::uint8_t bits[] = {7, 2, 1, 0};
                    int idx = pin - 38;
                    ddr = ports[idx];
                    port = portRegs[idx];
                    pinReg = pinRegs[idx];
                    bit = bits[idx];
                    return true;
                }
                if (pin >= 42 && pin <= 49)
                {
                    ddr = AVR_DDRL;
                    port = AVR_PORTL;
                    pinReg = AVR_PINL;
                    bit = static_cast<std::uint8_t>(49 - pin);
                    return true;
                }
                if (pin >= 50 && pin <= 53)
                {
                    ddr = AVR_DDRB;
                    port = AVR_PORTB;
                    pinReg = AVR_PINB;
                    bit = static_cast<std::uint8_t>(53 - pin);
                    return true;
                }
            }

            if (pin >= 54 && pin <= 69)
            {
                int analog = pin - 54;
                if (analog <= 7)
                {
                    ddr = AVR_DDRF;
                    port = AVR_PORTF;
                    pinReg = AVR_PINF;
                    bit = static_cast<std::uint8_t>(analog);
                    return true;
                }
                ddr = AVR_DDRK;
                port = AVR_PORTK;
                pinReg = AVR_PINK;
                bit = static_cast<std::uint8_t>(analog - 8);
                return true;
            }

            return false;
        }

        if (pin <= 7)
        {
            ddr = AVR_DDRD;
            port = AVR_PORTD;
            pinReg = AVR_PIND;
            bit = static_cast<std::uint8_t>(pin);
            return true;
        }
        if (pin <= 13)
        {
            ddr = AVR_DDRB;
            port = AVR_PORTB;
            pinReg = AVR_PINB;
            bit = static_cast<std::uint8_t>(pin - 8);
            return true;
        }
        if (pin <= 19)
        {
            ddr = AVR_DDRC;
            port = AVR_PORTC;
            pinReg = AVR_PINC;
            bit = static_cast<std::uint8_t>(pin - 14);
            return true;
        }
        return false;
    }

    void VirtualMcu::HandleUartWrite(int channel, std::uint8_t value)
    {
        auto &uart = _uarts[static_cast<std::size_t>(channel)];
        if (uart.txQueue.size() >= _uartQueueLimit)
        {
            uart.txQueue.pop_front();
        }
        uart.txQueue.push_back(value);
        _perf.uartTxBytes[static_cast<std::size_t>(channel)]++;
    }

    void VirtualMcu::IoWriteHook(AvrCore *core, std::uint16_t address, std::uint8_t value, void *user)
    {
        (void)core;
        if (!user)
            return;
        auto *self = static_cast<VirtualMcu *>(user);
        if (address == AVR_SPDR)
        {
            std::uint8_t spcr = self->GetIo(AVR_SPCR);
            if (spcr & (1u << 6))
            {
                std::uint8_t spsr = self->GetIo(AVR_SPSR);
                bool spif = (spsr & (1u << 7)) != 0;
                if (self->_spiActive || spif)
                {
                    spsr = static_cast<std::uint8_t>(spsr | (1u << 6));
                    AVR_IoWrite(&self->_state.core, AVR_SPSR, spsr);
                }
                else
                {
                    spsr = static_cast<std::uint8_t>(spsr & ~(1u << 7));
                    spsr = static_cast<std::uint8_t>(spsr & ~(1u << 6));
                    AVR_IoWrite(&self->_state.core, AVR_SPSR, spsr);
                    self->_spiData = value;
                    self->_spiActive = true;
                    self->_spiCyclesRemaining = self->ComputeSpiCyclesPerBit() * 8.0;
                    self->_spiSpsrRead = false;
                }
            }
        }
        if (address == AVR_TWDR)
        {
            std::uint8_t twcr = self->GetIo(AVR_TWCR);
            if (twcr & (1u << 2))
            {
                if (self->_twiActive)
                {
                    twcr = static_cast<std::uint8_t>(twcr | (1u << 3));
                    self->_twiStatus = 0x38;
                    AVR_IoWrite(&self->_state.core, AVR_TWCR, twcr);
                }
                else
                {
                    self->_twiData = value;
                    self->_twiActive = true;
                    self->_twiCyclesRemaining = self->ComputeTwiCyclesPerBit() * 9.0;
                    twcr = static_cast<std::uint8_t>(twcr & ~(1u << 7));
                    AVR_IoWrite(&self->_state.core, AVR_TWCR, twcr);
                }
            }
        }
        if (address == AVR_TWCR)
        {
            std::uint8_t twcr = value;
            if ((twcr & (1u << 2)) != 0)
            {
                if (twcr & (1u << 5))
                {
                    self->_twiStatus = 0x08;
                    twcr = static_cast<std::uint8_t>(twcr & ~(1u << 5));
                    twcr = static_cast<std::uint8_t>(twcr | (1u << 7));
                }
                else if (twcr & (1u << 4))
                {
                    self->_twiStatus = 0x10;
                    twcr = static_cast<std::uint8_t>(twcr & ~(1u << 4));
                    twcr = static_cast<std::uint8_t>(twcr | (1u << 7));
                }
            }
            if (twcr & (1u << 7))
            {
                twcr = static_cast<std::uint8_t>(twcr & ~(1u << 7));
            }
            std::size_t idx = static_cast<std::size_t>(AVR_TWCR - AVR_IO_BASE);
            if (idx < self->_state.io.size())
            {
                self->_state.io[idx] = twcr;
            }
            return;
        }
        if (address == AVR_WDTCSR)
        {
            self->_wdtCyclesRemaining = 0.0;
            self->_wdtResetArmed = false;
        }
        int uartIndex = -1;
        for (int i = 0; i < static_cast<int>(self->_uarts.size()); ++i)
        {
            if (address == UcsrAAddress[i])
            {
                uartIndex = i;
                if (value & (1u << UartTxCompleteBit))
                {
                    std::size_t idx = static_cast<std::size_t>(address - AVR_IO_BASE);
                    if (idx < self->_state.io.size())
                    {
                        self->_state.io[idx] = static_cast<std::uint8_t>(self->_state.io[idx] & ~(1u << UartTxCompleteBit));
                    }
                }
                return;
            }
        }

        for (int i = 0; i < static_cast<int>(self->_uarts.size()); ++i)
        {
            if (address != UdrAddress[i])
            {
                continue;
            }
            uartIndex = i;
            break;
        }
        if (uartIndex < 0 || !self->HasUart(uartIndex))
            return;
        if (!self->IsUartTxEnabled(uartIndex))
        {
            return;
        }
        auto &uart = self->_uarts[static_cast<std::size_t>(uartIndex)];
        if (!uart.txActive && uart.txPending.empty())
        {
            uart.txByte = value;
            uart.txActive = true;
            uart.txCyclesRemaining = self->ComputeUartCyclesPerByte(uartIndex);
            uart.udrEmptyCyclesRemaining = self->ComputeUartCyclesPerBit(uartIndex);
        }
        else if (uart.txPending.empty())
        {
            uart.txPending.push_back(value);
        }
        else
        {
            uart.txPending.back() = value;
        }
        std::uint8_t ucsra = self->GetIo(UcsrAAddress[uartIndex]);
        ucsra = static_cast<std::uint8_t>(ucsra & ~(1u << UartDataRegisterEmptyBit));
        ucsra = static_cast<std::uint8_t>(ucsra & ~(1u << UartTxCompleteBit));
        AVR_IoWrite(&self->_state.core, UcsrAAddress[uartIndex], ucsra);
    }

    void VirtualMcu::IoReadHook(AvrCore *core, std::uint16_t address, std::uint8_t value, void *user)
    {
        (void)core;
        (void)value;
        if (!user)
            return;
        auto *self = static_cast<VirtualMcu *>(user);
        auto readRaw = [&](std::uint16_t addr) -> std::uint8_t
        {
            std::size_t idx = static_cast<std::size_t>(addr - AVR_IO_BASE);
            if (idx < self->_state.io.size())
            {
                return self->_state.io[idx];
            }
            return 0;
        };
        auto writeRaw = [&](std::uint16_t addr, std::uint8_t value)
        {
            std::size_t idx = static_cast<std::size_t>(addr - AVR_IO_BASE);
            if (idx < self->_state.io.size())
            {
                self->_state.io[idx] = value;
            }
        };
        if (address == AVR_SPSR)
        {
            std::uint8_t spsr = readRaw(AVR_SPSR);
            if (spsr & (1u << 7))
            {
                self->_spiSpsrRead = true;
            }
            return;
        }
        if (address == AVR_SPDR)
        {
            if (self->_spiSpsrRead)
            {
                std::uint8_t spsr = readRaw(AVR_SPSR);
                spsr = static_cast<std::uint8_t>(spsr & ~(1u << 7));
                spsr = static_cast<std::uint8_t>(spsr & ~(1u << 6));
                writeRaw(AVR_SPSR, spsr);
                self->_spiSpsrRead = false;
            }
        }
        int uartIndex = -1;
        for (int i = 0; i < static_cast<int>(self->_uarts.size()); ++i)
        {
            if (address == UdrAddress[i])
            {
                uartIndex = i;
                break;
            }
        }
        if (uartIndex < 0 || !self->HasUart(uartIndex))
            return;
        auto &uart = self->_uarts[static_cast<std::size_t>(uartIndex)];
        if (!uart.rxReady)
        {
            return;
        }
        uart.rxReady = false;
        std::uint8_t ucsra = self->GetIo(UcsrAAddress[uartIndex]);
        ucsra = static_cast<std::uint8_t>(ucsra & ~(1u << UartRxCompleteBit));
        ucsra = static_cast<std::uint8_t>(ucsra & ~(1u << UartDataOverrunBit));
        ucsra = static_cast<std::uint8_t>(ucsra & ~(1u << UartFrameErrorBit));
        ucsra = static_cast<std::uint8_t>(ucsra & ~(1u << UartParityErrorBit));

        AVR_IoWrite(&self->_state.core, UcsrAAddress[uartIndex], ucsra);
    }

    void VirtualMcu::SimulateTimer0(std::uint64_t cycles)
    {
        std::uint8_t tccr0a = GetIo(AVR_TCCR0A);
        std::uint8_t tccr0b = GetIo(AVR_TCCR0B);
        std::uint8_t cs = static_cast<std::uint8_t>(tccr0b & 0x07);
        if (cs == 0)
        {
            return;
        }

        int prescaler = 1;
        switch (cs)
        {
        case 1:
            prescaler = 1;
            break;
        case 2:
            prescaler = 8;
            break;
        case 3:
            prescaler = 64;
            break;
        case 4:
            prescaler = 256;
            break;
        case 5:
            prescaler = 1024;
            break;
        default:
            prescaler = 1;
            break;
        }

        double ticksExact = (static_cast<double>(cycles) / prescaler) + _timer0Remainder;
        std::uint64_t ticks = static_cast<std::uint64_t>(ticksExact);
        _timer0Remainder = ticksExact - static_cast<double>(ticks);
        if (ticks == 0)
            return;

        std::uint8_t counter = GetIo(AVR_TCNT0);
        std::uint8_t prev = counter;
        std::uint8_t wgm = static_cast<std::uint8_t>((tccr0a & 0x03) | ((tccr0b & 0x08) >> 1));
        std::uint8_t ocr0a = GetIo(AVR_OCR0A);
        std::uint8_t ocr0b = GetIo(AVR_OCR0B);
        bool phaseCorrect = (wgm == 0x01 || wgm == 0x05);
        bool pwmMode = (wgm == 0x01 || wgm == 0x03 || wgm == 0x05 || wgm == 0x07);
        std::uint8_t top = 0xFF;
        if (wgm == 0x02 || wgm == 0x05 || wgm == 0x07)
        {
            top = ocr0a;
        }
        bool wrapped = false;

        if (phaseCorrect)
        {
            auto advancePhase = [](std::uint8_t start, std::uint64_t steps, bool &up, std::uint8_t top, bool &wrappedOut)
            {
                if (top == 0)
                {
                    wrappedOut = steps > 0;
                    return static_cast<std::uint8_t>(0);
                }
                std::uint32_t period = static_cast<std::uint32_t>(top) * 2u;
                if (period == 0)
                {
                    wrappedOut = false;
                    return start;
                }
                std::uint32_t pos = up ? start : (period - start);
                std::uint32_t move = static_cast<std::uint32_t>(steps % period);
                std::uint32_t newPos = pos + move;
                wrappedOut = steps >= period;
                if (newPos >= period)
                {
                    wrappedOut = true;
                    newPos %= period;
                }
                up = newPos <= top;
                return static_cast<std::uint8_t>(up ? newPos : (period - newPos));
            };
            counter = advancePhase(counter, ticks, _timer0Up, top, wrapped);
        }
        else
        {
            std::uint16_t span = static_cast<std::uint16_t>(top) + 1u;
            std::uint64_t total = static_cast<std::uint64_t>(counter) + ticks;
            counter = static_cast<std::uint8_t>(span == 0 ? 0 : (total % span));
            wrapped = span > 0 && total >= span;
        }

        AVR_IoWrite(&_state.core, AVR_TCNT0, counter);
        if (wrapped)
        {
            std::uint8_t tifr0 = GetIo(AVR_TIFR0);
            tifr0 = static_cast<std::uint8_t>(tifr0 | 0x01);
            AVR_IoWrite(&_state.core, AVR_TIFR0, tifr0);
        }

        auto crossed8 = [](std::uint8_t start, std::uint8_t end, std::uint8_t target)
        {
            if (start <= end)
            {
                return target > start && target <= end;
            }
            return target > start || target <= end;
        };
        auto crossedPhase8 = [](std::uint8_t start, std::uint8_t end, std::uint8_t target)
        {
            if (start == end)
            {
                return start == target;
            }
            if (start < end)
            {
                return target > start && target <= end;
            }
            return target < start && target >= end;
        };
        std::uint32_t period = phaseCorrect ? static_cast<std::uint32_t>(top) * 2u
                                            : static_cast<std::uint32_t>(top) + 1u;
        if (period == 0)
            period = 1;
        bool anyCycle = ticks >= period;
        if (phaseCorrect)
        {
            if (anyCycle || crossedPhase8(prev, counter, ocr0a))
            {
                std::uint8_t tifr0 = GetIo(AVR_TIFR0);
                tifr0 = static_cast<std::uint8_t>(tifr0 | (1u << 1));
                AVR_IoWrite(&_state.core, AVR_TIFR0, tifr0);
            }
            if (anyCycle || crossedPhase8(prev, counter, ocr0b))
            {
                std::uint8_t tifr0 = GetIo(AVR_TIFR0);
                tifr0 = static_cast<std::uint8_t>(tifr0 | (1u << 2));
                AVR_IoWrite(&_state.core, AVR_TIFR0, tifr0);
            }
        }
        else
        {
            if (anyCycle || crossed8(prev, counter, ocr0a))
            {
                std::uint8_t tifr0 = GetIo(AVR_TIFR0);
                tifr0 = static_cast<std::uint8_t>(tifr0 | (1u << 1));
                AVR_IoWrite(&_state.core, AVR_TIFR0, tifr0);
            }
            if (anyCycle || crossed8(prev, counter, ocr0b))
            {
                std::uint8_t tifr0 = GetIo(AVR_TIFR0);
                tifr0 = static_cast<std::uint8_t>(tifr0 | (1u << 2));
                AVR_IoWrite(&_state.core, AVR_TIFR0, tifr0);
            }
        }

        if (!pwmMode)
            return;

        bool com0a = (tccr0a & (1u << 7)) != 0;
        bool com0b = (tccr0a & (1u << 5)) != 0;

        if (_profile.mcu == "ATmega2560")
        {
            std::uint8_t ddrb = GetIo(AVR_DDRB);
            std::uint8_t portb = GetIo(AVR_PORTB);
            std::uint8_t ddrg = GetIo(AVR_DDRG);
            std::uint8_t portg = GetIo(AVR_PORTG);

            if (com0a && (ddrb & (1u << 7)) != 0)
            {
                bool high = counter < ocr0a;
                if (high)
                    portb |= (1u << 7);
                else
                    portb &= static_cast<std::uint8_t>(~(1u << 7));
            }

            if (com0b && (ddrg & (1u << 5)) != 0)
            {
                bool high = counter < ocr0b;
                if (high)
                    portg |= (1u << 5);
                else
                    portg &= static_cast<std::uint8_t>(~(1u << 5));
            }

            AVR_IoWrite(&_state.core, AVR_PORTB, portb);
            AVR_IoWrite(&_state.core, AVR_PORTG, portg);
        }
        else
        {
            std::uint8_t ddrd = GetIo(AVR_DDRD);
            std::uint8_t portd = GetIo(AVR_PORTD);

            if (com0a && (ddrd & (1u << 6)) != 0)
            {
                bool high = counter < ocr0a;
                if (high)
                    portd |= (1u << 6);
                else
                    portd &= static_cast<std::uint8_t>(~(1u << 6));
            }

            if (com0b && (ddrd & (1u << 5)) != 0)
            {
                bool high = counter < ocr0b;
                if (high)
                    portd |= (1u << 5);
                else
                    portd &= static_cast<std::uint8_t>(~(1u << 5));
            }

            AVR_IoWrite(&_state.core, AVR_PORTD, portd);
        }
    }

    void VirtualMcu::SimulateTimer1(std::uint64_t cycles)
    {
        std::uint8_t tccr1a = GetIo(AVR_TCCR1A);
        std::uint8_t tccr1b = GetIo(AVR_TCCR1B);
        std::uint8_t cs = static_cast<std::uint8_t>(tccr1b & 0x07);
        if (cs == 0)
        {
            return;
        }

        int prescaler = 1;
        switch (cs)
        {
        case 1:
            prescaler = 1;
            break;
        case 2:
            prescaler = 8;
            break;
        case 3:
            prescaler = 64;
            break;
        case 4:
            prescaler = 256;
            break;
        case 5:
            prescaler = 1024;
            break;
        default:
            prescaler = 1;
            break;
        }

        double ticksExact = (static_cast<double>(cycles) / prescaler) + _timer1Remainder;
        std::uint64_t ticks = static_cast<std::uint64_t>(ticksExact);
        _timer1Remainder = ticksExact - static_cast<double>(ticks);
        if (ticks == 0)
            return;

        std::uint16_t counter = static_cast<std::uint16_t>(
            static_cast<std::uint16_t>(GetIo(AVR_TCNT1L)) |
            (static_cast<std::uint16_t>(GetIo(AVR_TCNT1H)) << 8));
        std::uint16_t prev = counter;
        std::uint16_t ocr1a = static_cast<std::uint16_t>(
            static_cast<std::uint16_t>(GetIo(AVR_OCR1AL)) |
            (static_cast<std::uint16_t>(GetIo(AVR_OCR1AH)) << 8));
        std::uint16_t ocr1b = static_cast<std::uint16_t>(
            static_cast<std::uint16_t>(GetIo(AVR_OCR1BL)) |
            (static_cast<std::uint16_t>(GetIo(AVR_OCR1BH)) << 8));
        std::uint8_t wgm = static_cast<std::uint8_t>((tccr1a & 0x03) | ((tccr1b & 0x18) >> 1));
        bool phaseCorrect = (wgm == 1 || wgm == 2 || wgm == 3 || wgm == 8 || wgm == 9 || wgm == 10 || wgm == 11);
        bool pwmMode = (wgm == 1 || wgm == 2 || wgm == 3 || wgm == 5 || wgm == 6 || wgm == 7 ||
                        wgm == 8 || wgm == 9 || wgm == 10 || wgm == 11 || wgm == 14 || wgm == 15);
        std::uint16_t top = 0xFFFF;
        switch (wgm)
        {
        case 1:
            top = 0x00FF;
            break;
        case 2:
            top = 0x01FF;
            break;
        case 3:
            top = 0x03FF;
            break;
        case 4:
            top = ocr1a;
            break;
        case 5:
            top = 0x00FF;
            break;
        case 6:
            top = 0x01FF;
            break;
        case 7:
            top = 0x03FF;
            break;
        case 8:
            top = 0xFFFF;
            break;
        case 9:
            top = ocr1a;
            break;
        case 10:
            top = 0xFFFF;
            break;
        case 11:
            top = ocr1a;
            break;
        case 12:
            top = ocr1a;
            break;
        case 14:
            top = 0xFFFF;
            break;
        case 15:
            top = ocr1a;
            break;
        default:
            top = 0xFFFF;
            break;
        }
        bool wrapped = false;

        if (phaseCorrect)
        {
            auto advancePhase = [](std::uint16_t start, std::uint64_t steps, bool &up, std::uint16_t top, bool &wrappedOut)
            {
                std::uint32_t period = static_cast<std::uint32_t>(top) * 2u;
                if (period == 0)
                {
                    wrappedOut = false;
                    return start;
                }
                std::uint32_t pos = up ? start : (period - start);
                std::uint32_t move = static_cast<std::uint32_t>(steps % period);
                std::uint32_t newPos = pos + move;
                wrappedOut = steps >= period;
                if (newPos >= period)
                {
                    wrappedOut = true;
                    newPos %= period;
                }
                up = newPos <= top;
                return static_cast<std::uint16_t>(up ? newPos : (period - newPos));
            };
            counter = advancePhase(counter, ticks, _timer1Up, top, wrapped);
        }
        else
        {
            std::uint32_t span = static_cast<std::uint32_t>(top) + 1u;
            std::uint64_t total = static_cast<std::uint64_t>(counter) + ticks;
            counter = static_cast<std::uint16_t>(span == 0 ? 0 : (total % span));
            wrapped = span > 0 && total >= span;
        }

        AVR_IoWrite(&_state.core, AVR_TCNT1L, static_cast<std::uint8_t>(counter & 0xFF));
        AVR_IoWrite(&_state.core, AVR_TCNT1H, static_cast<std::uint8_t>((counter >> 8) & 0xFF));
        if (wrapped)
        {
            std::uint8_t tifr1 = GetIo(AVR_TIFR1);
            tifr1 = static_cast<std::uint8_t>(tifr1 | 0x01);
            AVR_IoWrite(&_state.core, AVR_TIFR1, tifr1);
        }

        auto crossed16 = [](std::uint16_t start, std::uint16_t end, std::uint16_t target)
        {
            if (start <= end)
            {
                return target > start && target <= end;
            }
            return target > start || target <= end;
        };
        auto crossedPhase16 = [](std::uint16_t start, std::uint16_t end, std::uint16_t target)
        {
            if (start == end)
            {
                return start == target;
            }
            if (start < end)
            {
                return target > start && target <= end;
            }
            return target < start && target >= end;
        };
        std::uint32_t period = phaseCorrect ? static_cast<std::uint32_t>(top) * 2u
                                            : static_cast<std::uint32_t>(top) + 1u;
        if (period == 0)
            period = 1;
        bool anyCycle = ticks >= period;
        if (phaseCorrect)
        {
            if (anyCycle || crossedPhase16(prev, counter, ocr1a))
            {
                std::uint8_t tifr1 = GetIo(AVR_TIFR1);
                tifr1 = static_cast<std::uint8_t>(tifr1 | (1u << 1));
                AVR_IoWrite(&_state.core, AVR_TIFR1, tifr1);
            }
            if (anyCycle || crossedPhase16(prev, counter, ocr1b))
            {
                std::uint8_t tifr1 = GetIo(AVR_TIFR1);
                tifr1 = static_cast<std::uint8_t>(tifr1 | (1u << 2));
                AVR_IoWrite(&_state.core, AVR_TIFR1, tifr1);
            }
        }
        else
        {
            if (anyCycle || crossed16(prev, counter, ocr1a))
            {
                std::uint8_t tifr1 = GetIo(AVR_TIFR1);
                tifr1 = static_cast<std::uint8_t>(tifr1 | (1u << 1));
                AVR_IoWrite(&_state.core, AVR_TIFR1, tifr1);
            }
            if (anyCycle || crossed16(prev, counter, ocr1b))
            {
                std::uint8_t tifr1 = GetIo(AVR_TIFR1);
                tifr1 = static_cast<std::uint8_t>(tifr1 | (1u << 2));
                AVR_IoWrite(&_state.core, AVR_TIFR1, tifr1);
            }
        }

        if (!pwmMode)
            return;

        std::uint8_t ddrb = GetIo(AVR_DDRB);
        std::uint8_t portb = GetIo(AVR_PORTB);
        bool com1a = (tccr1a & (1u << 7)) != 0;
        bool com1b = (tccr1a & (1u << 5)) != 0;

        if (_profile.mcu == "ATmega2560")
        {
            if (com1a && (ddrb & (1u << 5)) != 0)
            {
                bool high = counter < ocr1a;
                if (high)
                    portb |= (1u << 5);
                else
                    portb &= static_cast<std::uint8_t>(~(1u << 5));
            }
            if (com1b && (ddrb & (1u << 6)) != 0)
            {
                bool high = counter < ocr1b;
                if (high)
                    portb |= (1u << 6);
                else
                    portb &= static_cast<std::uint8_t>(~(1u << 6));
            }
        }
        else
        {
            if (com1a && (ddrb & (1u << 1)) != 0)
            {
                bool high = counter < ocr1a;
                if (high)
                    portb |= (1u << 1);
                else
                    portb &= static_cast<std::uint8_t>(~(1u << 1));
            }
            if (com1b && (ddrb & (1u << 2)) != 0)
            {
                bool high = counter < ocr1b;
                if (high)
                    portb |= (1u << 2);
                else
                    portb &= static_cast<std::uint8_t>(~(1u << 2));
            }
        }

        AVR_IoWrite(&_state.core, AVR_PORTB, portb);
    }

    void VirtualMcu::SimulateTimer2(std::uint64_t cycles)
    {
        std::uint8_t tccr2a = GetIo(AVR_TCCR2A);
        std::uint8_t tccr2b = GetIo(AVR_TCCR2B);
        std::uint8_t cs = static_cast<std::uint8_t>(tccr2b & 0x07);
        if (cs == 0)
        {
            return;
        }

        int prescaler = 1;
        switch (cs)
        {
        case 1:
            prescaler = 1;
            break;
        case 2:
            prescaler = 8;
            break;
        case 3:
            prescaler = 32;
            break;
        case 4:
            prescaler = 64;
            break;
        case 5:
            prescaler = 128;
            break;
        case 6:
            prescaler = 256;
            break;
        case 7:
            prescaler = 1024;
            break;
        default:
            prescaler = 1;
            break;
        }

        double ticksExact = (static_cast<double>(cycles) / prescaler) + _timer2Remainder;
        std::uint64_t ticks = static_cast<std::uint64_t>(ticksExact);
        _timer2Remainder = ticksExact - static_cast<double>(ticks);
        if (ticks == 0)
            return;

        std::uint8_t counter = GetIo(AVR_TCNT2);
        std::uint8_t prev = counter;
        std::uint8_t wgm = static_cast<std::uint8_t>((tccr2a & 0x03) | ((tccr2b & 0x08) >> 1));
        std::uint8_t ocr2a = GetIo(AVR_OCR2A);
        std::uint8_t ocr2b = GetIo(AVR_OCR2B);
        bool phaseCorrect = (wgm == 0x01 || wgm == 0x05);
        bool pwmMode = (wgm == 0x01 || wgm == 0x03 || wgm == 0x05 || wgm == 0x07);
        std::uint8_t top = 0xFF;
        if (wgm == 0x02 || wgm == 0x05 || wgm == 0x07)
        {
            top = ocr2a;
        }
        bool wrapped = false;

        if (phaseCorrect)
        {
            auto advancePhase = [](std::uint8_t start, std::uint64_t steps, bool &up, std::uint8_t top, bool &wrappedOut)
            {
                std::uint32_t period = static_cast<std::uint32_t>(top) * 2u;
                if (period == 0)
                {
                    wrappedOut = false;
                    return start;
                }
                std::uint32_t pos = up ? start : (period - start);
                std::uint32_t move = static_cast<std::uint32_t>(steps % period);
                std::uint32_t newPos = pos + move;
                wrappedOut = steps >= period;
                if (newPos >= period)
                {
                    wrappedOut = true;
                    newPos %= period;
                }
                up = newPos <= top;
                return static_cast<std::uint8_t>(up ? newPos : (period - newPos));
            };
            counter = advancePhase(counter, ticks, _timer2Up, top, wrapped);
        }
        else
        {
            std::uint16_t span = static_cast<std::uint16_t>(top) + 1u;
            std::uint64_t total = static_cast<std::uint64_t>(counter) + ticks;
            counter = static_cast<std::uint8_t>(span == 0 ? 0 : (total % span));
            wrapped = span > 0 && total >= span;
        }

        AVR_IoWrite(&_state.core, AVR_TCNT2, counter);
        if (wrapped)
        {
            std::uint8_t tifr2 = GetIo(AVR_TIFR2);
            tifr2 = static_cast<std::uint8_t>(tifr2 | 0x01);
            AVR_IoWrite(&_state.core, AVR_TIFR2, tifr2);
        }

        auto crossed8 = [](std::uint8_t start, std::uint8_t end, std::uint8_t target)
        {
            if (start <= end)
            {
                return target > start && target <= end;
            }
            return target > start || target <= end;
        };
        auto crossedPhase8 = [](std::uint8_t start, std::uint8_t end, std::uint8_t target)
        {
            if (start == end)
            {
                return start == target;
            }
            if (start < end)
            {
                return target > start && target <= end;
            }
            return target < start && target >= end;
        };
        std::uint32_t period = phaseCorrect ? static_cast<std::uint32_t>(top) * 2u
                                            : static_cast<std::uint32_t>(top) + 1u;
        if (period == 0)
            period = 1;
        bool anyCycle = ticks >= period;
        if (phaseCorrect)
        {
            if (anyCycle || crossedPhase8(prev, counter, ocr2a))
            {
                std::uint8_t tifr2 = GetIo(AVR_TIFR2);
                tifr2 = static_cast<std::uint8_t>(tifr2 | (1u << 1));
                AVR_IoWrite(&_state.core, AVR_TIFR2, tifr2);
            }
            if (anyCycle || crossedPhase8(prev, counter, ocr2b))
            {
                std::uint8_t tifr2 = GetIo(AVR_TIFR2);
                tifr2 = static_cast<std::uint8_t>(tifr2 | (1u << 2));
                AVR_IoWrite(&_state.core, AVR_TIFR2, tifr2);
            }
        }
        else
        {
            if (anyCycle || crossed8(prev, counter, ocr2a))
            {
                std::uint8_t tifr2 = GetIo(AVR_TIFR2);
                tifr2 = static_cast<std::uint8_t>(tifr2 | (1u << 1));
                AVR_IoWrite(&_state.core, AVR_TIFR2, tifr2);
            }
            if (anyCycle || crossed8(prev, counter, ocr2b))
            {
                std::uint8_t tifr2 = GetIo(AVR_TIFR2);
                tifr2 = static_cast<std::uint8_t>(tifr2 | (1u << 2));
                AVR_IoWrite(&_state.core, AVR_TIFR2, tifr2);
            }
        }

        if (!pwmMode)
            return;
        bool com2a = (tccr2a & (1u << 7)) != 0;
        bool com2b = (tccr2a & (1u << 5)) != 0;

        if (_profile.mcu == "ATmega2560")
        {
            std::uint8_t ddrb = GetIo(AVR_DDRB);
            std::uint8_t portb = GetIo(AVR_PORTB);
            std::uint8_t ddrh = GetIo(AVR_DDRH);
            std::uint8_t porth = GetIo(AVR_PORTH);

            if (com2a && (ddrb & (1u << 4)) != 0)
            {
                bool high = counter < ocr2a;
                if (high)
                    portb |= (1u << 4);
                else
                    portb &= static_cast<std::uint8_t>(~(1u << 4));
            }

            if (com2b && (ddrh & (1u << 6)) != 0)
            {
                bool high = counter < ocr2b;
                if (high)
                    porth |= (1u << 6);
                else
                    porth &= static_cast<std::uint8_t>(~(1u << 6));
            }

            AVR_IoWrite(&_state.core, AVR_PORTB, portb);
            AVR_IoWrite(&_state.core, AVR_PORTH, porth);
        }
        else
        {
            std::uint8_t ddrb = GetIo(AVR_DDRB);
            std::uint8_t portb = GetIo(AVR_PORTB);
            std::uint8_t ddrd = GetIo(AVR_DDRD);
            std::uint8_t portd = GetIo(AVR_PORTD);

            if (com2a && (ddrb & (1u << 3)) != 0)
            {
                bool high = counter < ocr2a;
                if (high)
                    portb |= (1u << 3);
                else
                    portb &= static_cast<std::uint8_t>(~(1u << 3));
            }

            if (com2b && (ddrd & (1u << 3)) != 0)
            {
                bool high = counter < ocr2b;
                if (high)
                    portd |= (1u << 3);
                else
                    portd &= static_cast<std::uint8_t>(~(1u << 3));
            }

            AVR_IoWrite(&_state.core, AVR_PORTB, portb);
            AVR_IoWrite(&_state.core, AVR_PORTD, portd);
        }
    }

    void VirtualMcu::SimulateTimer3(std::uint64_t cycles)
    {
        std::uint8_t tccr3a = GetIo(AVR_TCCR3A);
        std::uint8_t tccr3b = GetIo(AVR_TCCR3B);
        std::uint8_t cs = static_cast<std::uint8_t>(tccr3b & 0x07);
        if (cs == 0)
        {
            return;
        }

        int prescaler = 1;
        switch (cs)
        {
        case 1:
            prescaler = 1;
            break;
        case 2:
            prescaler = 8;
            break;
        case 3:
            prescaler = 64;
            break;
        case 4:
            prescaler = 256;
            break;
        case 5:
            prescaler = 1024;
            break;
        default:
            prescaler = 1;
            break;
        }

        double ticksExact = (static_cast<double>(cycles) / prescaler) + _timer3Remainder;
        std::uint64_t ticks = static_cast<std::uint64_t>(ticksExact);
        _timer3Remainder = ticksExact - static_cast<double>(ticks);
        if (ticks == 0)
            return;

        std::uint16_t counter = static_cast<std::uint16_t>(
            static_cast<std::uint16_t>(GetIo(AVR_TCNT3L)) |
            (static_cast<std::uint16_t>(GetIo(AVR_TCNT3H)) << 8));
        std::uint16_t prev = counter;
        std::uint16_t ocr3a = static_cast<std::uint16_t>(
            static_cast<std::uint16_t>(GetIo(AVR_OCR3AL)) |
            (static_cast<std::uint16_t>(GetIo(AVR_OCR3AH)) << 8));
        std::uint16_t ocr3b = static_cast<std::uint16_t>(
            static_cast<std::uint16_t>(GetIo(AVR_OCR3BL)) |
            (static_cast<std::uint16_t>(GetIo(AVR_OCR3BH)) << 8));
        std::uint16_t ocr3c = static_cast<std::uint16_t>(
            static_cast<std::uint16_t>(GetIo(AVR_OCR3CL)) |
            (static_cast<std::uint16_t>(GetIo(AVR_OCR3CH)) << 8));
        std::uint8_t wgm = static_cast<std::uint8_t>((tccr3a & 0x03) | ((tccr3b & 0x18) >> 1));
        bool phaseCorrect = (wgm == 1 || wgm == 2 || wgm == 3 || wgm == 8 || wgm == 9 || wgm == 10 || wgm == 11);
        bool pwmMode = (wgm == 1 || wgm == 2 || wgm == 3 || wgm == 5 || wgm == 6 || wgm == 7 ||
                        wgm == 8 || wgm == 9 || wgm == 10 || wgm == 11 || wgm == 14 || wgm == 15);
        std::uint16_t top = 0xFFFF;
        switch (wgm)
        {
        case 1:
            top = 0x00FF;
            break;
        case 2:
            top = 0x01FF;
            break;
        case 3:
            top = 0x03FF;
            break;
        case 4:
            top = ocr3a;
            break;
        case 5:
            top = 0x00FF;
            break;
        case 6:
            top = 0x01FF;
            break;
        case 7:
            top = 0x03FF;
            break;
        case 8:
            top = 0xFFFF;
            break;
        case 9:
            top = ocr3a;
            break;
        case 10:
            top = 0xFFFF;
            break;
        case 11:
            top = ocr3a;
            break;
        case 12:
            top = ocr3a;
            break;
        case 14:
            top = 0xFFFF;
            break;
        case 15:
            top = ocr3a;
            break;
        default:
            top = 0xFFFF;
            break;
        }
        bool wrapped = false;

        if (phaseCorrect)
        {
            auto advancePhase = [](std::uint16_t start, std::uint64_t steps, bool &up, std::uint16_t top, bool &wrappedOut)
            {
                std::uint32_t period = static_cast<std::uint32_t>(top) * 2u;
                if (period == 0)
                {
                    wrappedOut = false;
                    return start;
                }
                std::uint32_t pos = up ? start : (period - start);
                std::uint32_t move = static_cast<std::uint32_t>(steps % period);
                std::uint32_t newPos = pos + move;
                wrappedOut = steps >= period;
                if (newPos >= period)
                {
                    wrappedOut = true;
                    newPos %= period;
                }
                up = newPos <= top;
                return static_cast<std::uint16_t>(up ? newPos : (period - newPos));
            };
            counter = advancePhase(counter, ticks, _timer3Up, top, wrapped);
        }
        else
        {
            std::uint32_t span = static_cast<std::uint32_t>(top) + 1u;
            std::uint64_t total = static_cast<std::uint64_t>(counter) + ticks;
            counter = static_cast<std::uint16_t>(span == 0 ? 0 : (total % span));
            wrapped = span > 0 && total >= span;
        }

        AVR_IoWrite(&_state.core, AVR_TCNT3L, static_cast<std::uint8_t>(counter & 0xFF));
        AVR_IoWrite(&_state.core, AVR_TCNT3H, static_cast<std::uint8_t>((counter >> 8) & 0xFF));
        if (wrapped)
        {
            std::uint8_t tifr3 = GetIo(AVR_TIFR3);
            tifr3 = static_cast<std::uint8_t>(tifr3 | 0x01);
            AVR_IoWrite(&_state.core, AVR_TIFR3, tifr3);
        }

        auto crossed16 = [](std::uint16_t start, std::uint16_t end, std::uint16_t target)
        {
            if (start <= end)
            {
                return target > start && target <= end;
            }
            return target > start || target <= end;
        };
        auto crossedPhase16 = [](std::uint16_t start, std::uint16_t end, std::uint16_t target)
        {
            if (start == end)
            {
                return start == target;
            }
            if (start < end)
            {
                return target > start && target <= end;
            }
            return target < start && target >= end;
        };
        std::uint32_t period = phaseCorrect ? static_cast<std::uint32_t>(top) * 2u
                                            : static_cast<std::uint32_t>(top) + 1u;
        if (period == 0)
            period = 1;
        bool anyCycle = ticks >= period;
        if (phaseCorrect)
        {
            if (anyCycle || crossedPhase16(prev, counter, ocr3a))
            {
                std::uint8_t tifr3 = GetIo(AVR_TIFR3);
                tifr3 = static_cast<std::uint8_t>(tifr3 | (1u << 1));
                AVR_IoWrite(&_state.core, AVR_TIFR3, tifr3);
            }
            if (anyCycle || crossedPhase16(prev, counter, ocr3b))
            {
                std::uint8_t tifr3 = GetIo(AVR_TIFR3);
                tifr3 = static_cast<std::uint8_t>(tifr3 | (1u << 2));
                AVR_IoWrite(&_state.core, AVR_TIFR3, tifr3);
            }
            if (anyCycle || crossedPhase16(prev, counter, ocr3c))
            {
                std::uint8_t tifr3 = GetIo(AVR_TIFR3);
                tifr3 = static_cast<std::uint8_t>(tifr3 | (1u << 3));
                AVR_IoWrite(&_state.core, AVR_TIFR3, tifr3);
            }
        }
        else
        {
            if (anyCycle || crossed16(prev, counter, ocr3a))
            {
                std::uint8_t tifr3 = GetIo(AVR_TIFR3);
                tifr3 = static_cast<std::uint8_t>(tifr3 | (1u << 1));
                AVR_IoWrite(&_state.core, AVR_TIFR3, tifr3);
            }
            if (anyCycle || crossed16(prev, counter, ocr3b))
            {
                std::uint8_t tifr3 = GetIo(AVR_TIFR3);
                tifr3 = static_cast<std::uint8_t>(tifr3 | (1u << 2));
                AVR_IoWrite(&_state.core, AVR_TIFR3, tifr3);
            }
            if (anyCycle || crossed16(prev, counter, ocr3c))
            {
                std::uint8_t tifr3 = GetIo(AVR_TIFR3);
                tifr3 = static_cast<std::uint8_t>(tifr3 | (1u << 3));
                AVR_IoWrite(&_state.core, AVR_TIFR3, tifr3);
            }
        }

        if (!pwmMode)
            return;

        std::uint8_t ddre = GetIo(AVR_DDRE);
        std::uint8_t porte = GetIo(AVR_PORTE);
        bool com3a = (tccr3a & (1u << 7)) != 0;
        bool com3b = (tccr3a & (1u << 5)) != 0;
        bool com3c = (tccr3a & (1u << 3)) != 0;

        if (com3a && (ddre & (1u << 3)) != 0)
        {
            bool high = counter < ocr3a;
            if (high)
                porte |= (1u << 3);
            else
                porte &= static_cast<std::uint8_t>(~(1u << 3));
        }
        if (com3b && (ddre & (1u << 4)) != 0)
        {
            bool high = counter < ocr3b;
            if (high)
                porte |= (1u << 4);
            else
                porte &= static_cast<std::uint8_t>(~(1u << 4));
        }
        if (com3c && (ddre & (1u << 5)) != 0)
        {
            bool high = counter < ocr3c;
            if (high)
                porte |= (1u << 5);
            else
                porte &= static_cast<std::uint8_t>(~(1u << 5));
        }

        AVR_IoWrite(&_state.core, AVR_PORTE, porte);
    }

    void VirtualMcu::SimulateTimer4(std::uint64_t cycles)
    {
        std::uint8_t tccr4a = GetIo(AVR_TCCR4A);
        std::uint8_t tccr4b = GetIo(AVR_TCCR4B);
        std::uint8_t cs = static_cast<std::uint8_t>(tccr4b & 0x07);
        if (cs == 0)
        {
            return;
        }

        int prescaler = 1;
        switch (cs)
        {
        case 1:
            prescaler = 1;
            break;
        case 2:
            prescaler = 8;
            break;
        case 3:
            prescaler = 64;
            break;
        case 4:
            prescaler = 256;
            break;
        case 5:
            prescaler = 1024;
            break;
        default:
            prescaler = 1;
            break;
        }

        double ticksExact = (static_cast<double>(cycles) / prescaler) + _timer4Remainder;
        std::uint64_t ticks = static_cast<std::uint64_t>(ticksExact);
        _timer4Remainder = ticksExact - static_cast<double>(ticks);
        if (ticks == 0)
            return;

        std::uint16_t counter = static_cast<std::uint16_t>(
            static_cast<std::uint16_t>(GetIo(AVR_TCNT4L)) |
            (static_cast<std::uint16_t>(GetIo(AVR_TCNT4H)) << 8));
        std::uint16_t prev = counter;
        std::uint16_t ocr4a = static_cast<std::uint16_t>(
            static_cast<std::uint16_t>(GetIo(AVR_OCR4AL)) |
            (static_cast<std::uint16_t>(GetIo(AVR_OCR4AH)) << 8));
        std::uint16_t ocr4b = static_cast<std::uint16_t>(
            static_cast<std::uint16_t>(GetIo(AVR_OCR4BL)) |
            (static_cast<std::uint16_t>(GetIo(AVR_OCR4BH)) << 8));
        std::uint16_t ocr4c = static_cast<std::uint16_t>(
            static_cast<std::uint16_t>(GetIo(AVR_OCR4CL)) |
            (static_cast<std::uint16_t>(GetIo(AVR_OCR4CH)) << 8));
        std::uint8_t wgm = static_cast<std::uint8_t>((tccr4a & 0x03) | ((tccr4b & 0x18) >> 1));
        bool phaseCorrect = (wgm == 1 || wgm == 2 || wgm == 3 || wgm == 8 || wgm == 9 || wgm == 10 || wgm == 11);
        bool pwmMode = (wgm == 1 || wgm == 2 || wgm == 3 || wgm == 5 || wgm == 6 || wgm == 7 ||
                        wgm == 8 || wgm == 9 || wgm == 10 || wgm == 11 || wgm == 14 || wgm == 15);
        std::uint16_t top = 0xFFFF;
        switch (wgm)
        {
        case 1:
            top = 0x00FF;
            break;
        case 2:
            top = 0x01FF;
            break;
        case 3:
            top = 0x03FF;
            break;
        case 4:
            top = ocr4a;
            break;
        case 5:
            top = 0x00FF;
            break;
        case 6:
            top = 0x01FF;
            break;
        case 7:
            top = 0x03FF;
            break;
        case 8:
            top = 0xFFFF;
            break;
        case 9:
            top = ocr4a;
            break;
        case 10:
            top = 0xFFFF;
            break;
        case 11:
            top = ocr4a;
            break;
        case 12:
            top = ocr4a;
            break;
        case 14:
            top = 0xFFFF;
            break;
        case 15:
            top = ocr4a;
            break;
        default:
            top = 0xFFFF;
            break;
        }
        bool wrapped = false;

        if (phaseCorrect)
        {
            auto advancePhase = [](std::uint16_t start, std::uint64_t steps, bool &up, std::uint16_t top, bool &wrappedOut)
            {
                std::uint32_t period = static_cast<std::uint32_t>(top) * 2u;
                if (period == 0)
                {
                    wrappedOut = false;
                    return start;
                }
                std::uint32_t pos = up ? start : (period - start);
                std::uint32_t move = static_cast<std::uint32_t>(steps % period);
                std::uint32_t newPos = pos + move;
                wrappedOut = steps >= period;
                if (newPos >= period)
                {
                    wrappedOut = true;
                    newPos %= period;
                }
                up = newPos <= top;
                return static_cast<std::uint16_t>(up ? newPos : (period - newPos));
            };
            counter = advancePhase(counter, ticks, _timer4Up, top, wrapped);
        }
        else
        {
            std::uint32_t span = static_cast<std::uint32_t>(top) + 1u;
            std::uint64_t total = static_cast<std::uint64_t>(counter) + ticks;
            counter = static_cast<std::uint16_t>(span == 0 ? 0 : (total % span));
            wrapped = span > 0 && total >= span;
        }

        AVR_IoWrite(&_state.core, AVR_TCNT4L, static_cast<std::uint8_t>(counter & 0xFF));
        AVR_IoWrite(&_state.core, AVR_TCNT4H, static_cast<std::uint8_t>((counter >> 8) & 0xFF));
        if (wrapped)
        {
            std::uint8_t tifr4 = GetIo(AVR_TIFR4);
            tifr4 = static_cast<std::uint8_t>(tifr4 | 0x01);
            AVR_IoWrite(&_state.core, AVR_TIFR4, tifr4);
        }

        auto crossed16 = [](std::uint16_t start, std::uint16_t end, std::uint16_t target)
        {
            if (start <= end)
            {
                return target > start && target <= end;
            }
            return target > start || target <= end;
        };
        auto crossedPhase16 = [](std::uint16_t start, std::uint16_t end, std::uint16_t target)
        {
            if (start == end)
            {
                return start == target;
            }
            if (start < end)
            {
                return target > start && target <= end;
            }
            return target < start && target >= end;
        };
        std::uint32_t period = phaseCorrect ? static_cast<std::uint32_t>(top) * 2u
                                            : static_cast<std::uint32_t>(top) + 1u;
        if (period == 0)
            period = 1;
        bool anyCycle = ticks >= period;
        if (phaseCorrect)
        {
            if (anyCycle || crossedPhase16(prev, counter, ocr4a))
            {
                std::uint8_t tifr4 = GetIo(AVR_TIFR4);
                tifr4 = static_cast<std::uint8_t>(tifr4 | (1u << 1));
                AVR_IoWrite(&_state.core, AVR_TIFR4, tifr4);
            }
            if (anyCycle || crossedPhase16(prev, counter, ocr4b))
            {
                std::uint8_t tifr4 = GetIo(AVR_TIFR4);
                tifr4 = static_cast<std::uint8_t>(tifr4 | (1u << 2));
                AVR_IoWrite(&_state.core, AVR_TIFR4, tifr4);
            }
            if (anyCycle || crossedPhase16(prev, counter, ocr4c))
            {
                std::uint8_t tifr4 = GetIo(AVR_TIFR4);
                tifr4 = static_cast<std::uint8_t>(tifr4 | (1u << 3));
                AVR_IoWrite(&_state.core, AVR_TIFR4, tifr4);
            }
        }
        else
        {
            if (anyCycle || crossed16(prev, counter, ocr4a))
            {
                std::uint8_t tifr4 = GetIo(AVR_TIFR4);
                tifr4 = static_cast<std::uint8_t>(tifr4 | (1u << 1));
                AVR_IoWrite(&_state.core, AVR_TIFR4, tifr4);
            }
            if (anyCycle || crossed16(prev, counter, ocr4b))
            {
                std::uint8_t tifr4 = GetIo(AVR_TIFR4);
                tifr4 = static_cast<std::uint8_t>(tifr4 | (1u << 2));
                AVR_IoWrite(&_state.core, AVR_TIFR4, tifr4);
            }
            if (anyCycle || crossed16(prev, counter, ocr4c))
            {
                std::uint8_t tifr4 = GetIo(AVR_TIFR4);
                tifr4 = static_cast<std::uint8_t>(tifr4 | (1u << 3));
                AVR_IoWrite(&_state.core, AVR_TIFR4, tifr4);
            }
        }

        if (!pwmMode)
            return;

        std::uint8_t ddrh = GetIo(AVR_DDRH);
        std::uint8_t porth = GetIo(AVR_PORTH);
        bool com4a = (tccr4a & (1u << 7)) != 0;
        bool com4b = (tccr4a & (1u << 5)) != 0;
        bool com4c = (tccr4a & (1u << 3)) != 0;

        if (com4a && (ddrh & (1u << 3)) != 0)
        {
            bool high = counter < ocr4a;
            if (high)
                porth |= (1u << 3);
            else
                porth &= static_cast<std::uint8_t>(~(1u << 3));
        }
        if (com4b && (ddrh & (1u << 4)) != 0)
        {
            bool high = counter < ocr4b;
            if (high)
                porth |= (1u << 4);
            else
                porth &= static_cast<std::uint8_t>(~(1u << 4));
        }
        if (com4c && (ddrh & (1u << 5)) != 0)
        {
            bool high = counter < ocr4c;
            if (high)
                porth |= (1u << 5);
            else
                porth &= static_cast<std::uint8_t>(~(1u << 5));
        }

        AVR_IoWrite(&_state.core, AVR_PORTH, porth);
    }

    void VirtualMcu::SimulateTimer5(std::uint64_t cycles)
    {
        std::uint8_t tccr5a = GetIo(AVR_TCCR5A);
        std::uint8_t tccr5b = GetIo(AVR_TCCR5B);
        std::uint8_t cs = static_cast<std::uint8_t>(tccr5b & 0x07);
        if (cs == 0)
        {
            return;
        }

        int prescaler = 1;
        switch (cs)
        {
        case 1:
            prescaler = 1;
            break;
        case 2:
            prescaler = 8;
            break;
        case 3:
            prescaler = 64;
            break;
        case 4:
            prescaler = 256;
            break;
        case 5:
            prescaler = 1024;
            break;
        default:
            prescaler = 1;
            break;
        }

        double ticksExact = (static_cast<double>(cycles) / prescaler) + _timer5Remainder;
        std::uint64_t ticks = static_cast<std::uint64_t>(ticksExact);
        _timer5Remainder = ticksExact - static_cast<double>(ticks);
        if (ticks == 0)
            return;

        std::uint16_t counter = static_cast<std::uint16_t>(
            static_cast<std::uint16_t>(GetIo(AVR_TCNT5L)) |
            (static_cast<std::uint16_t>(GetIo(AVR_TCNT5H)) << 8));
        std::uint16_t prev = counter;
        std::uint16_t ocr5a = static_cast<std::uint16_t>(
            static_cast<std::uint16_t>(GetIo(AVR_OCR5AL)) |
            (static_cast<std::uint16_t>(GetIo(AVR_OCR5AH)) << 8));
        std::uint16_t ocr5b = static_cast<std::uint16_t>(
            static_cast<std::uint16_t>(GetIo(AVR_OCR5BL)) |
            (static_cast<std::uint16_t>(GetIo(AVR_OCR5BH)) << 8));
        std::uint16_t ocr5c = static_cast<std::uint16_t>(
            static_cast<std::uint16_t>(GetIo(AVR_OCR5CL)) |
            (static_cast<std::uint16_t>(GetIo(AVR_OCR5CH)) << 8));
        std::uint8_t wgm = static_cast<std::uint8_t>((tccr5a & 0x03) | ((tccr5b & 0x18) >> 1));
        bool phaseCorrect = (wgm == 1 || wgm == 2 || wgm == 3 || wgm == 8 || wgm == 9 || wgm == 10 || wgm == 11);
        bool pwmMode = (wgm == 1 || wgm == 2 || wgm == 3 || wgm == 5 || wgm == 6 || wgm == 7 ||
                        wgm == 8 || wgm == 9 || wgm == 10 || wgm == 11 || wgm == 14 || wgm == 15);
        std::uint16_t top = 0xFFFF;
        switch (wgm)
        {
        case 1:
            top = 0x00FF;
            break;
        case 2:
            top = 0x01FF;
            break;
        case 3:
            top = 0x03FF;
            break;
        case 4:
            top = ocr5a;
            break;
        case 5:
            top = 0x00FF;
            break;
        case 6:
            top = 0x01FF;
            break;
        case 7:
            top = 0x03FF;
            break;
        case 8:
            top = 0xFFFF;
            break;
        case 9:
            top = ocr5a;
            break;
        case 10:
            top = 0xFFFF;
            break;
        case 11:
            top = ocr5a;
            break;
        case 12:
            top = ocr5a;
            break;
        case 14:
            top = 0xFFFF;
            break;
        case 15:
            top = ocr5a;
            break;
        default:
            top = 0xFFFF;
            break;
        }
        bool wrapped = false;

        if (phaseCorrect)
        {
            auto advancePhase = [](std::uint16_t start, std::uint64_t steps, bool &up, std::uint16_t top, bool &wrappedOut)
            {
                std::uint32_t period = static_cast<std::uint32_t>(top) * 2u;
                if (period == 0)
                {
                    wrappedOut = false;
                    return start;
                }
                std::uint32_t pos = up ? start : (period - start);
                std::uint32_t move = static_cast<std::uint32_t>(steps % period);
                std::uint32_t newPos = pos + move;
                wrappedOut = steps >= period;
                if (newPos >= period)
                {
                    wrappedOut = true;
                    newPos %= period;
                }
                up = newPos <= top;
                return static_cast<std::uint16_t>(up ? newPos : (period - newPos));
            };
            counter = advancePhase(counter, ticks, _timer5Up, top, wrapped);
        }
        else
        {
            std::uint32_t span = static_cast<std::uint32_t>(top) + 1u;
            std::uint64_t total = static_cast<std::uint64_t>(counter) + ticks;
            counter = static_cast<std::uint16_t>(span == 0 ? 0 : (total % span));
            wrapped = span > 0 && total >= span;
        }

        AVR_IoWrite(&_state.core, AVR_TCNT5L, static_cast<std::uint8_t>(counter & 0xFF));
        AVR_IoWrite(&_state.core, AVR_TCNT5H, static_cast<std::uint8_t>((counter >> 8) & 0xFF));
        if (wrapped)
        {
            std::uint8_t tifr5 = GetIo(AVR_TIFR5);
            tifr5 = static_cast<std::uint8_t>(tifr5 | 0x01);
            AVR_IoWrite(&_state.core, AVR_TIFR5, tifr5);
        }

        auto crossed16 = [](std::uint16_t start, std::uint16_t end, std::uint16_t target)
        {
            if (start <= end)
            {
                return target > start && target <= end;
            }
            return target > start || target <= end;
        };
        auto crossedPhase16 = [](std::uint16_t start, std::uint16_t end, std::uint16_t target)
        {
            if (start == end)
            {
                return start == target;
            }
            if (start < end)
            {
                return target > start && target <= end;
            }
            return target < start && target >= end;
        };
        std::uint32_t period = phaseCorrect ? static_cast<std::uint32_t>(top) * 2u
                                            : static_cast<std::uint32_t>(top) + 1u;
        if (period == 0)
            period = 1;
        bool anyCycle = ticks >= period;
        if (phaseCorrect)
        {
            if (anyCycle || crossedPhase16(prev, counter, ocr5a))
            {
                std::uint8_t tifr5 = GetIo(AVR_TIFR5);
                tifr5 = static_cast<std::uint8_t>(tifr5 | (1u << 1));
                AVR_IoWrite(&_state.core, AVR_TIFR5, tifr5);
            }
            if (anyCycle || crossedPhase16(prev, counter, ocr5b))
            {
                std::uint8_t tifr5 = GetIo(AVR_TIFR5);
                tifr5 = static_cast<std::uint8_t>(tifr5 | (1u << 2));
                AVR_IoWrite(&_state.core, AVR_TIFR5, tifr5);
            }
            if (anyCycle || crossedPhase16(prev, counter, ocr5c))
            {
                std::uint8_t tifr5 = GetIo(AVR_TIFR5);
                tifr5 = static_cast<std::uint8_t>(tifr5 | (1u << 3));
                AVR_IoWrite(&_state.core, AVR_TIFR5, tifr5);
            }
        }
        else
        {
            if (anyCycle || crossed16(prev, counter, ocr5a))
            {
                std::uint8_t tifr5 = GetIo(AVR_TIFR5);
                tifr5 = static_cast<std::uint8_t>(tifr5 | (1u << 1));
                AVR_IoWrite(&_state.core, AVR_TIFR5, tifr5);
            }
            if (anyCycle || crossed16(prev, counter, ocr5b))
            {
                std::uint8_t tifr5 = GetIo(AVR_TIFR5);
                tifr5 = static_cast<std::uint8_t>(tifr5 | (1u << 2));
                AVR_IoWrite(&_state.core, AVR_TIFR5, tifr5);
            }
            if (anyCycle || crossed16(prev, counter, ocr5c))
            {
                std::uint8_t tifr5 = GetIo(AVR_TIFR5);
                tifr5 = static_cast<std::uint8_t>(tifr5 | (1u << 3));
                AVR_IoWrite(&_state.core, AVR_TIFR5, tifr5);
            }
        }

        if (!pwmMode)
            return;

        std::uint8_t ddrl = GetIo(AVR_DDRL);
        std::uint8_t portl = GetIo(AVR_PORTL);
        bool com5a = (tccr5a & (1u << 7)) != 0;
        bool com5b = (tccr5a & (1u << 5)) != 0;
        bool com5c = (tccr5a & (1u << 3)) != 0;

        if (com5a && (ddrl & (1u << 3)) != 0)
        {
            bool high = counter < ocr5a;
            if (high)
                portl |= (1u << 3);
            else
                portl &= static_cast<std::uint8_t>(~(1u << 3));
        }
        if (com5b && (ddrl & (1u << 4)) != 0)
        {
            bool high = counter < ocr5b;
            if (high)
                portl |= (1u << 4);
            else
                portl &= static_cast<std::uint8_t>(~(1u << 4));
        }
        if (com5c && (ddrl & (1u << 5)) != 0)
        {
            bool high = counter < ocr5c;
            if (high)
                portl |= (1u << 5);
            else
                portl &= static_cast<std::uint8_t>(~(1u << 5));
        }

        AVR_IoWrite(&_state.core, AVR_PORTL, portl);
    }

    bool VirtualMcu::MeasureHexMaxAddress(const std::string &hexText, std::size_t &outMax)
    {
        outMax = 0;
        std::uint32_t upper = 0;
        const char *line = hexText.c_str();
        while (*line != '\0')
        {
            if (*line == '\r' || *line == '\n')
            {
                ++line;
                continue;
            }
            if (*line != ':')
                return false;
            const char *ptr = line + 1;
            std::uint8_t len = 0;
            std::uint8_t addr_hi = 0;
            std::uint8_t addr_lo = 0;
            std::uint8_t type = 0;
            if (!NativeEngine::Utils::HexLoader::ParseHexByte(ptr, len))
                return false;
            ptr += 2;
            if (!NativeEngine::Utils::HexLoader::ParseHexByte(ptr, addr_hi))
                return false;
            ptr += 2;
            if (!NativeEngine::Utils::HexLoader::ParseHexByte(ptr, addr_lo))
                return false;
            ptr += 2;
            if (!NativeEngine::Utils::HexLoader::ParseHexByte(ptr, type))
                return false;
            ptr += 2;
            std::uint32_t addr = (static_cast<std::uint32_t>(addr_hi) << 8) | addr_lo;

            if (type == 0x00)
            {
                std::uint32_t final_addr = (upper << 16) + addr + len;
                if (final_addr > outMax)
                {
                    outMax = final_addr;
                }
            }
            else if (type == 0x04)
            {
                std::uint8_t up_hi = 0, up_lo = 0;
                if (!NativeEngine::Utils::HexLoader::ParseHexByte(ptr, up_hi))
                    return false;
                ptr += 2;
                if (!NativeEngine::Utils::HexLoader::ParseHexByte(ptr, up_lo))
                    return false;
                ptr += 2;
                upper = (static_cast<std::uint32_t>(up_hi) << 8) | up_lo;
            }

            while (*ptr != '\0' && *ptr != '\n' && *ptr != '\r')
            {
                ++ptr;
            }
            line = ptr;
            if (type == 0x01)
                break;
        }
        return true;
    }
}
