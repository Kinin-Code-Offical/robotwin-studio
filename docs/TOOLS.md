# Tooling & CLI

The primary entry point for development tasks is `tools/rt_tool.py`. It wraps common build/setup steps so you don’t have to remember a dozen commands.

## Common Commands

### Build & Setup

```powershell
python tools/rt_tool.py setup              # Install dependencies (CMake, Ninja, QEMU)
python tools/rt_tool.py build-native       # Build Physics Engine (C++)
python tools/rt_tool.py build-firmware     # Build Firmware Host & AVR Sim
python tools/rt_tool.py build-all          # Build everything
```

### QEMU & Images

```powershell
python tools/rt_tool.py qemu-download --distro ubuntu-22.04  # Download base image
python tools/rt_tool.py qemu-create-img --size 32G           # Create blank SD card image
python tools/rt_tool.py qemu-flash --image my_os.img         # Flash custom OS to virtual SD
```

### Unity Integration

```powershell
python tools/rt_tool.py update-unity-plugins  # Copy native DLLs to Unity Assets
python tools/rt_tool.py run-unity-smoke       # Run headless Unity smoke tests
```

### Snapshot & Maintenance

```powershell
python tools/rt_tool.py update-repo-snapshot  # Update file lists and docs
python tools/rt_tool.py clean                 # Clean all build artifacts
```

## Debug Console

The Debug Console is a web-based dashboard for monitoring the simulation state in real-time, separate from the Unity UI.

```powershell
python tools/rt_tool.py debug-console
```

- **Port:** http://localhost:8080
- **Features:**
  - Realtime log streaming (CoreSim, Physics, Firmware).
  - Performance graphs (Frame time, Memory usage).
  - QEMU Serial Console (VNC/Serial over WebSocket).
