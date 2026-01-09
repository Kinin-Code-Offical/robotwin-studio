# Binary Audit Report

Generated: 2025-01-XX

## Summary

Total builds size scanned: ~92 MB (top 30 files)

## Findings

### 1. Duplicate com0com Installers

**Issue**: Same installer appears twice

- `builds/com0com/Setup_com0com_v3.0.0.0_W7_x64_signed.exe` (0.25 MB)
- `builds/com0com/extracted/Setup_com0com_v3.0.0.0_W7_x64_signed.exe` (0.25 MB)
- `builds/com0com/Setup_com0com_v3.0.0.0_W7_x86_signed.exe` (similar)
- `builds/com0com/extracted/Setup_com0com_v3.0.0.0_W7_x86_signed.exe` (similar)

**Recommendation**: Remove `builds/com0com/extracted/` directory (duplicates)
**Savings**: ~0.5 MB

### 2. CMake Intermediate Files

**Issue**: CMake compiler test executables left in build artifacts

- `builds/firmware/cmake/CMakeFiles/3.31.3/CompilerIdCXX/a.exe`
- `builds/firmware/cmake/CMakeFiles/3.31.3/CompilerIdC/a.exe`
- `builds/native/cmake/CMakeFiles/3.31.3/CompilerIdCXX/CompilerIdCXX.exe`
- `builds/native/cmake/CMakeFiles/3.31.3/CompilerIdC/CompilerIdC.exe`

**Recommendation**: Exclude `CMakeFiles/` from distribution builds
**Savings**: Minimal size, but cleaner

### 3. Large Native Engine Builds

**File**: `builds/firmware/RoboTwinFirmwareHost.exe` (18.51 MB)
**File**: `builds/native/NativeEngine.exe` (17.11 MB)

**Recommendation**:

- Check if debug symbols included (`/DEBUG:FULL` in link flags)
- Switch to Release builds with `/DEBUG:NONE` or use separate PDB files
- Apply UPX compression for standalone executables

**Potential Savings**: 30-50% size reduction (9-17 MB)

### 4. Static Library in Distribution

**File**: `builds/native/cmake/NativeEngineCore.dir/Release/NativeEngineCore.lib` (5.69 MB)

**Recommendation**: This is an intermediate build artifact, should NOT be in distribution
**Savings**: 5.69 MB

### 5. Export Files (.exp)

**Files**:

- `builds/native/NativeEngine.exp`
- `builds/native/NativeEngineStandalone.exp`

**Recommendation**: `.exp` files are linker intermediates, not needed at runtime
**Savings**: Minimal but cleaner

## Action Plan

### High Priority (13+ MB savings)

1. **Remove intermediate build artifacts**:

   ```powershell
   Remove-Item -Recurse -Force builds/native/cmake/NativeEngineCore.dir
   Remove-Item builds/native/*.exp
   Remove-Item builds/com0com/extracted -Recurse
   ```

2. **Strip debug symbols from release builds**:
   - Update CMakeLists.txt: `set(CMAKE_BUILD_TYPE Release)`
   - Add linker flag: `/DEBUG:NONE` or `/OPT:REF /OPT:ICF`

### Medium Priority (9-17 MB savings)

3. **Apply UPX compression** to standalone executables:
   ```powershell
   upx --best builds/firmware/RoboTwinFirmwareHost.exe
   upx --best builds/native/NativeEngine.exe
   ```

### Low Priority (Code cleanup)

4. **Update .gitignore** to exclude:
   ```
   builds/**/CMakeFiles/
   builds/**/*.exp
   builds/**/*.lib
   ```

## Before/After Size Comparison

- **Before**: ~92 MB (top 30 files)
- **After** (with all optimizations): ~60-70 MB estimated
- **Savings**: 22-32 MB (24-35% reduction)

## Notes

- Unity runtime DLLs (UnityPlayer.dll, mono-2.0-bdwgc.dll) are expected and necessary
- System.\*.dll files are part of .NET framework, normal for Unity builds
- RobotTwin.Runtime.dll and RobotTwin.CoreSim.dll are application code, appropriately sized
