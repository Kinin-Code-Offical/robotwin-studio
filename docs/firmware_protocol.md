# FirmwareEngine IPC Protocol (v1.0)

Binary framed protocol used between Unity (FirmwareClient) and VirtualArduinoFirmware.exe.

## Header

All messages begin with a 20-byte header (little-endian):

- uint32 magic: 0x57465452 ("RTFW")
- uint16 version_major: 1
- uint16 version_minor: 0
- uint16 type: MessageType
- uint16 flags: reserved
- uint32 payload_size: bytes following header
- uint32 sequence: monotonic id

## Message Types

- 1 Hello (client -> server)
- 2 HelloAck (server -> client)
- 3 LoadBvm (client -> server)
- 4 Step (client -> server)
- 5 OutputState (server -> client)
- 6 Serial (server -> client)
- 7 Status (server -> client)
- 8 Log (server -> client)
- 9 Error (server -> client)

## Payloads (v1)

Hello:
- uint32 flags
- uint32 pin_count

HelloAck:
- uint32 flags
- uint32 pin_count

LoadBvm:
- raw .bvm bytes

Step:
- uint32 delta_micros
- uint8 pins[20]

OutputState:
- uint64 tick_count
- uint8 pins[20]

Serial:
- raw UTF-8 bytes

Status:
- uint64 tick_count

Log:
- uint8 level (1=info,2=warn,3=error)
- UTF-8 text

Error:
- uint32 code
- UTF-8 text
