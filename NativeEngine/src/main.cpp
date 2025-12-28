#include "../include/Bridge/UnityInterface.h"
#include <cstdio>
#include <vector>

int main(int argc, char **argv) {
  std::printf("NativeEngine Standalone - AVR Blinky Test\n");

  Native_CreateContext();

  // Circuit: AVR (Pin 13) -- Resistor (220) -- LED -- GND
  int nodePin13 = Native_AddNode(); // Node 1
  int nodeAnode = Native_AddNode(); // Node 2

  // Components
  // AVR (Type 6 = IC_Pin/AVR)
  int avr = Native_AddComponent(6, 0, nullptr);

  // Resistor
  float rParams[] = {220.0f};
  int r1 = Native_AddComponent(0, 1, rParams);

  // Diode (LED)
  int d1 = Native_AddComponent(3, 0, nullptr);

  std::printf("Components Created.\n");

  // Connections
  // AVR Pin 13 -> NodePin13
  Native_Connect(avr, 13, nodePin13);

  // R1: NodePin13 -> NodeAnode
  Native_Connect(r1, 0, nodePin13);
  Native_Connect(r1, 1, nodeAnode);

  // Diode: NodeAnode -> GND
  Native_Connect(d1, 0, nodeAnode);
  Native_Connect(d1, 1, 0);

  // Load Firmware
  // Hardcode Hex
  // :06000000259A2D9AFFCF70
  FILE *f = std::fopen("blink_test.hex", "w");
  if (f) {
    std::fprintf(f, ":06000000259A2D9AFFCF70\n:00000001FF\n");
    std::fclose(f);
  }

  LoadHexFromFile("blink_test.hex");

  // Simulate
  std::printf("Stepping simulation...\n");
  for (int i = 0; i < 20; ++i) {
    Native_Step(0.001f);

    float vPin = Native_GetVoltage(nodePin13);
    float vAnode = Native_GetVoltage(nodeAnode);
    std::printf("Step %d: Pin13=%.2f V, Anode=%.2f V\n", i, vPin, vAnode);
  }

  Native_DestroyContext();
  return 0;
}
