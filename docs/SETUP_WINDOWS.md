# Windows Development Setup

This project requires a specific Windows configuration to support Hard Realtime execution and QEMU virtualization.

## Prerequisites

- **OS:** Windows 10/11 Pro or Enterprise (x64)
- **Virtualization:** Hyper-V enabled in BIOS and Windows Features.
- **IDE:** Visual Studio 2022 (Desktop C++ & .NET Desktop Development workloads).
- **Engine:** Unity 6 (LTS).
- **Languages:** Python 3.11+, Node.js 20+.
- **Tools:** CMake 3.28+, Ninja, Git.

## 1. Realtime Kernel Configuration

To achieve <10s jitter, we must configure Windows to prioritize the simulation threads.

1. **Enable High Performance Power Plan:**
   `powershell
   powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61
   `
2. **Disable CPU Core Parking:**
   Run 	ools/scripts/disable_core_parking.ps1 (Admin).
3. **Set Timer Resolution:**
   The simulation engine will automatically request 0.5ms timer resolution at runtime.

## 2. QEMU Setup

RobotWin bundles a custom QEMU build, but you must enable the Windows Hypervisor Platform.

1. Open **Turn Windows features on or off**.
2. Check **Windows Hypervisor Platform**.
3. Check **Virtual Machine Platform**.
4. Restart your computer.

## 3. Clone and Bootstrap

1. Clone the repo recursively (submodules are required for QEMU/SimAVR):
   `powershell
   git clone --recursive https://github.com/robotwin-studio/robotwin-studio.git
   cd robotwin-studio
   `

2. Run the automated setup script:
   `powershell
   python tools/rt_tool.py setup
   `
   This will:
   - Download the QEMU binaries.
   - Compile the NativeEngine dependencies (PhysX/Jolt).
   - Generate the Visual Studio solution files.

## 4. Verify Installation

Run the system health check:
`powershell
python tools/rt_tool.py check-env
`
Expected output:
- [OK] Realtime Kernel: Enabled
- [OK] Hypervisor: Present
- [OK] QEMU: Found (v8.2.0)
- [OK] Unity: Found (2023.3.0f1)

