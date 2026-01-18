#include "Wire.h"

// Global Arduino-style state for U1 HLE.
firmware::StepPayload g_inputState{};
firmware::OutputStatePayload g_outputState{};
uint32_t g_millis = 0;
std::string g_serialBuffer;
SerialMock Serial;
TwoWire Wire;
