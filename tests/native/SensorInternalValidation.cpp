// SensorInternalValidation.cpp
// Validation of FirmwareEngine/U1/Sensors.h logic using internal Firmware Engine mocks

#include <iostream>
#include <iomanip>
#include <cassert>
#include <cmath>
#include <vector>
#include <string>
#include <algorithm>

// 1. Include Mocks/Dependencies
#include "../../FirmwareEngine/include/Wire.h"
#include "../../FirmwareEngine/include/Arduino.h"

// 2. Define Globals required by Arduino.h
firmware::StepPayload g_inputState;
firmware::OutputStatePayload g_outputState;
uint32_t g_millis = 0;
std::string g_serialBuffer;
SerialMock Serial;
TwoWire Wire;

// 3. Include System Under Test
#include "../../FirmwareEngine/U1/Sensors.h"

using namespace std;

// Helper to reset mocks
void ResetMocks()
{
    g_millis = 0;
    for (int i = 0; i < firmware::kPinCount; i++)
    {
        g_inputState.pins[i] = 0; // All LOW
    }
    // Pins: A3, A2, A1, A0
    // FirmwareEngine/include/Arduino.h defines A0=14.
    // Config.h defines SENSOR_FAR_LEFT = A3 = 17
    // SENSOR_MID_LEFT = A2 = 16
    // SENSOR_MID_RIGHT = A1 = 15
    // SENSOR_FAR_RIGHT = A0 = 14
}

void Test_LineSensor_AllWhite()
{
    cout << "[Test] LineSensor All White (0)... ";
    ResetMocks();

    LineSensorArray sensors;
    sensors.begin();

    // All inputs 0
    // Update multiple times to fill filter
    for (int i = 0; i < 10; i++)
        sensors.update();

    if (sensors.isOffLine())
    {
        cout << "PASS" << endl;
    }
    else
    {
        cout << "FAIL (Expected OffLine)" << endl;
        exit(1);
    }
}

void Test_LineSensor_CenterLine()
{
    cout << "[Test] LineSensor Center Line... ";
    ResetMocks();

    LineSensorArray sensors;
    sensors.begin();

    // Center Line -> Mid Left + Mid Right are HIGH (Black line on white background? or reflective?)
    // Usually IR sensors:
    // If logic is "Active HIGH", then seeing line = 1.
    // Let's assume seeing line = 1.

    g_inputState.pins[HardwarePins::SENSOR_MID_LEFT] = 1;
    g_inputState.pins[HardwarePins::SENSOR_MID_RIGHT] = 1;

    for (int i = 0; i < 10; i++)
        sensors.update();

    float pos = sensors.getPosition();

    // Use fabs to avoid macro conflict with abs()
    if (std::fabs(pos) < 0.1f)
    {
        cout << "PASS (Pos: " << pos << ")" << endl;
    }
    else
    {
        cout << "FAIL (Pos: " << pos << ")" << endl;
        exit(1);
    }
}

void Test_LineSensor_FarLeft()
{
    cout << "[Test] LineSensor Far Left... ";
    ResetMocks();

    LineSensorArray sensors;
    sensors.begin();

    g_inputState.pins[HardwarePins::SENSOR_FAR_LEFT] = 1;
    // Others 0

    for (int i = 0; i < 10; i++)
        sensors.update();

    float pos = sensors.getPosition();

    // Config.h documentation says output is -1.0 to 1.0.
    // Far Left implies -1.0.
    if (pos <= -0.9f)
    {
        cout << "PASS (Pos: " << pos << ")" << endl;
    }
    else
    {
        cout << "FAIL (Pos: " << pos << ")" << endl;
        exit(1);
    }
}

int main()
{
    printf("DEBUG: Starting Main\n");
    fflush(stdout);
    setvbuf(stdout, NULL, _IONBF, 0);
    cerr << "=== Sensor Internal Logic Validation ===" << endl;
    try
    {
        Test_LineSensor_AllWhite();
        Test_LineSensor_CenterLine();
        Test_LineSensor_FarLeft();
        cerr << "All Tests Passed" << endl;
    }
    catch (const std::exception &e)
    {
        cerr << "Exception: " << e.what() << endl;
        return 1;
    }
    catch (...)
    {
        cerr << "Unknown Exception" << endl;
        return 1;
    }
    return 0;
}
