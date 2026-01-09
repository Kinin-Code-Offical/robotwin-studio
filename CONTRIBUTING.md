# Contributing to RobotWin Studio

\ud83c\udf89 **Thank you for your interest in contributing to RobotWin Studio!**

We welcome contributions of all kinds: bug reports, feature requests, documentation improvements, and code contributions.

## \ud83d\udccc Table of Contents

- [Where to Start](#where-to-start)
- [Development Setup](#development-setup)
- [Running Tests](#running-tests)
- [Code Style](#code-style)
- [Pull Request Process](#pull-request-process)
- [Tooling](#tooling)

## \ud83d\udccd Where to Start

### Reporting Bugs

- **Check existing issues** first to avoid duplicates
- **Open an Issue** with:
  - Clear description of the problem
  - Steps to reproduce
  - Expected vs actual behavior
  - System information (Windows version, Unity version, .NET SDK version)
  - Relevant logs or error messages

### Suggesting Features

- **Use GitHub Discussions** for brainstorming and feedback
- **Open an Issue** for concrete feature proposals with:
  - Use case description
  - Proposed implementation (if applicable)
  - Potential impact on existing functionality

### Asking Questions

- **Use GitHub Discussions** for "How do I...?" questions
- Check existing documentation in `docs/` first

## \u2699\ufe0f Development Setup

### Prerequisites

```powershell
# Verify installed versions
dotnet --version    # Should be 8.0+
cmake --version     # Should be 3.20+
python --version    # Should be 3.11+
```

### Initial Setup

1. **Fork and clone** the repository:

   ```powershell
   git clone https://github.com/YOUR_USERNAME/robotwin-studio.git
   cd robotwin-studio
   ```

2. **Follow the Windows setup guide**:

   ```powershell
   # See docs/SETUP_WINDOWS.md for detailed instructions
   python tools/rt_tool.py setup
   ```

3. **Build all components**:

   ```powershell
   # Build native engines
   cmake -S NativeEngine -B builds/native
   cmake --build builds/native --config Release

   # Build CoreSim
   dotnet build CoreSim/CoreSim.sln --configuration Release

   # Build FirmwareEngine
   cmake -S FirmwareEngine -B builds/firmware
   cmake --build builds/firmware --config Release
   ```

## \ud83e\uddea Running Tests

## \ud83e\uddea Running Tests

### CoreSim Unit Tests

```powershell
# Run all CoreSim tests
dotnet test CoreSim/CoreSim.sln

# Run with detailed output
dotnet test CoreSim/CoreSim.sln --logger "console;verbosity=detailed"
```

### Native Engine Validation

```powershell
# Physics deep validation (energy conservation, friction, constraints)
builds/native/PhysicsDeepValidation.exe

# Sensor internal validation (line sensor algorithms)
builds/native/SensorInternalValidation.exe
```

### Integration Tests

```powershell
# Firmware integration tests
builds/native/FirmwareIntegrationTest.exe
```

## \ud83c\udfa8 Code Style

### C# (.NET/CoreSim)

- Follow [.NET coding conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use **PascalCase** for public members
- Use **camelCase** for private fields
- Prefer `var` when type is obvious
- Add XML documentation comments for public APIs

```csharp
/// <summary>
/// Processes a simulation step.
/// </summary>
/// <param name="deltaTime">Time step in seconds.</param>
public void Step(float deltaTime)
{
    // Implementation
}
```

### C++ (NativeEngine/FirmwareEngine)

- Follow **Google C++ Style Guide** with modifications:
  - Use `snake_case` for variables and functions
  - Use `PascalCase` for types and classes
  - Use `k` prefix for constants (`kMaxIterations`)
- Prefer modern C++ (C++20)
- Use `nullptr` instead of `NULL`
- Use `auto` when type is obvious

```cpp
// Good
auto* body = GetBody(id);
if (body == nullptr) {
    return false;
}

// Avoid
RigidBody* body = GetBody(id);
if (!body) {
    return false;
}
```

### Unity (C# Scripts)

- Follow Unity naming conventions
- Use `[SerializeField]` for inspector-visible fields
- Use `[Tooltip]` for user-facing descriptions
- Keep MonoBehaviour classes focused and single-purpose

## \ud83d\udd04 Pull Request Process

### Before Submitting

1. **Create a feature branch**:

   ```powershell
   git checkout -b feature/your-feature-name
   # or
   git checkout -b fix/your-bug-fix
   ```

2. **Make your changes**:

   - Keep commits atomic and focused
   - Write clear commit messages
   - Follow code style guidelines

3. **Test your changes**:

   ```powershell
   # Run all tests
   dotnet test CoreSim/CoreSim.sln
   builds/native/PhysicsDeepValidation.exe
   builds/native/SensorInternalValidation.exe
   ```

4. **Update documentation** if needed:
   - Update README.md if adding features
   - Update relevant docs/ files
   - Add inline code comments for complex logic

### PR Guidelines

- **Keep PRs small and focused** (< 500 lines if possible)
- **Provide a clear description**:
  - What problem does this solve?
  - How does it solve it?
  - Any breaking changes?
- **Include test/verification notes**:
  - What tests did you run?
  - What manual testing did you perform?
- **Link related issues**: Use "Fixes #123" or "Relates to #456"
- **Ensure CI passes** (if GitHub Actions is configured)

### PR Checklist

- [ ] Code follows project style guidelines
- [ ] All tests pass locally
- [ ] Documentation updated (if applicable)
- [ ] No build warnings introduced
- [ ] No build artifacts committed (`*.exe`, `*.dll`, `*.pdb`, etc.)
- [ ] Commit messages are clear and descriptive

## \ud83d\udee0\ufe0f Tooling

## \ud83d\udee0\ufe0f Tooling

### Repository Management

```powershell
# Update repo snapshot (README tree + docs index)
python tools/rt_tool.py update-repo-snapshot

# Sync Unity plugins from native builds
python tools/rt_tool.py update-unity-plugins

# Setup development environment
python tools/rt_tool.py setup
```

### Build Commands

```powershell
# Clean build (native)
cmake --build builds/native --target clean
cmake --build builds/native --config Release

# Clean build (CoreSim)
dotnet clean CoreSim/CoreSim.sln
dotnet build CoreSim/CoreSim.sln --configuration Release

# Rebuild everything
python tools/rt_tool.py rebuild-all
```

### Debugging

- **CoreSim**: Use Visual Studio or VS Code with C# debugger
- **NativeEngine**: Attach Visual Studio debugger to Unity process
- **FirmwareEngine**: Use named pipe debugging (see `docs/DEBUG_CONSOLE.md`)
- **Unity**: Use Unity Editor debug tools and console

## \u26a0\ufe0f Important Notes

### What NOT to Commit

- Build outputs: `bin/`, `obj/`, `builds/`, `*.exe`, `*.dll`, `*.pdb`
- Unity generated: `Library/`, `Temp/`, `Logs/`, `UserSettings/`
- IDE files: `.vs/`, `.vscode/`, `.idea/`
- Test outputs: `TestResults/`, `test_output.txt`
- Personal config: `.venv/`, `__pycache__/`

### Breaking Changes

If your PR introduces breaking changes:

1. **Document in PR description** with clear upgrade path
2. **Update version numbers** (follow Semantic Versioning)
3. **Update CHANGELOG.md** (if it exists)
4. **Consider deprecation** before removal

### Performance-Critical Code

For changes to physics engine or tight loops:

1. **Profile before and after** using built-in profilers
2. **Include benchmarks** in PR description
3. **Test on target hardware** (don't rely on dev machine)
4. **Consider numerical stability** (floating-point precision)

## \ud83d\udc65 Community

- Be respectful and constructive
- Follow [Code of Conduct](CODE_OF_CONDUCT.md)
- Help others in discussions
- Share your use cases and projects

## \ud83d\udcdd License

By contributing, you agree that your contributions will be licensed under the same [MIT License](LICENSE) that covers the project.

---

**Questions?** Open a [Discussion](https://github.com/Kinin-Code-Offical/robotwin-studio/discussions) or reach out to the maintainers.
