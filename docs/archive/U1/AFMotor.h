#pragma once

#include <cstdint>
#include <Arduino.h>

// Mock implementation of the Adafruit Motor Shield library for the simulator.
// This translates the object-oriented API calls into raw pin writes
// that the FirmwareEngine main loop will transmit to Unity.

// Unity Pin Mapping Convention for AFMotor (L293D):
// Motor 1: PWM=11, In1=A3?? No, the shield uses a Shift Register (74HC595).
// However, the simulator might prioritize a direct pin mapping for simplicity
// OR we need to simulate the shift register logic.

// Given the Plan says "Mock AFMotor.h to interpret speed(0-255) as Torque",
// We will bypass the Shift Register simulation found in BoardProfile
// and map directly to "Virtual Pins" if possible, OR utilize the Serial/Log
// channel to send specific motor commands that the C# NativeBridge intercepts.

// STRATEGY:
// 1. Detect if we are on Motor 1, 2, 3, 4.
// 2. Map logical state (FORWARD/BACKWARD) and Speed (0-255) to a custom protocol.
// 3. BUT, to be "God Tier" compliant with the existing Protocol.h (which sends PINS),
//    we should synthesize the output pins that the L293D *would* produce.

// Pins used by L293D Shield:
// Motor 1: D11 (PWM), Shift Register Bit 2, 3
// Motor 2: D3  (PWM), Shift Register Bit 1, 4
// Motor 3: D6  (PWM), Shift Register Bit 5, 7
// Motor 4: D5  (PWM), Shift Register Bit 0, 6
// Shift Register Control: D4 (CLK), D7 (EN), D8 (DATA), D12 (LATCH)

// Since simulating the 74HC595 bit-banging in the "Step" function is slow & complex
// for a 4-hour sprint, we will "Cheat" elegantly by writing to high-index Virtual Pins
// that Unity watches as "Left Motor Speed" and "Right Motor Speed".

// Let's assume Virtual Pins 50-53 are reserved for Motor Debugging in Protocol.h's "kPinCount = 70".

#define DC_MOTOR_1 1
#define DC_MOTOR_2 2
#define DC_MOTOR_3 3
#define DC_MOTOR_4 4

#define FORWARD 1
#define BACKWARD 2
#define RELEASE 3

class AF_DCMotor
{
public:
    uint8_t motornum;

    AF_DCMotor(uint8_t num, uint8_t freq = 0)
    {
        motornum = num;
        // In a real generic implementation, we would toggle the Shift Register pins here.
    }

    void run(uint8_t cmd)
    {
        // Here we can encode Direction into a Virtual Pin.
        // Let's use Pin 60 + motornum for Direction (0=Release, 1=Fwd, 2=Back)
        // Unity NativeBridge will read Pin[60+n].
        // 0: Release, 1: Forward, 2: Backward
        uint8_t dirVal = 0;
        if (cmd == FORWARD)
            dirVal = 1;
        if (cmd == BACKWARD)
            dirVal = 2;

        // Write to a 'Virtual' high pin that doesn't exist on physical Uno
        // but exists in our kPinCount=70
        digitalWrite(60 + motornum, dirVal);
    }

    void setSpeed(uint8_t speed)
    {
        // Direct PWM mapping
        // M1->11, M2->3, M3->6, M4->5
        int pin = 0;
        switch (motornum)
        {
        case 1:
            pin = 11;
            break;
        case 2:
            pin = 3;
            break;
        case 3:
            pin = 6;
            break;
        case 4:
            pin = 5;
            break;
        }
        if (pin != 0)
        {
            analogWrite(pin, speed);
        }
    }
};
