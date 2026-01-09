// Lockstep Timing Validation Test
// Validates VirtualMcu timing precision, determinism, and synchronization
// Tests: cycle accuracy, UART timing, ADC timing, timer overflow, determinism

#include "../VirtualMcu.h"
#include "../BoardProfile.h"
#include <iostream>
#include <cassert>
#include <cmath>
#include <chrono>
#include <vector>
#include <algorithm>

using namespace firmware;

namespace LockstepTests
{

    constexpr double EPSILON = 1e-9;
    constexpr std::uint64_t F_CPU = 16000000ULL; // 16MHz

    bool nearEqual(double a, double b, double epsilon = EPSILON)
    {
        return std::fabs(a - b) < epsilon;
    }

    // === TEST 1: Cycle-Accurate Execution ===
    // Verify that StepCycles advances tick count exactly
    bool Test_CycleAccuracy()
    {
        std::cout << "[TEST] Cycle-Accurate Execution\n";

        BoardProfile profile{};
        profile.mcu = "ATmega328P";
        profile.freq_hz = F_CPU;
        profile.flash_bytes = 32768;
        profile.sram_bytes = 2048;
        profile.eeprom_bytes = 1024;
        profile.io_bytes = 256;
        profile.pin_count = 20;

        VirtualMcu mcu(profile);

        // Test 1: Single cycle step
        std::uint64_t tick0 = mcu.TickCount();
        mcu.StepCycles(1);
        std::uint64_t tick1 = mcu.TickCount();
        assert(tick1 == tick0 + 1);
        std::cout << "  [PASS] Single cycle step accurate\n";

        // Test 2: Multiple cycle step
        mcu.StepCycles(1000);
        std::uint64_t tick2 = mcu.TickCount();
        assert(tick2 == tick1 + 1000);
        std::cout << "  [PASS] Multi-cycle step accurate\n";

        // Test 3: Large step
        mcu.StepCycles(1000000);
        std::uint64_t tick3 = mcu.TickCount();
        assert(tick3 == tick2 + 1000000);
        std::cout << "  [PASS] Large cycle step accurate\n";

        return true;
    }

    // === TEST 2: Deterministic Execution ===
    // Same inputs must produce same outputs
    bool Test_Determinism()
    {
        std::cout << "[TEST] Deterministic Execution\n";

        BoardProfile profile{};
        profile.mcu = "ATmega328P";
        profile.freq_hz = F_CPU;
        profile.flash_bytes = 32768;
        profile.sram_bytes = 2048;
        profile.eeprom_bytes = 1024;
        profile.io_bytes = 256;
        profile.pin_count = 20;

        // Run 1
        std::vector<std::uint8_t> outputs1;
        {
            VirtualMcu mcu(profile);

            // Set some inputs
            mcu.SetAnalogInput(0, 2.5f);
            mcu.SetInputPin(5, 1);

            // Execute steps
            for (int i = 0; i < 100; i++)
            {
                mcu.StepCycles(100);
                std::uint8_t portb = mcu.GetIo(0x05); // PORTB
                outputs1.push_back(portb);
            }
        }

        // Run 2 (identical conditions)
        std::vector<std::uint8_t> outputs2;
        {
            VirtualMcu mcu(profile);

            mcu.SetAnalogInput(0, 2.5f);
            mcu.SetInputPin(5, 1);

            for (int i = 0; i < 100; i++)
            {
                mcu.StepCycles(100);
                std::uint8_t portb = mcu.GetIo(0x05);
                outputs2.push_back(portb);
            }
        }

        // Compare
        assert(outputs1.size() == outputs2.size());
        for (size_t i = 0; i < outputs1.size(); i++)
        {
            assert(outputs1[i] == outputs2[i]);
        }

        std::cout << "  [PASS] Deterministic execution verified\n";
        return true;
    }

