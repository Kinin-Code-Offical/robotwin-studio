from __future__ import annotations

from pathlib import Path
import sys


REPO_ROOT = Path(__file__).resolve().parents[2]
LOG_DIR = REPO_ROOT / "logs" / "tools"
LOG_FILE = LOG_DIR / "build_output_audit.log"

EXPECTED = {
    "tools/scripts/build_firmware.ps1": ["builds/firmware", "logs/firmware"],
    "tools/scripts/build_firmware_monitor.ps1": ["builds/RobotWinFirmwareMonitor", "logs/RobotWinFirmwareMonitor"],
    "tools/scripts/build_native.ps1": ["builds/native"],
    "tools/scripts/build_windows_standalone.ps1": ["builds/windows", "logs/unity"],
    "tools/scripts/build_template_bvms.ps1": ["logs/tools", "builds"],
}


def normalize(text: str) -> str:
    cleaned = text.replace("\\", "/")
    while "//" in cleaned:
        cleaned = cleaned.replace("//", "/")
    return cleaned


def audit_script(path: Path, expected_tokens: list[str]) -> list[str]:
    try:
        content = normalize(path.read_text(encoding="utf-8", errors="ignore"))
    except OSError:
        return [f"{path}: unreadable"]

    issues: list[str] = []
    for token in expected_tokens:
        if token not in content:
            issues.append(f"{path}: missing token '{token}'")
    return issues


def main() -> int:
    issues: list[str] = []
    results: list[str] = []

    for rel_path, tokens in EXPECTED.items():
        path = REPO_ROOT / rel_path
        if not path.exists():
            issue = f"{path}: missing file"
            issues.append(issue)
            results.append(f"WARN {issue}")
            continue
        missing = audit_script(path, tokens)
        if missing:
            issues.extend(missing)
            results.extend([f"FAIL {item}" for item in missing])
        else:
            results.append(f"OK {path}")

    LOG_DIR.mkdir(parents=True, exist_ok=True)
    LOG_FILE.write_text("\n".join(results) + "\n", encoding="utf-8")

    if issues:
        print("Build output audit failed:")
        for issue in issues:
            print(f"- {issue}")
        print(f"Log: {LOG_FILE}")
        return 1

    print(f"Build output audit passed. Log: {LOG_FILE}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
