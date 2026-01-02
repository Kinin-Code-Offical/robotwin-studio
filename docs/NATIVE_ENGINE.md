# NativeEngine

NativeEngine contains native libraries and build configuration used by the simulation stack.

## Build

```powershell
python tools/rt_tool.py build-native
```

## Output

- Native DLLs land under `builds/native` and may be copied into Unity plugins.

## Notes

- Keep CMake targets small and deterministic.
- Prefer clear ABI boundaries for C# interop.