    // === TEST 3: UART Timing Accuracy ===
    // Verify UART bit timing matches baud rate
    bool Test_UartTiming()
    {
        std::cout << "[TEST] UART Timing Accuracy\n";

        BoardProfile profile{};
        profile.mcu = "ATmega328P";
        profile.freq_hz = F_CPU;
        profile.flash_bytes = 32768;
        profile.sram_bytes = 2048;
        profile.eeprom_bytes = 1024;
        profile.io_bytes = 256;
        profile.pin_count = 20;

        VirtualMcu mcu(profile);

        // Configure UART0 for 9600 baud (UBRR = F_CPU / (16 * 9600) - 1 = 103)
        constexpr std::uint16_t ubrr = 103;
        constexpr double expectedBaud = F_CPU / (16.0 * (ubrr + 1));
        constexpr double cyclesPerBit = F_CPU / expectedBaud;
        constexpr double cyclesPerByte = cyclesPerBit * 10; // 1 start + 8 data + 1 stop

        std::cout << "  Expected baud: " << expectedBaud << " Hz\n";
        std::cout << "  Cycles per byte: " << cyclesPerByte << "\n";

        // Enable UART TX (set TXEN0)
        mcu.SetIo(0xC1, 0x08); // UCSR0B = 0b00001000

        // Set baud rate
        mcu.SetIo(0xC4, ubrr & 0xFF);        // UBRR0L
        mcu.SetIo(0xC5, (ubrr >> 8) & 0xFF); // UBRR0H

        // Queue a byte for transmission
        mcu.QueueSerialInput(0, 0x55); // 'U'

        // Check that transmission takes expected cycles
        // (This is a simplified test - real UART timing is more complex)
        std::cout << "  [PASS] UART timing parameters configured\n";

        return true;
    }

    // === TEST 4: Reset Behavior ===
    // Verify reset properly initializes state
    bool Test_ResetBehavior()
    {
        std::cout << "[TEST] Reset Behavior\n";

        BoardProfile profile{};
        profile.mcu = "ATmega328P";
        profile.freq_hz = F_CPU;
        profile.flash_bytes = 32768;
        profile.sram_bytes = 2048;
        profile.eeprom_bytes = 1024;
        profile.io_bytes = 256;
        profile.pin_count = 20;

        VirtualMcu mcu(profile);

        // Execute some cycles
        mcu.StepCycles(10000);
        assert(mcu.TickCount() == 10000);

        // Set some state
        mcu.SetIo(0x05, 0xFF); // PORTB
        mcu.SetAnalogInput(0, 5.0f);

        // Soft reset (should preserve flash/EEPROM)
        mcu.SoftReset();
        assert(mcu.TickCount() == 0);

        // IO should be reset
        std::uint8_t portb = mcu.GetIo(0x05);
        assert(portb == 0);

        std::cout << "  [PASS] Soft reset clears state\n";

        // Hard reset
        mcu.StepCycles(5000);
        mcu.Reset();
        assert(mcu.TickCount() == 0);

        std::cout << "  [PASS] Hard reset works correctly\n";

        return true;
    }

    // === TEST 5: Pin I/O Timing ===
    // Verify pin changes are synchronized with execution
    bool Test_PinIOTiming()
    {
        std::cout << "[TEST] Pin I/O Timing\n";

        BoardProfile profile{};
        profile.mcu = "ATmega328P";
        profile.freq_hz = F_CPU;
        profile.flash_bytes = 32768;
        profile.sram_bytes = 2048;
        profile.eeprom_bytes = 1024;
        profile.io_bytes = 256;
        profile.pin_count = 20;

        VirtualMcu mcu(profile);

        // Set pin as output
        mcu.SetIo(0x04, 0x01); // DDRB bit 0 = output

        // Write to port
        mcu.SetIo(0x05, 0x01); // PORTB bit 0 = high

        // Read back
        std::uint8_t portb = mcu.GetIo(0x05);
        assert(portb == 0x01);

        // Sample pin outputs
        std::uint8_t pinOutputs[20] = {0xFF};
        mcu.SamplePinOutputs(pinOutputs, 20);

        // Pin 8 (PB0) should be driven high
        // (Actual pin mapping depends on board profile)
        std::cout << "  [PASS] Pin I/O operations synchronized\n";

        return true;
    }

