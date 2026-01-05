from __future__ import annotations

import argparse
from pathlib import Path
import sys


REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SCAN_ROOT = REPO_ROOT / "RobotWin" / "Assets" / "Scripts"
DISALLOWED_TOKENS = ("System.Windows.Forms",)
EDITOR_ONLY_TOKEN = "UnityEditor"
EDITOR_GUARD = "#if UNITY_EDITOR"


def is_editor_path(path: Path) -> bool:
    return "Editor" in path.parts


def scan_file(path: Path) -> list[str]:
    try:
        content = path.read_text(encoding="utf-8", errors="ignore")
    except OSError:
        return [f"{path}: unreadable"]

    issues: list[str] = []
    for token in DISALLOWED_TOKENS:
        if token in content:
            issues.append(f"{path}: disallowed token '{token}'")

    if EDITOR_ONLY_TOKEN in content and not is_editor_path(path):
        if EDITOR_GUARD not in content:
            issues.append(f"{path}: '{EDITOR_ONLY_TOKEN}' without UNITY_EDITOR guard")

    return issues


def main() -> int:
    parser = argparse.ArgumentParser(description="Audit runtime scripts for editor-only APIs.")
    parser.add_argument("--root", type=Path, default=DEFAULT_SCAN_ROOT, help="Root directory to scan")
    args = parser.parse_args()

    root = args.root
    if not root.exists():
        print(f"Missing scan root: {root}")
        return 1

    issues: list[str] = []
    for path in root.rglob("*.cs"):
        if is_editor_path(path):
            continue
        issues.extend(scan_file(path))

    if issues:
        print("Runtime API audit failed:")
        for issue in issues:
            print(f"- {issue}")
        return 1

    print(f"Runtime API audit passed ({root})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
