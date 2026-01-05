import argparse
import subprocess
import sys
import time
from pathlib import Path

import urllib

REPO_ROOT = Path(__file__).resolve().parents[1]


def run_command(cmd: list[str], *, cwd: Path = REPO_ROOT) -> int:
    try:
        return subprocess.call(cmd, cwd=str(cwd))
    except KeyboardInterrupt:
        return 130


def try_version(cmd: list[str]) -> tuple[bool, str]:
    try:
        res = subprocess.run(
            cmd,
            cwd=str(REPO_ROOT),
            capture_output=True,
            text=True,
            check=False,
        )
    except FileNotFoundError:
        return False, ""

    out = (res.stdout or "") + (res.stderr or "")
    return res.returncode == 0, out.strip()


def _print_versions(title: str, commands: dict[str, list[str]], *, track_missing: bool) -> list[str]:
    missing: list[str] = []
    print(f"\n{title}:")
    for name, cmd in commands.items():
        ok, output = try_version(cmd)
        status = "OK" if ok else "MISSING"
        detail = output.splitlines()[0] if output else ""
        print(f"- {name}: {status} {detail}".rstrip())
        if track_missing and not ok:
            missing.append(name)
    return missing


def _read_unity_editor_version() -> str | None:
    unity_ver_file = REPO_ROOT / "RobotWin" / "ProjectSettings" / "ProjectVersion.txt"
    if not unity_ver_file.exists():
        return None
    content = unity_ver_file.read_text(encoding="utf-8", errors="ignore")
    for line in content.splitlines():
        line = line.strip()
        if line.startswith("m_EditorVersion:"):
            return line
    return None


def _print_unity_version() -> None:
    if editor_version := _read_unity_editor_version():
        print("\nUnity:")
        print(f"- {editor_version}")


def _print_missing_and_hint(missing: list[str]) -> None:
    print("\nMissing required prerequisites:")
    for item in missing:
        print(f"- {item}")
    print("\nSee docs/SETUP_WINDOWS.md for installation steps.")


def _run_step(label: str, cmd: list[str]) -> int:
    print(f"\nRunning: {label}")
    return run_command(cmd)


def _run_rt_tool(*args: str) -> int:
    return _run_step(" ".join(args), [sys.executable, "tools/rt_tool.py", *args])


def _required_prereqs() -> dict[str, list[str]]:
    return {
        "Git": ["git", "--version"],
        ".NET SDK": ["dotnet", "--version"],
        "Python": [sys.executable, "--version"],
        "CMake": ["cmake", "--version"],
        "C++ compiler (g++)": ["g++", "--version"],
    }


def _optional_prereqs() -> dict[str, list[str]]:
    return {
        "Node.js": ["node", "--version"],
        "npm": ["npm", "--version"],
    }


def _check_prereqs(*, run_qa: bool) -> int:
    missing = _print_versions("Prerequisites", _required_prereqs(), track_missing=True)
    _print_versions("Optional", _optional_prereqs(), track_missing=False)
    _print_unity_version()
    if missing:
        _print_missing_and_hint(missing)
        return 1

    if run_qa:
        ok, _ = try_version(["npm", "--version"])
        if not ok:
            print("\nMissing required prerequisite for --qa: npm")
            return 1

    return 0


def _run_coresim_tests() -> int:
    code = _run_step("dotnet restore CoreSim/CoreSim.sln", ["dotnet", "restore", "CoreSim/CoreSim.sln"])
    if code != 0:
        return code

    return _run_step(
        "dotnet test CoreSim/CoreSim.sln",
        ["dotnet", "test", "CoreSim/CoreSim.sln", "--no-restore", "--verbosity", "minimal"],
    )


def _run_repo_validations(*, sync_unity_plugins: bool) -> int:
    code = _run_rt_tool("validate-uxml")
    if code != 0:
        return code

    return _run_rt_tool("update-unity-plugins") if sync_unity_plugins else 0


def _run_builds(*, build_native: bool, build_firmware: bool, configuration: str) -> int:
    if build_native:
        code = _run_rt_tool("build-native")
        if code != 0:
            return code

    if build_firmware:
        cmd = ["build-firmware"]
        if configuration and configuration != "Release":
            cmd.append(configuration)
        code = _run_rt_tool(*cmd)
        if code != 0:
            return code

    return 0


def _run_optional_steps(*, run_qa: bool, build_standalone: bool, unity_path: str | None) -> int:
    if run_qa:
        code = _run_rt_tool("run-qa")
        if code != 0:
            return code

    if build_standalone:
        cmd = [sys.executable, "tools/rt_tool.py", "build-standalone"]
        if unity_path:
            cmd += ["-UnityPath", unity_path]
        code = _run_step("build-standalone", cmd)
        if code != 0:
            return code

    return 0


