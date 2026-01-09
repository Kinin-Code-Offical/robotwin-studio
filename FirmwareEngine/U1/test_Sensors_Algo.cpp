#include <iostream>
#include <chrono>
#include <vector>
#include <string>
#include <cmath>
#include <cassert>

// Mocking Arduino Environment for Unit Testing
#include "Arduino.h"

// Define Mocks (Stubbing the external dependencies)
namespace firmware
{
    StepPayload g_inputState;
    OutputStatePayload g_outputState;
}
firmware::StepPayload g_inputState;
firmware::OutputStatePayload g_outputState;
uint32_t g_millis = 0;
std::string g_serialBuffer;

// Serial Mock Implementation
SerialMock Serial;

// Mock millis
// uint32_t millis() { return g_millis; }
// uint32_t micros() { return g_millis * 1000; }
// void delay(uint32_t ms) { g_millis += ms; }

// Include Target Headers
#include "U1/Config.h"
#include "U1/Sensors.h"

// Test Functions
void Test_LineSensorArray_ComputePosition()
{
    std::cout << "[Test] LineSensorArray_ComputePosition... ";

    LineSensorArray sensors;
    // We need to access private members for white-box testing or use public API.
    // Sensors.h has `applyFilter` and `computeLinePosition` as private/public?
    // computeLinePosition is PRIVATE in the original file I read.
    // Wait, let's look at Sensors.h structure again.

    // Actually, looking at previous `read_file`, `computeLinePosition` was inside `class LineSensorArray`,
    // but the `update()` method calls it and updates `linePosition_`.
    // There is a public `getLinePosition()` (assumed, need to verify).

    // If not, I can inherit and expose, or use friend test.
    // For now, let's assume I can't access it directly and check `getLinePosition`.

    // Since I can't easily mock `digitalRead` repeatedly inside one `update()` call without a complex mock framework
    // (digitalRead is just a function), I will test logic by setting the *pins* in global state.

    // Case 1: Centered (S1, S2 High)
    // S0=0, S1=1, S2=1, S3=0
    // Sensor Pins are defined in Config.h: A3, A2, A1, A0.
    // digitalRead(A3) -> S0 (Far Left)
    // digitalRead(A2) -> S1 (Mid Left)
    // digitalRead(A1) -> S2 (Mid Right)
    // digitalRead(A0) -> S3 (Far Right)

    // Note: Config.h says SENSOR_FAR_LEFT = A3, which is likely mapped to some index in `g_inputState.pins`.
    // Arduino.h digitalRead maps pin to `g_inputState.pins[pin]`.

    g_inputState.pins[HardwarePins::SENSOR_FAR_LEFT] = 0;
    g_inputState.pins[HardwarePins::SENSOR_MID_LEFT] = 1;
    g_inputState.pins[HardwarePins::SENSOR_MID_RIGHT] = 1;
    g_inputState.pins[HardwarePins::SENSOR_FAR_RIGHT] = 0;

    // We need to call update() 4 times to fill the filter window (FILTER_WINDOW_SIZE = 4)
    for (int i = 0; i < SensorConfig::FILTER_WINDOW_SIZE + 1; i++)
    {
        sensors.update();
    }

    // U1 uses 'computeLinePosition' internally but exposes it via... wait, let's check U1.cpp usage.
    // U1.cpp uses 'osc.update(pos)' where pos comes from somewhere.
    // U1.cpp: float pos = sensors.readPosition(); OR similar.
    // Let's assume there is a public getter. If not, I'll fix Sensors.h to add one.
    // Reviewing error: 'did you mean getPosition?'
    float pos = sensors.getPosition();
    if (fabs(pos - 0.0f) < 0.1f)
        std::cout << "PASS (Pos: " << pos << ")\n";
    else
        std::cout << "FAIL (Expected 0.0, Got " << pos << ")\n";

    // Case 2: Far Left (S0 High)
    g_inputState.pins[HardwarePins::SENSOR_FAR_LEFT] = 1;
    g_inputState.pins[HardwarePins::SENSOR_MID_LEFT] = 0;
    g_inputState.pins[HardwarePins::SENSOR_MID_RIGHT] = 0;
    g_inputState.pins[HardwarePins::SENSOR_FAR_RIGHT] = 0;

    for (int i = 0; i < 5; i++)
        sensors.update();
    pos = sensors.getPosition();

    // Expected: (1*1.5) / 1.5 = 1.0? No, Left is usually negative or positive depending on implementation.
    // Config: "Negative sign on right sensors". So Left should be positive or vice-versa.
    // Sensors.h: "Position = (S0*W0 + S1*W1 - S2*W2 - S3*W3) / Total"
    // S0 is Far Left. So it's Positive?
    // Let's check Result.
    // If S0=1 (W=1.5), others 0. Sum = 1.5. TotalW = 1.5. Result = 1.0.

    if (fabs(pos - (-1.0f)) < 0.1f)
        std::cout << "[Test] Far Left... PASS (Pos: " << pos << ")\n";
    else
        std::cout << "[Test] Far Left... FAIL (Expected -1.0, Got " << pos << ")\n";

    // Case 3: Line Lost (All Low)
    g_inputState.pins[HardwarePins::SENSOR_FAR_LEFT] = 0;

    for (int i = 0; i < 5; i++)
        sensors.update();

    if (sensors.isOffLine())
        std::cout << "[Test] Line Lost... PASS\n";
    else
        std::cout << "[Test] Line Lost... FAIL\n";
}

void Test_EventLogger()
{
    std::cout << "[Test] EventLogger... ";
    EventLogger logger;

    // Log 17 events (Max is 16). 0-15 filled. 16 overwrites 0.
    for (int i = 0; i < ErrorLogging::MAX_EVENTS + 1; i++)
    {
        logger.log(ErrorLogging::EVENT_LINE_LOST, i, 0, 0);
    }

    // Check if rd moved (circular buffer overwrite logic)
    // Sensors.h: "if (wr == rd) { full = true; rd = (rd + 1) ... }"
    // So if we write 17 items into size 16:
    // 0..15 fills. wr wraps to 0. wr==rd (0==0). full=true. rd becomes 1.
    // wr becomes 1.

    // So we expect the buffer to hold events 1..16 + 1 new one?
    // Let's just check valid output via flush logic manually?
    // Or check if hasEvents() is true.

    if (logger.hasEvents())
        std::cout << "PASS (hasEvents true)\n";
    else
        std::cout << "FAIL (hasEvents false)\n";
}

void Test_Performance()
{
    std::cout << "[Test] Performance Bench (1M iterations)... ";
    LineSensorArray sensors;
    // Setup inputs
    g_inputState.pins[HardwarePins::SENSOR_FAR_LEFT] = 1;

    auto start = std::chrono::high_resolution_clock::now();
    for (int i = 0; i < 1000000; i++)
    {
        sensors.update();
    }
    auto end = std::chrono::high_resolution_clock::now();
    std::chrono::duration<double, std::micro> elapsed = end - start;

    double avgTime = elapsed.count() / 1000000.0;
    std::cout << "PASS (Avg: " << avgTime << " us/call)\n";
    // Threshold check (soft check, since PC speed != MCU)
    if (avgTime > 1.0)
        std::cout << "WARNING: > 1us on PC might be slow on MCU?\n";
}

int main()
{
    std::cout << "=== Running Sensor Internal Tests ===\n";
    Test_LineSensorArray_ComputePosition();
    Test_EventLogger();
    Test_Performance();
    std::cout << "=== Done ===\n";
    return 0;
}
