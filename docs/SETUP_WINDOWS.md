# Windows Setup

This guide covers the standard Windows developer setup.

## Prerequisites

- Windows 10/11 x64
- Git
- Visual Studio 2022 (Desktop C++ and .NET workloads)
- .NET SDK
- Python 3.11+
- CMake
- C++ compiler in PATH (g++ or clang)
- Unity 6 LTS
- Optional: Node.js + npm for integration tests

## Optional virtualization

Enable Windows Hypervisor Platform and Virtual Machine Platform if you plan to run QEMU targets.

## Quick setup

1. Clone the repo.
2. Run `python tools/rt_tool.py setup`.
3. Open `RobotWin/` in Unity Hub.

## Check only

Run a prerequisite check without building:

```powershell
python tools/rt_tool.py setup --check-only
```

## Optional: virtual COM ports (com0com)

If you need a virtual COM pair for serial workflows:

```powershell
python tools/rt_tool.py setup --install-com0com
```

## Notes

- Use `python tools/rt_tool.py update-unity-plugins` after building native or firmware binaries.
- Logs are written under `logs/`.
- The tooling expects `g++` on PATH for the native build (for example via MSYS2/MinGW-w64).
