#include <iostream>
#include <vector>
#include <string>
#include <cmath>
#include <cstdio>

// Define missing macros
#define NUM_DIGITAL_PINS 20
#define NUM_ANALOG_INPUTS 6

// Mock Serial implementation for U1
#include "../../FirmwareEngine/include/Arduino.h"

// Define Global State for Arduino.h
firmware::StepPayload g_inputState;
firmware::OutputStatePayload g_outputState;
uint32_t g_millis = 0;
std::string g_serialBuffer;
SerialMock Serial;
// Wire is removed from global.

// Helper to set inputs
void SetDigitalInput(uint8_t pin, uint8_t val)
{
    if (pin < NUM_DIGITAL_PINS)
    {
        g_inputState.pins[pin] = val;
    }
}

// === INCLUDE FIRMWARE ===
namespace RobotFirmware
{
// Include Headers from FirmwareEngine/include (Mocked hardware)
#include "AFMotor.h"
#include "Servo.h"
#include "Adafruit_TCS34725.h"
#include "Wire.h" // Include explicit mock

    // Define the extern Wire object here, inside the namespace
    TwoWire Wire;

// Include The Sketch
#include "../../FirmwareEngine/U1/U1.ino"
}

// === TEST FRAMEWORK ===
#define ASSERT_TRUE(condition, msg) \
    if (!(condition))               \
    {                               \
        printf("[FAIL] %s\n", msg); \
        return false;               \
    }                               \
    else                            \
    {                               \
        printf("[PASS] %s\n", msg); \
    }

#define ASSERT_EQ(val1, val2, msg)                                                 \
    if ((val1) != (val2))                                                          \
    {                                                                              \
        printf("[FAIL] %s: Expected %d, Got %d\n", msg, (int)(val2), (int)(val1)); \
        return false;                                                              \
    }                                                                              \
    else                                                                           \
    {                                                                              \
        printf("[PASS] %s\n", msg);                                                \
    }

// Helper to reset simulation state
void ResetSimulation()
{
    g_millis = 0;
    RobotFirmware::st = RobotFirmware::S_INIT;

    // Clear Inputs
    for (int i = 0; i < 70; ++i)
        g_inputState.pins[i] = 0; // Clear all pins
    for (int i = 0; i < 70; ++i)
        g_outputState.pins[i] = 0;

    // Rerun setup
    RobotFirmware::setup();
}

// Helper to get Motor State
// mL (M1) -> Dir: 61, Speed: 11
// mR (M3) -> Dir: 63, Speed: 6
uint8_t GetLeftMotorDir() { return g_outputState.pins[61]; }
uint8_t GetRightMotorDir() { return g_outputState.pins[63]; }
uint8_t GetLeftMotorSpeed() { return g_outputState.pins[11]; }
uint8_t GetRightMotorSpeed() { return g_outputState.pins[6]; }

#define STATE_RELEASE 0
#define STATE_FORWARD 1
#define STATE_BACKWARD 2

// Test 1: System Initialization
bool Test_Initialization()
{
    printf("=== Test: Initialization ===\n");
    ResetSimulation();

    ASSERT_EQ(GetLeftMotorDir(), STATE_RELEASE, "Left Motor Released");
    ASSERT_EQ(GetRightMotorDir(), STATE_RELEASE, "Right Motor Released");
    return true;
}

// Test 2: Line Following Logic (Center)
bool Test_LineFollowing_Center()
{
    printf("=== Test: Line Following (Center) ===\n");
    ResetSimulation();

    RobotFirmware::loop(); // Trigger transition from S_INIT

    // Set Center (Mid Left + Mid Right)
    SetDigitalInput(15, 1);
    SetDigitalInput(16, 1);

    // Pump the loop multiple times to fill the Moving Average Filter (Window Size 4)
    for (int i = 0; i < 5; i++)
    {
        g_millis += 10;
        RobotFirmware::loop();
    }

    ASSERT_EQ(RobotFirmware::st, RobotFirmware::S_LINE, "State S_LINE");

    // Check if moving
    if (GetLeftMotorSpeed() > 0 && GetRightMotorSpeed() > 0)
    {
        printf("[PASS] Motors Moving\n");
    }
    else
    {
        printf("[FAIL] Motors Stopped (L=%d R=%d)\n", GetLeftMotorSpeed(), GetRightMotorSpeed());
        return false;
    }

    return true;
}

// Test 3: Turn Left Logic
bool Test_LineFollowing_TurnLeft()
{
    printf("=== Test: Make Turn Left ===\n");
    ResetSimulation();
    RobotFirmware::loop();

    // Far Left Active (A3 = 17) -> Means line is on Left -> Robot should Turn Left
    SetDigitalInput(15, 0);
    SetDigitalInput(16, 0);
    SetDigitalInput(17, 1); // Leftmost Sensor

    for (int i = 0; i < 10; i++)
    {
        g_millis += 4;
        RobotFirmware::loop();
    }

    uint8_t sL = GetLeftMotorSpeed();
    uint8_t sR = GetRightMotorSpeed();
    printf("Speeds: L=%d R=%d\n", sL, sR);

    if (sL < sR)
    {
        printf("[PASS] Left Motor Slower (Turning Left)\n");
        return true;
    }
    else
    {
        printf("[FAIL] Left Motor Not Slower\n");
        return false;
    }
}

int main()
{
    setvbuf(stdout, NULL, _IONBF, 0);
    if (!Test_Initialization())
        return 1;
    if (!Test_LineFollowing_Center())
        return 1;
    if (!Test_LineFollowing_TurnLeft())
        return 1;

    printf("ALL INTEGRATION TESTS PASSED\n");
    return 0;
}
