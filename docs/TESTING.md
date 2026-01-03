# Testing

## CoreSim Tests

Run .NET tests from the repo root:

```powershell
dotnet test CoreSim/tests/RobotTwin.CoreSim.Tests/RobotTwin.CoreSim.Tests.csproj
```

## Unity Smoke Test

```powershell
python tools/rt_tool.py run-unity-smoke
```

## Physics Tests

```powershell
dotnet test CoreSim/tests/RobotTwin.CoreSim.Tests/RobotTwin.CoreSim.Tests.csproj --filter Category=Physics
```

## Debug Console

Use the local web UI to run tests and inspect logs:

```powershell
python tools/rt_tool.py debug-console
```
