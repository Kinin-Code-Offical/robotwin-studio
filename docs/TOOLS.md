# Tooling

The primary entry point is `tools/rt_tool.py`.

## Common Commands

```powershell
python tools/rt_tool.py update-repo-snapshot
python tools/rt_tool.py update-unity-plugins
python tools/rt_tool.py run-unity-smoke
python tools/rt_tool.py build-native
python tools/rt_tool.py build-firmware
python tools/rt_tool.py build-standalone
```

## Snapshot Automation

`update-repo-snapshot` performs three actions:

- Updates `docs/repo_files.txt` using `git ls-files`.
- Writes a workspace snapshot to `logs/tools/workspace_snapshot.txt`.
- Regenerates the README folder tree section.

## Debug Console

```powershell
python tools/rt_tool.py debug-console
```