def _run_com0com_install(*, install_com0com: bool) -> int:
    if not install_com0com:
        return 0

    script = REPO_ROOT / "tools" / "scripts" / "install_com0com_ports.ps1"
    if not script.exists():
        print(f"\nMissing script: {script}")
        return 1

    print("\nRunning: install_com0com_ports.ps1 (may require admin)")
    return run_powershell(script, [])


def windows_setup(
    check_only: bool,
    run_qa: bool,
    install_com0com: bool,
    build_native: bool,
    build_firmware: bool,
    sync_unity_plugins: bool,
    build_standalone: bool,
    unity_path: str | None,
    configuration: str,
) -> int:
    print("== RobotWin Studio Setup (Windows) ==")
    print(f"Repo: {REPO_ROOT}")

    prereq_code = _check_prereqs(run_qa=run_qa)
    if prereq_code != 0:
        return prereq_code
    if check_only:
        print("\nCheck-only complete.")
        return 0

    if (code := _run_coresim_tests()) != 0:
        return code
    if (code := _run_repo_validations(sync_unity_plugins=sync_unity_plugins)) != 0:
        return code
    if (code := _run_builds(build_native=build_native, build_firmware=build_firmware, configuration=configuration)) != 0:
        return code
    if (code := _run_optional_steps(run_qa=run_qa, build_standalone=build_standalone, unity_path=unity_path)) != 0:
        return code
    if (code := _run_com0com_install(install_com0com=install_com0com)) != 0:
        return code

    print("\nSetup complete.")
    return 0


def run_powershell(script_path: Path, args: list[str]) -> int:
    cmd = [
        "powershell",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        str(script_path),
        *args,
    ]
    try:
        return subprocess.call(cmd, cwd=str(REPO_ROOT))
    except KeyboardInterrupt:
        return 130


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
    "build-template-bvms": "Build .bvm outputs for all Arduino templates into each template's builds folder.",
    "migrate-template-specs": "Generate template.json specs (with legacy aliases) from metadata.json.",
    "build-standalone": "Build Unity Windows player via batchmode.",
    "update-unity-plugins": "Build CoreSim .NET plugin and sync into RobotWin/Assets/Plugins.",
    "update-repo-snapshot": "Refresh docs/repo_files.txt, workspace snapshot, and README folder tree.",
    "validate-uxml": "Parse and validate all .uxml files.",
    "run-qa": "Run Node/Jest integration tests.",
    "run-unity-smoke": "Batchmode Unity compile smoke test.",
    "monitor-unity": "Tail logs/unity/unity_live_error.log and highlight errors.",
    "console": "Interactive HTTP console for RemoteCommandServer.",
    "debug-console": "Launch the Debug Console web dashboard.",
    "setup": "Verify prerequisites and bootstrap dev setup (tests + validations + builds).",
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
    add_script_command("build-template-bvms", "tools/scripts/build_template_bvms.ps1", "Build template .bvm outputs")
    add_script_command("migrate-template-specs", "tools/scripts/migrate_template_specs.py", "Migrate template specs")
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
    add_script_command("debug-console", "tools/debug_console/launch_debug_console.ps1", "Launch Debug Console dashboard")
    setup_parser = subparsers.add_parser("setup", help="Verify prerequisites and bootstrap local dev")
    setup_parser.add_argument("--check-only", action="store_true", help="Only verify prerequisites")
    setup_parser.add_argument("--qa", action="store_true", help="Also run integration tests (Node/Jest)")
    setup_parser.add_argument("--install-com0com", action="store_true", help="Also run com0com port installer (admin may be required)")
    setup_parser.add_argument("--no-native", action="store_true", help="Skip NativeEngine build")
    setup_parser.add_argument("--no-firmware", action="store_true", help="Skip FirmwareEngine build")
    setup_parser.add_argument("--no-plugins", action="store_true", help="Skip CoreSim Unity plugin sync")
    setup_parser.add_argument("--standalone", action="store_true", help="Also build Unity Windows standalone player")
    setup_parser.add_argument("--unity-path", help="Unity.exe path for --standalone (overrides UNITY_PATH)")
    setup_parser.add_argument("--configuration", default="Release", choices=["Release", "Debug"], help="Build configuration for firmware")
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
    if args.command == "setup":
        return windows_setup(
            check_only=args.check_only,
            run_qa=args.qa,
            install_com0com=args.install_com0com,
            build_native=not args.no_native,
            build_firmware=not args.no_firmware,
            sync_unity_plugins=not args.no_plugins,
            build_standalone=args.standalone,
            unity_path=args.unity_path,
            configuration=args.configuration,
        )

    script_path = REPO_ROOT / args._script
    if not script_path.exists():
        print(f"Missing script: {script_path}")
        return 1

    if script_path.suffix.lower() == ".ps1":
        return run_powershell(script_path, args.args)
    return run_python(script_path, args.args)


if __name__ == "__main__":
    raise SystemExit(main())