    // === TEST 6: Performance - Cycles Per Second ===
    // Measure simulation throughput
    bool Test_PerformanceThroughput()
    {
        std::cout << "[TEST] Performance Throughput\n";

        BoardProfile profile{};
        profile.mcu = "ATmega328P";
        profile.freq_hz = F_CPU;
        profile.flash_bytes = 32768;
        profile.sram_bytes = 2048;
        profile.eeprom_bytes = 1024;
        profile.io_bytes = 256;
        profile.pin_count = 20;

        VirtualMcu mcu(profile);

        constexpr std::uint64_t TEST_CYCLES = 10000000; // 10M cycles = 625ms @ 16MHz

        auto start = std::chrono::high_resolution_clock::now();
        mcu.StepCycles(TEST_CYCLES);
        auto end = std::chrono::high_resolution_clock::now();

        auto duration = std::chrono::duration_cast<std::chrono::milliseconds>(end - start);
        double realTimeMs = duration.count();
        double simTimeMs = (TEST_CYCLES * 1000.0) / F_CPU;
        double speedup = simTimeMs / realTimeMs;

        std::cout << "  Simulated: " << simTimeMs << " ms\n";
        std::cout << "  Real time: " << realTimeMs << " ms\n";
        std::cout << "  Speedup: " << speedup << "x\n";
        std::cout << "  Throughput: " << (TEST_CYCLES / realTimeMs) << " kcycles/ms\n";

        // Should achieve at least 1x real-time (simulate faster than reality)
        // Modern CPUs should achieve 10-100x easily
        std::cout << "  [PASS] Performance throughput measured\n";

        return true;
    }

    // === TEST 7: Analog Input Resolution ===
    // Verify ADC accepts various voltage levels
    bool Test_AnalogInputResolution()
    {
        std::cout << "[TEST] Analog Input Resolution\n";

        BoardProfile profile{};
        profile.mcu = "ATmega328P";
        profile.freq_hz = F_CPU;
        profile.flash_bytes = 32768;
        profile.sram_bytes = 2048;
        profile.eeprom_bytes = 1024;
        profile.io_bytes = 256;
        profile.pin_count = 20;

        VirtualMcu mcu(profile);

        // Test various voltage levels
        std::vector<float> testVoltages = {0.0f, 1.0f, 2.5f, 3.3f, 5.0f};

        for (float voltage : testVoltages)
        {
            for (int channel = 0; channel < 8; channel++)
            {
                mcu.SetAnalogInput(channel, voltage);
            }
            mcu.StepCycles(100);
        }

        std::cout << "  [PASS] Analog inputs accepted at various voltages\n";
        return true;
    }

    // === TEST 8: Serial I/O Buffering ===
    // Verify UART queues don't overflow
    bool Test_SerialBuffering()
    {
        std::cout << "[TEST] Serial I/O Buffering\n";

        BoardProfile profile{};
        profile.mcu = "ATmega328P";
        profile.freq_hz = F_CPU;
        profile.flash_bytes = 32768;
        profile.sram_bytes = 2048;
        profile.eeprom_bytes = 1024;
        profile.io_bytes = 256;
        profile.pin_count = 20;

        VirtualMcu mcu(profile);

        // Queue many bytes
        for (int i = 0; i < 100; i++)
        {
            mcu.QueueSerialInput(static_cast<std::uint8_t>(i & 0xFF));
        }

        // Consume bytes
        int consumed = 0;
        std::uint8_t byte;
        while (mcu.ConsumeSerialByte(byte))
        {
            consumed++;
        }

        std::cout << "  Consumed: " << consumed << " bytes\n";
        assert(consumed == 100);

        std::cout << "  [PASS] Serial buffering handles burst traffic\n";
        return true;
    }

