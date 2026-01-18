# RobotWinFirmwareMonitor

Standalone Windows monitor that connects to the firmware pipe and reconstructs activity externally.

## Features
- Connect to FirmwareEngine via named pipe (lockstep).
- View digital inputs, outputs, bit-level transitions, and pin history graph.
- Inspect analog inputs (voltage, raw value, bits).
- Live logs, serial output, perf counters, CPU/memory counters, routing stats, and optional trace tailing.
- Host process tree snapshot for external context.
- Firmware patch injection (flash-only) for live instrumentation.
- Windows service install/start/stop/remove controls.

## Run

```
dotnet run --project tools/RobotWinFirmwareMonitor/RobotWinFirmwareMonitor.csproj
```

## Quick start
1. Set `Pipe`, `Board ID`, and `Profile` if needed.
2. Set `Firmware EXE` and optionally `Extra Args`.
3. Click `Connect`, then `Launch Firmware`.
4. Load a firmware image via `Load BVM/HEX`.
5. Use `Step Once` or `Auto Step` to drive the simulation.

## Trace tailing
If your firmware writes a trace/log file (for example using `--log <path>` or `--trace-lockstep`), point `Trace file` at that path and click `Tail Trace`.

## Live instruction trace
Add `--trace-cpu` (and optional `--trace-cpu-interval <n>`) to `Extra Args` to stream instruction trace lines into the Trace tab.

## Firmware injection
Use the Injector tab to patch Flash/SRAM/IO/EEPROM bytes into the running board (hex address + raw binary patch file).

## Service management
Use the Services tab to install, start, stop, query, or remove a Windows service. Operations that require admin rights will trigger UAC.

## Logs and builds
- Logs must live under `logs/RobotWinFirmwareMonitor`.
- Build outputs are routed under `builds/<ProjectName>/` via `Directory.Build.props`.
