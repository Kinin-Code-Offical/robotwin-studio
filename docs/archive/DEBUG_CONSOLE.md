# Debug Console

The Debug Console is a local web dashboard for running common tasks and browsing logs.

## Launch

```powershell
python tools/rt_tool.py debug-console
```

The server starts on http://127.0.0.1:8090 and will pick the next open port up to 8100.

## What it does

- Runs common build, validation, and test actions by invoking the same scripts behind `rt_tool.py`.
- Provides a simple UI for browsing `logs/` outputs.

## Notes

- Logs are stored in `logs/debug_console/`.
- The dashboard can launch build and validation tasks that are also available in `rt_tool.py`.
