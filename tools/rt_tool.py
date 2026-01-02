import argparse
import subprocess
import sys
import time
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]


def run_powershell(script_path: Path, args: list[str]) -> int:
    cmd = [
        "powershell",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        str(script_path),
        *args,
    ]
    return subprocess.call(cmd, cwd=str(REPO_ROOT))


def run_python(script_path: Path, args: list[str]) -> int:
    cmd = [sys.executable, str(script_path), *args]
    return subprocess.call(cmd, cwd=str(REPO_ROOT))


def monitor_unity() -> int:
    log_file = REPO_ROOT / "logs" / "unity" / "unity_live_error.log"
    print(f"Monitoring: {log_file}")
    log_file.parent.mkdir(parents=True, exist_ok=True)
    if not log_file.exists():
        log_file.write_text("[MONITOR START]\n", encoding="utf-8")

    with log_file.open("r", encoding="utf-8", errors="ignore") as handle:
        handle.seek(0, 2)
        while True:
            line = handle.readline()
            if not line:
                time.sleep(0.1)
                continue
            clean = line.strip()
            if "[ERROR]" in clean or "[EXCEPTION]" in clean or "[ASSERT]" in clean:
                print("=== UNITY ERROR ===")
                print(clean)
            elif clean:
                print(clean)


def console(url: str) -> int:
    print("Robotwin Unity Console")
    print(f"Target: {url}")
    print("Commands: screenshot, reset, run-tests, status, help, quit")

    def request(endpoint: str) -> None:
        full = f"{url}/{endpoint}"
        try:
            with urllib.request.urlopen(full, timeout=2) as res:
                body = res.read().decode("utf-8", errors="ignore")
                print(body)
        except Exception as exc:
            print(f"Request failed: {exc}")

    while True:
        try:
            cmd = input("rt> ").strip().lower()
        except KeyboardInterrupt:
            print("\nExiting.")
            return 0
        if not cmd:
            continue
        if cmd in ("quit", "exit"):
            return 0
        if cmd == "help":
            print("Commands: screenshot, reset, run-tests, status, help, quit")
            continue
        if cmd == "status":
            request("query?target=ping")
            continue
        if cmd in ("screenshot", "reset", "run-tests"):
            request(cmd)
            continue
        request(cmd)


COMMAND_HELP = {
    "build-bvm": "Build a .bvm from .hex or .ino. For .ino, pass --fqbn arduino:avr:uno.",
    "build-firmware": "Build VirtualArduinoFirmware.exe into builds/firmware (logs to logs/firmware).",
    "build-native": "Build NativeEngine DLL and standalone (g++). Outputs to builds/native.",
    "build-standalone": "Build Unity Windows player via batchmode.",
    "update-unity-plugins": "Build CoreSim .NET plugin and sync into RobotWin/Assets/Plugins.",
    "update-repo-snapshot": "Refresh docs/repo_files.txt, workspace snapshot, and README folder tree.",
    "validate-uxml": "Parse and validate all .uxml files.",
    "run-qa": "Run Node/Jest integration tests.",
    "run-unity-smoke": "Batchmode Unity compile smoke test.",
    "monitor-unity": "Tail logs/unity/unity_live_error.log and highlight errors.",
    "console": "Interactive HTTP console for RemoteCommandServer.",
    "mission-control": "Launch the Mission Control dashboard.",
}


def main() -> int:
    parser = argparse.ArgumentParser(
        prog="rt_tool",
        description="Robotwin unified tool runner.",
        formatter_class=argparse.RawTextHelpFormatter
    )
    subparsers = parser.add_subparsers(dest="command")

    def add_script_command(name: str, script: str, help_text: str) -> None:
        sub = subparsers.add_parser(name, help=help_text)
        sub.add_argument("args", nargs=argparse.REMAINDER)
        sub.set_defaults(_script=script)

    add_script_command("build-bvm", "tools/scripts/build_bvm.py", "Build a .bvm from .hex or .ino")
    add_script_command("build-firmware", "tools/scripts/build_firmware.ps1", "Build VirtualArduinoFirmware.exe")
    add_script_command("build-native", "tools/scripts/build_native.ps1", "Build NativeEngine DLL + standalone")
    add_script_command("build-standalone", "tools/scripts/build_windows_standalone.ps1", "Build Unity Windows player")
    add_script_command("update-unity-plugins", "tools/scripts/update_unity_plugins.ps1", "Build CoreSim and sync Unity plugins")
    add_script_command(
        "update-repo-snapshot",
        "tools/scripts/update_repo_snapshot.ps1",
        "Refresh repo files list, workspace snapshot, and README tree"
    )
    add_script_command("validate-uxml", "tools/scripts/validate_uxml.ps1", "Validate UXML files")
    add_script_command("run-qa", "tools/scripts/run_qa.ps1", "Run integration tests (Node/Jest)")
    add_script_command("run-unity-smoke", "tools/scripts/run_unity_smoke.ps1", "Run Unity batchmode smoke test")
    add_script_command("mission-control", "tools/mission_control/launch_mission_control.ps1", "Launch Mission Control dashboard")
    subparsers.add_parser("monitor-unity", help="Tail Unity error log")
    subparsers.add_parser("help", help="Show detailed command help")
    console_parser = subparsers.add_parser("console", help="Unity HTTP console")
    console_parser.add_argument("--url", default="http://localhost:8085", help="Unity HTTP base URL")

    args = parser.parse_args()
    if not args.command:
        parser.print_help()
        return 1

    if args.command == "help":
        for key, value in COMMAND_HELP.items():
            print(f"{key}: {value}")
        return 0

    if args.command == "monitor-unity":
        return monitor_unity()
    if args.command == "console":
        return console(args.url)

    script_path = REPO_ROOT / args._script
    if not script_path.exists():
        print(f"Missing script: {script_path}")
        return 1

    if script_path.suffix.lower() == ".ps1":
        return run_powershell(script_path, args.args)
    return run_python(script_path, args.args)


if __name__ == "__main__":
    raise SystemExit(main())

