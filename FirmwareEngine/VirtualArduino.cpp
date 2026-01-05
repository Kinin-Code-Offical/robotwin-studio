#ifndef __NO_FLOAT128__
#define __NO_FLOAT128__
#endif

#include "VirtualArduino.h"

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
        constexpr std::uint8_t Udr0Address = AVR_UDR0;
        constexpr std::uint8_t Ucsr0aAddress = AVR_UCSR0A;
        constexpr std::uint8_t UartRxCompleteBit = 7;
        constexpr std::uint8_t UartTxCompleteBit = 6;
        constexpr std::uint8_t UartDataRegisterEmptyBit = 5;
        constexpr std::uint8_t UartDataOverrunBit = 3;

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

    VirtualArduino::VirtualArduino(const BoardProfile &profile)
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
        _analogInputs.assign(AnalogCount, 0.0f);
        Reset();
    }

    void VirtualArduino::Reset()
    {
        std::fill(_state.flash.begin(), _state.flash.end(), 0);
        std::fill(_state.sram.begin(), _state.sram.end(), 0);
        // EEPROM persists across resets by design.
        std::fill(_state.io.begin(), _state.io.end(), 0);
        std::fill(_state.regs.begin(), _state.regs.end(), 0);
        _tickCount = 0;
        _uartTxQueue.clear();
        _uartTxPending.clear();
        _uartTxActive = false;
        _uartTxCyclesRemaining = 0.0;
        _uartRxReady = false;
        _uartRxCyclesRemaining = 0.0;
        _adcNoiseSeed = static_cast<std::uint32_t>(_tickCount ^ 0x9E3779B9u);

        AVR_Init(&_state.core,
                 _state.flash.data(), _state.flash.size(),
                 _state.sram.data(), _state.sram.size(),
                 _state.io.data(), _state.io.size(),
                 _state.regs.data(), _state.regs.size());
        AVR_SetIoWriteHook(&_state.core, IoWriteHook, this);
        AVR_SetIoReadHook(&_state.core, IoReadHook, this);
        std::uint8_t ucsr0a = static_cast<std::uint8_t>((1u << UartDataRegisterEmptyBit));
        AVR_IoWrite(&_state.core, Ucsr0aAddress, ucsr0a);
    }

    bool VirtualArduino::LoadBvm(const std::vector<std::uint8_t> &buffer, std::string &error)
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

    bool VirtualArduino::ParseBvmText(const std::vector<std::uint8_t> &buffer, const std::uint8_t *&data,
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

    bool VirtualArduino::LoadTextSection(const std::uint8_t *data, std::size_t size, std::uint64_t flags, std::string &error)
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

    void VirtualArduino::StepCycles(std::uint64_t cycles)
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
            SimulateTimer0(executed);
            SimulateTimer1(executed);
            SimulateTimer2(executed);
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
                std::uint8_t channel = static_cast<std::uint8_t>(admux & 0x0F);
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
        double cyclesPerByte = ComputeUartCyclesPerByte();

        if (!IsUartTxEnabled())
        {
            _uartTxActive = false;
            _uartTxCyclesRemaining = 0.0;
            std::uint8_t ucsr0a = GetIo(Ucsr0aAddress);
            ucsr0a = static_cast<std::uint8_t>(ucsr0a | (1u << UartDataRegisterEmptyBit));
            AVR_IoWrite(&_state.core, Ucsr0aAddress, ucsr0a);
        }
        else
        {
            double txElapsed = elapsed;
            while (txElapsed > 0.0)
            {
                if (_uartTxActive)
                {
                    if (_uartTxCyclesRemaining > txElapsed)
                    {
                        _uartTxCyclesRemaining -= txElapsed;
                        txElapsed = 0.0;
                        break;
                    }

                    txElapsed -= _uartTxCyclesRemaining;
                    _uartTxCyclesRemaining = 0.0;
                    _uartTxActive = false;
                    HandleUartWrite(_uartTxByte);
                    std::uint8_t ucsr0a = GetIo(Ucsr0aAddress);
                    if (_uartTxPending.empty())
                    {
                        ucsr0a = static_cast<std::uint8_t>(ucsr0a | (1u << UartTxCompleteBit));
                    }
                    AVR_IoWrite(&_state.core, Ucsr0aAddress, ucsr0a);
                }

                if (!_uartTxActive && !_uartTxPending.empty())
                {
                    _uartTxByte = _uartTxPending.front();
                    _uartTxPending.pop_front();
                    _uartTxActive = true;
                    _uartTxCyclesRemaining = cyclesPerByte;
                    std::uint8_t ucsr0a = GetIo(Ucsr0aAddress);
                    ucsr0a = static_cast<std::uint8_t>(ucsr0a & ~(1u << UartTxCompleteBit));
                    AVR_IoWrite(&_state.core, Ucsr0aAddress, ucsr0a);
                    continue;
                }

                break;
            }

            std::uint8_t ucsr0a = GetIo(Ucsr0aAddress);
            if (_uartTxPending.empty())
            {
                ucsr0a = static_cast<std::uint8_t>(ucsr0a | (1u << UartDataRegisterEmptyBit));
            }
            else
            {
                ucsr0a = static_cast<std::uint8_t>(ucsr0a & ~(1u << UartDataRegisterEmptyBit));
            }
            AVR_IoWrite(&_state.core, Ucsr0aAddress, ucsr0a);
        }

        if (!IsUartRxEnabled())
        {
            _uartRxReady = false;
            _uartRxCyclesRemaining = 0.0;
            _uartRxQueue.clear();
            std::uint8_t ucsr0a = GetIo(Ucsr0aAddress);
            ucsr0a = static_cast<std::uint8_t>(ucsr0a & ~(1u << UartRxCompleteBit));
            AVR_IoWrite(&_state.core, Ucsr0aAddress, ucsr0a);
        }
        else if (!_uartRxReady)
        {
            double rxElapsed = elapsed;
            while (rxElapsed > 0.0 && !_uartRxReady)
            {
                if (_uartRxCyclesRemaining <= 0.0)
                {
                    if (_uartRxQueue.empty())
                    {
                        break;
                    }
                    _uartRxCyclesRemaining = cyclesPerByte;
                }

                if (_uartRxCyclesRemaining > rxElapsed)
                {
                    _uartRxCyclesRemaining -= rxElapsed;
                    rxElapsed = 0.0;
                    break;
                }

                rxElapsed -= _uartRxCyclesRemaining;
                _uartRxCyclesRemaining = 0.0;
                if (!_uartRxQueue.empty())
                {
                    std::uint8_t next = _uartRxQueue.front();
                    _uartRxQueue.pop_front();
                    AVR_IoWrite(&_state.core, Udr0Address, next);
                    _uartRxReady = true;
                    std::uint8_t ucsr0a = GetIo(Ucsr0aAddress);
                    ucsr0a = static_cast<std::uint8_t>(ucsr0a | (1u << UartRxCompleteBit));
                    AVR_IoWrite(&_state.core, Ucsr0aAddress, ucsr0a);
                }
            }
        }
    }

    void VirtualArduino::SetInputPin(int pin, int value)
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

    void VirtualArduino::SyncInputs()
    {
        std::uint8_t ddrb = GetIo(AVR_DDRB);
        std::uint8_t ddrc = GetIo(AVR_DDRC);
        std::uint8_t ddrd = GetIo(AVR_DDRD);
        std::uint8_t portb = GetIo(AVR_PORTB);
        std::uint8_t portc = GetIo(AVR_PORTC);
        std::uint8_t portd = GetIo(AVR_PORTD);

        std::uint8_t pinb = 0;
        for (int bit = 0; bit < 6; ++bit)
        {
            bool isOutput = (ddrb & (1u << bit)) != 0;
            int inputValue = ((8 + bit) < _pinCount) ? _pinInputs[8 + bit] : -1;
            bool value = isOutput ? ((portb & (1u << bit)) != 0)
                                  : (inputValue >= 0 ? (inputValue != 0) : ((portb & (1u << bit)) != 0));
            if (value)
                pinb |= (1u << bit);
        }

        std::uint8_t pinc = 0;
        for (int bit = 0; bit < 6; ++bit)
        {
            bool isOutput = (ddrc & (1u << bit)) != 0;
            int inputValue = ((14 + bit) < _pinCount) ? _pinInputs[14 + bit] : -1;
            bool value = isOutput ? ((portc & (1u << bit)) != 0)
                                  : (inputValue >= 0 ? (inputValue != 0) : ((portc & (1u << bit)) != 0));
            if (value)
                pinc |= (1u << bit);
        }

        std::uint8_t pind = 0;
        for (int bit = 0; bit < 8; ++bit)
        {
            bool isOutput = (ddrd & (1u << bit)) != 0;
            int inputValue = (bit < _pinCount) ? _pinInputs[bit] : -1;
            bool value = isOutput ? ((portd & (1u << bit)) != 0)
                                  : (inputValue >= 0 ? (inputValue != 0) : ((portd & (1u << bit)) != 0));
            if (value)
                pind |= (1u << bit);
        }

        AVR_IoWrite(&_state.core, AVR_PINB, pinb);
        AVR_IoWrite(&_state.core, AVR_PINC, pinc);
        AVR_IoWrite(&_state.core, AVR_PIND, pind);
    }

    std::uint8_t VirtualArduino::GetIo(std::uint8_t address) const
    {
        return AVR_IoRead(const_cast<AvrCore *>(&_state.core), address);
    }

    bool VirtualArduino::IsUartRxEnabled() const
    {
        std::uint8_t ucsr0b = GetIo(AVR_UCSR0B);
        return (ucsr0b & (1u << 4)) != 0;
    }

    bool VirtualArduino::IsUartTxEnabled() const
    {
        std::uint8_t ucsr0b = GetIo(AVR_UCSR0B);
        return (ucsr0b & (1u << 3)) != 0;
    }

    double VirtualArduino::ComputeUartCyclesPerByte() const
    {
        std::uint8_t ucsr0a = GetIo(AVR_UCSR0A);
        bool u2x = (ucsr0a & (1u << 1)) != 0;
        std::uint16_t ubrr = static_cast<std::uint16_t>(
            static_cast<std::uint16_t>(GetIo(AVR_UBRR0H)) << 8 |
            static_cast<std::uint16_t>(GetIo(AVR_UBRR0L)));
        double divisor = u2x ? 8.0 : 16.0;
        double cyclesPerBit = divisor * static_cast<double>(ubrr + 1);
        double cyclesPerByte = cyclesPerBit * 10.0;
        if (cyclesPerByte < 1.0)
        {
            cyclesPerByte = 1.0;
        }
        return cyclesPerByte;
    }

    void VirtualArduino::SnapshotPorts(std::uint8_t &portb, std::uint8_t &portc, std::uint8_t &portd,
                                       std::uint8_t &ddrb, std::uint8_t &ddrc, std::uint8_t &ddrd) const
    {
        portb = GetIo(AVR_PORTB);
        portc = GetIo(AVR_PORTC);
        portd = GetIo(AVR_PORTD);
        ddrb = GetIo(AVR_DDRB);
        ddrc = GetIo(AVR_DDRC);
        ddrd = GetIo(AVR_DDRD);
    }

    bool VirtualArduino::ConsumeSerialByte(std::uint8_t &outByte)
    {
        if (_uartTxQueue.empty())
            return false;
        outByte = _uartTxQueue.front();
        _uartTxQueue.pop_front();
        return true;
    }

    void VirtualArduino::LoadEepromFromFile(const std::string &path)
    {
        if (path.empty())
            return;
        std::ifstream file(path, std::ios::binary);
        if (!file.is_open())
            return;
        file.read(reinterpret_cast<char *>(_state.eeprom.data()), static_cast<std::streamsize>(_state.eeprom.size()));
    }

    void VirtualArduino::SaveEepromToFile(const std::string &path) const
    {
        if (path.empty())
            return;
        std::ofstream file(path, std::ios::binary | std::ios::trunc);
        if (!file.is_open())
            return;
        file.write(reinterpret_cast<const char *>(_state.eeprom.data()), static_cast<std::streamsize>(_state.eeprom.size()));
    }

    void VirtualArduino::SetAnalogInput(int channel, float voltage)
    {
        if (channel < 0 || channel >= static_cast<int>(_analogInputs.size()))
            return;
        _analogInputs[channel] = voltage;
    }

    void VirtualArduino::QueueSerialInput(std::uint8_t value)
    {
        if (!IsUartRxEnabled())
        {
            return;
        }
        if (_uartRxQueue.size() >= _uartQueueLimit)
        {
            _uartRxQueue.pop_front();
            std::uint8_t ucsr0a = GetIo(Ucsr0aAddress);
            ucsr0a = static_cast<std::uint8_t>(ucsr0a | (1u << UartDataOverrunBit));
            AVR_IoWrite(&_state.core, Ucsr0aAddress, ucsr0a);
        }
        _uartRxQueue.push_back(value);
    }

    bool VirtualArduino::PinToPort(int pin, std::uint8_t &ddr, std::uint8_t &port, std::uint8_t &pinReg, std::uint8_t &bit)
    {
        if (pin < 0)
            return false;
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

    void VirtualArduino::HandleUartWrite(std::uint8_t value)
    {
        if (_uartTxQueue.size() >= _uartQueueLimit)
        {
            _uartTxQueue.pop_front();
        }
        _uartTxQueue.push_back(value);
    }

    void VirtualArduino::IoWriteHook(AvrCore *core, std::uint8_t address, std::uint8_t value, void *user)
    {
        (void)core;
        if (!user)
            return;
        auto *self = static_cast<VirtualArduino *>(user);
        if (address == Ucsr0aAddress)
        {
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

        if (address != Udr0Address)
            return;
        if (!self->IsUartTxEnabled())
        {
            return;
        }
        if (self->_uartTxPending.size() >= 1)
        {
            self->_uartTxPending.back() = value;
        }
        else
        {
            self->_uartTxPending.push_back(value);
        }
        std::uint8_t ucsr0a = self->GetIo(Ucsr0aAddress);
        ucsr0a = static_cast<std::uint8_t>(ucsr0a & ~(1u << UartDataRegisterEmptyBit));
        ucsr0a = static_cast<std::uint8_t>(ucsr0a & ~(1u << UartTxCompleteBit));
        AVR_IoWrite(&self->_state.core, Ucsr0aAddress, ucsr0a);
    }

    void VirtualArduino::IoReadHook(AvrCore *core, std::uint8_t address, std::uint8_t value, void *user)
    {
        (void)core;
        (void)value;
        if (!user)
            return;
        if (address != Udr0Address)
            return;
        auto *self = static_cast<VirtualArduino *>(user);
        if (!self->_uartRxReady)
        {
            return;
        }
        self->_uartRxReady = false;
        std::uint8_t ucsr0a = self->GetIo(Ucsr0aAddress);
        ucsr0a = static_cast<std::uint8_t>(ucsr0a & ~(1u << UartRxCompleteBit));
        ucsr0a = static_cast<std::uint8_t>(ucsr0a & ~(1u << UartDataOverrunBit));

        AVR_IoWrite(&self->_state.core, Ucsr0aAddress, ucsr0a);
    }

    void VirtualArduino::SimulateTimer0(std::uint64_t cycles)
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
        std::uint16_t sum = static_cast<std::uint16_t>(counter) + static_cast<std::uint16_t>(ticks);
        counter = static_cast<std::uint8_t>(sum & 0xFF);
        AVR_IoWrite(&_state.core, AVR_TCNT0, counter);
        if (sum > 0xFF)
        {
            std::uint8_t tifr0 = GetIo(AVR_TIFR0);
            tifr0 = static_cast<std::uint8_t>(tifr0 | 0x01);
            AVR_IoWrite(&_state.core, AVR_TIFR0, tifr0);
        }

        std::uint8_t ocr0a = GetIo(AVR_OCR0A);
        std::uint8_t ocr0b = GetIo(AVR_OCR0B);
        auto crossed8 = [](std::uint8_t start, std::uint8_t end, std::uint8_t target)
        {
            if (start <= end)
            {
                return target > start && target <= end;
            }
            return target > start || target <= end;
        };
        bool anyCycle = ticks >= 256;
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

        bool fastPwm = (tccr0a & 0x03) == 0x03;
        if (!fastPwm)
            return;

        std::uint8_t ddrd = GetIo(AVR_DDRD);
        std::uint8_t portd = GetIo(AVR_PORTD);

        bool com0a = (tccr0a & (1u << 7)) != 0;
        bool com0b = (tccr0a & (1u << 5)) != 0;

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

    void VirtualArduino::SimulateTimer1(std::uint64_t cycles)
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
        std::uint32_t sum = static_cast<std::uint32_t>(counter) + static_cast<std::uint32_t>(ticks);
        counter = static_cast<std::uint16_t>(sum & 0xFFFF);
        AVR_IoWrite(&_state.core, AVR_TCNT1L, static_cast<std::uint8_t>(counter & 0xFF));
        AVR_IoWrite(&_state.core, AVR_TCNT1H, static_cast<std::uint8_t>((counter >> 8) & 0xFF));
        if (sum > 0xFFFF)
        {
            std::uint8_t tifr1 = GetIo(AVR_TIFR1);
            tifr1 = static_cast<std::uint8_t>(tifr1 | 0x01);
            AVR_IoWrite(&_state.core, AVR_TIFR1, tifr1);
        }

        std::uint16_t ocr1a = static_cast<std::uint16_t>(
            static_cast<std::uint16_t>(GetIo(AVR_OCR1AL)) |
            (static_cast<std::uint16_t>(GetIo(AVR_OCR1AH)) << 8));
        std::uint16_t ocr1b = static_cast<std::uint16_t>(
            static_cast<std::uint16_t>(GetIo(AVR_OCR1BL)) |
            (static_cast<std::uint16_t>(GetIo(AVR_OCR1BH)) << 8));
        auto crossed16 = [](std::uint16_t start, std::uint16_t end, std::uint16_t target)
        {
            if (start <= end)
            {
                return target > start && target <= end;
            }
            return target > start || target <= end;
        };
        bool anyCycle = ticks >= 65536;
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

        bool fastPwm = (tccr1a & 0x03) == 0x03;
        if (!fastPwm)
            return;

        std::uint8_t ddrb = GetIo(AVR_DDRB);
        std::uint8_t portb = GetIo(AVR_PORTB);
        bool com1a = (tccr1a & (1u << 7)) != 0;
        bool com1b = (tccr1a & (1u << 5)) != 0;

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

        AVR_IoWrite(&_state.core, AVR_PORTB, portb);
    }

    void VirtualArduino::SimulateTimer2(std::uint64_t cycles)
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
        std::uint16_t sum = static_cast<std::uint16_t>(counter) + static_cast<std::uint16_t>(ticks);
        counter = static_cast<std::uint8_t>(sum & 0xFF);
        AVR_IoWrite(&_state.core, AVR_TCNT2, counter);
        if (sum > 0xFF)
        {
            std::uint8_t tifr2 = GetIo(AVR_TIFR2);
            tifr2 = static_cast<std::uint8_t>(tifr2 | 0x01);
            AVR_IoWrite(&_state.core, AVR_TIFR2, tifr2);
        }

        std::uint8_t ocr2a = GetIo(AVR_OCR2A);
        std::uint8_t ocr2b = GetIo(AVR_OCR2B);
        auto crossed8 = [](std::uint8_t start, std::uint8_t end, std::uint8_t target)
        {
            if (start <= end)
            {
                return target > start && target <= end;
            }
            return target > start || target <= end;
        };
        bool anyCycle = ticks >= 256;
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

        bool fastPwm = (tccr2a & 0x03) == 0x03;
        if (!fastPwm)
            return;
        std::uint8_t ddrb = GetIo(AVR_DDRB);
        std::uint8_t portb = GetIo(AVR_PORTB);
        std::uint8_t ddrd = GetIo(AVR_DDRD);
        std::uint8_t portd = GetIo(AVR_PORTD);

        bool com2a = (tccr2a & (1u << 7)) != 0;
        bool com2b = (tccr2a & (1u << 5)) != 0;

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

    bool VirtualArduino::MeasureHexMaxAddress(const std::string &hexText, std::size_t &outMax)
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