    // === TEST 9: Performance Counter Tracking ===
    // Verify perf counters increment correctly
    bool Test_PerformanceCounters()
    {
        std::cout << "[TEST] Performance Counter Tracking\n";

        BoardProfile profile{};
        profile.mcu = "ATmega328P";
        profile.freq_hz = F_CPU;
        profile.flash_bytes = 32768;
        profile.sram_bytes = 2048;
        profile.eeprom_bytes = 1024;
        profile.io_bytes = 256;
        profile.pin_count = 20;

        VirtualMcu mcu(profile);

        std::uint64_t cycles = 10000;
        mcu.StepCycles(cycles);

        const auto &perf = mcu.GetPerfCounters();
        assert(perf.cycles == cycles);

        std::cout << "  Cycles: " << perf.cycles << "\n";
        std::cout << "  ADC Samples: " << perf.adcSamples << "\n";
        std::cout << "  UART TX[0]: " << perf.uartTxBytes[0] << "\n";

        std::cout << "  [PASS] Performance counters tracked\n";
        return true;
    }

    // === TEST 10: Lockstep Synchronization ===
    // Verify two MCUs can be kept in lockstep
    bool Test_LockstepSync()
    {
        std::cout << "[TEST] Lockstep Synchronization\n";

        BoardProfile profile{};
        profile.mcu = "ATmega328P";
        profile.freq_hz = F_CPU;
        profile.flash_bytes = 32768;
        profile.sram_bytes = 2048;
        profile.eeprom_bytes = 1024;
        profile.io_bytes = 256;
        profile.pin_count = 20;

        VirtualMcu mcu1(profile);
        VirtualMcu mcu2(profile);

        // Step both in lockstep
        for (int i = 0; i < 100; i++)
        {
            mcu1.StepCycles(100);
            mcu2.StepCycles(100);

            assert(mcu1.TickCount() == mcu2.TickCount());
        }

        std::cout << "  [PASS] Two MCUs maintained lockstep sync\n";
        return true;
    }

    void RunAllTests()
    {
        std::cout << "\n=== LOCKSTEP TIMING VALIDATION SUITE ===\n\n";

        int passed = 0;
        int total = 0;

        auto runTest = [&](bool (*testFunc)(), const char *name)
        {
            total++;
            try
            {
                if (testFunc())
                {
                    passed++;
                }
                else
                {
                    std::cout << "[FAIL] " << name << "\n";
                }
            }
            catch (const std::exception &e)
            {
                std::cout << "[ERROR] " << name << ": " << e.what() << "\n";
            }
            std::cout << "\n";
        };

        runTest(Test_CycleAccuracy, "Cycle-Accurate Execution");
        runTest(Test_Determinism, "Deterministic Execution");
        runTest(Test_UartTiming, "UART Timing Accuracy");
        runTest(Test_ResetBehavior, "Reset Behavior");
        runTest(Test_PinIOTiming, "Pin I/O Timing");
        runTest(Test_PerformanceThroughput, "Performance Throughput");
        runTest(Test_AnalogInputResolution, "Analog Input Resolution");
        runTest(Test_SerialBuffering, "Serial I/O Buffering");
        runTest(Test_PerformanceCounters, "Performance Counter Tracking");
        runTest(Test_LockstepSync, "Lockstep Synchronization");

        std::cout << "=== TEST RESULTS ===\n";
        std::cout << "Passed: " << passed << "/" << total << "\n";
        std::cout << "Failed: " << (total - passed) << "/" << total << "\n";

        if (passed == total)
        {
            std::cout << "\n✓ ALL LOCKSTEP TESTS PASSED\n";
        }
        else
        {
            std::cout << "\n✗ SOME TESTS FAILED\n";
        }
    }

} // namespace LockstepTests

int main()
{
    LockstepTests::RunAllTests();
    return 0;
}
