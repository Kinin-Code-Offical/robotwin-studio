import argparse
import os
import subprocess
import sys
import time
import urllib.request
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]

ICON_MAP = {
    "home.png": "https://img.icons8.com/ios-glyphs/64/ffffff/home.png",
    "folder.png": "https://img.icons8.com/ios-glyphs/64/ffffff/folder-invoices.png",
    "settings.png": "https://img.icons8.com/ios-glyphs/64/ffffff/gear.png",
    "plus.png": "https://img.icons8.com/ios-glyphs/64/ffffff/plus-math.png",
    "search.png": "https://img.icons8.com/ios-glyphs/64/ffffff/search--v1.png",
    "menu.png": "https://img.icons8.com/ios-glyphs/64/ffffff/menu-vertical.png",
    "cpu.png": "https://img.icons8.com/ios-glyphs/64/ffffff/processor.png",
    "box.png": "https://img.icons8.com/ios-glyphs/64/ffffff/box.png",
    "activity.png": "https://img.icons8.com/ios-glyphs/64/ffffff/activity-history.png",
    "import.png": "https://img.icons8.com/ios-glyphs/64/ffffff/import.png",
    "copy.png": "https://img.icons8.com/ios-glyphs/64/ffffff/copy.png",
    "trash-2.png": "https://img.icons8.com/ios-glyphs/64/ffffff/trash.png",
    "filter.png": "https://img.icons8.com/ios-glyphs/64/ffffff/filter.png",
    "layout-template.png": "https://img.icons8.com/ios-glyphs/64/ffffff/layout.png",
    "more-vertical.png": "https://img.icons8.com/ios-glyphs/64/ffffff/menu-vertical.png",
    "arrow-right.png": "https://img.icons8.com/ios-glyphs/64/ffffff/arrow.png",
    "hexagon.png": "https://img.icons8.com/ios-glyphs/64/ffffff/hexagon.png",
    "zap.png": "https://img.icons8.com/ios-glyphs/64/ffffff/lightning-bolt.png",
    "clock.png": "https://img.icons8.com/ios-glyphs/64/ffffff/clock.png",
    "external-link.png": "https://img.icons8.com/ios-glyphs/64/ffffff/external-link.png",
    "play.png": "https://img.icons8.com/ios-glyphs/64/ffffff/play--v1.png",
    "pause.png": "https://img.icons8.com/ios-glyphs/64/ffffff/pause--v1.png",
    "stop.png": "https://img.icons8.com/ios-glyphs/64/ffffff/stop.png",
    "step.png": "https://img.icons8.com/ios-glyphs/64/ffffff/end.png",
    "console.png": "https://img.icons8.com/ios-glyphs/64/ffffff/console.png",
    "inspector.png": "https://img.icons8.com/ios-glyphs/64/ffffff/info.png",
    "hierarchy.png": "https://img.icons8.com/ios-glyphs/64/ffffff/list.png",
}


def run_powershell(script_path: Path, args: list[str]) -> int:
    cmd = [
        "powershell",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        str(script_path),
    ]
    cmd.extend(args)
    return subprocess.call(cmd, cwd=str(REPO_ROOT))


def run_python(script_path: Path, args: list[str]) -> int:
    cmd = [sys.executable, str(script_path)]
    cmd.extend(args)
    return subprocess.call(cmd, cwd=str(REPO_ROOT))


def fetch_icons() -> int:
    icons_dir = REPO_ROOT / "UnityApp" / "Assets" / "UI" / "Icons"
    icons_dir.mkdir(parents=True, exist_ok=True)

    opener = urllib.request.build_opener()
    opener.addheaders = [("User-agent", "Mozilla/5.0")]
    urllib.request.install_opener(opener)

    for filename, url in ICON_MAP.items():
        dest = icons_dir / filename
        try:
            print(f"Downloading {filename}...")
            urllib.request.urlretrieve(url, dest)
        except Exception as exc:
            print(f"Failed: {filename} ({exc})")
            return 1

    print(f"Icons downloaded to {icons_dir}")
    return 0


def verify_assets() -> int:
    icon_path = REPO_ROOT / "UnityApp" / "Assets" / "UI" / "Icons" / "home.png"
    if icon_path.exists():
        print("Icons verified.")
        return 0
    print("Icons missing. Run: python tools/rt_tool.py fetch-icons")
    return 1


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
    "build-native": "Build NativeEngine DLL and standalone (g++). Outputs to build/native.",
    "build-standalone": "Build Unity Windows player via batchmode.",
    "update-unity-plugins": "Build CoreSim .NET plugin and sync into UnityApp/Assets/Plugins.",
    "update-repo-files": "Refresh docs/repo_files.txt inventory.",
    "workspace-snapshot": "Write a workspace snapshot to logs/tools/workspace_snapshot.txt.",
    "validate-uxml": "Parse and validate all .uxml files.",
    "run-qa": "Run Node/Jest integration tests.",
    "run-unity-smoke": "Batchmode Unity compile smoke test.",
    "fetch-icons": "Download UI icon PNGs into UnityApp/Assets/UI/Icons.",
    "verify-assets": "Check required UI assets exist.",
    "monitor-unity": "Tail logs/unity/unity_live_error.log and highlight errors.",
    "console": "Interactive HTTP console for RemoteCommandServer.",
    "mission-control": "Launch the Mission Control dashboard.",
    "export-context": "Create a full context export package.",
    "export-context-min": "Create a minimal context export package.",
    "shared-info": "Show latest shared info snapshot.",
    "end-session": "Finalize and write shared info summary.",
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
    add_script_command("build-native", "tools/scripts/build_native.ps1", "Build NativeEngine DLL + standalone")
    add_script_command("build-standalone", "tools/scripts/build_windows_standalone.ps1", "Build Unity Windows player")
    add_script_command("update-unity-plugins", "tools/scripts/update_unity_plugins.ps1", "Build CoreSim and sync Unity plugins")
    add_script_command("update-repo-files", "tools/scripts/update_repo_files.ps1", "Refresh docs/repo_files.txt")
    add_script_command("workspace-snapshot", "tools/scripts/update_workspace_snapshot.ps1", "Write workspace snapshot")
    add_script_command("validate-uxml", "tools/scripts/validate_uxml.ps1", "Validate UXML files")
    add_script_command("run-qa", "tools/scripts/run_qa.ps1", "Run integration tests (Node/Jest)")
    add_script_command("run-unity-smoke", "tools/scripts/run_unity_smoke.ps1", "Run Unity batchmode smoke test")
    add_script_command("mission-control", "tools/mission_control/launch_mission_control.ps1", "Launch Mission Control dashboard")
    add_script_command("export-context", "tools/context/export_context_pack.ps1", "Export full context pack")
    add_script_command("export-context-min", "tools/context/export_context_pack_min.ps1", "Export minimal context pack")
    add_script_command("shared-info", "tools/context/get_latest_shared_info.ps1", "Print latest shared info")
    add_script_command("end-session", "tools/context/end_session_shared_info.ps1", "Finalize session shared info")

    subparsers.add_parser("fetch-icons", help="Download UI icon PNGs")
    subparsers.add_parser("verify-assets", help="Verify required UI assets")
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

    if args.command == "fetch-icons":
        return fetch_icons()
    if args.command == "verify-assets":
        return verify_assets()
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
