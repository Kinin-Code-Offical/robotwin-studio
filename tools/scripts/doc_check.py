"""Keeps the documentation true to the code it describes.

  1. Code paths: every `path/with/a/slash.ext` written in backticks names a file that exists (a shortened path matches
     its end, `CoreSim/.../File.cs` any folder in between).
  2. The .NET version: `.NET N` and `netN.0` say the version the projects target.
  3. Menu paths: each step of a **A → B → C** path in the user guide is a text the app shows (the Turkish guide against
     the app's sources, the English guide against the English language pack as well).

Exit code 1 with --check when anything is off (CI). Intentional exceptions are listed in IGNORE below, with why.

    python tools/scripts/doc_check.py [--check]
"""
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]

# Not files of this repository, on purpose.
IGNORE_PATHS = {
    # entries inside a .rwproj / .rwworld container (described in the architecture docs)
    "designs/index.json", "firmware/index.json", "library/parts.json", "mechanics/assembly.json", "pcb/board.json",
    # ESP-IDF's own headers, which the hardware-compatible mode's firmware includes
    "driver/mcpwm.h", "driver/ledc.h", "driver/gpio.h", "driver/uart.h", "driver/i2c.h", "driver/rmt.h", "driver/pcnt.h",
}

# Menu steps that are not RobotWin's (Windows' own settings, keys) or are written around a step.
IGNORE_STEPS = {
    "Uygulamalar", "RobotWin Studio", "Değiştir", "Apps", "Modify", "COMx", "Ek bilgi", "More info", "Yine de çalıştır", "Run anyway",
    # a status line the app writes with numbers in it ("NET → osiloskop CH1")
    "osiloskop CH1", "oscilloscope CH1",
}

DOCS = [p for p in ROOT.glob("docs/**/*.md") if "archive" not in p.parts] + [ROOT / n for n in ("README.md", "CONTRIBUTING.md", "SECURITY.md")]
SKIP_VERSION = {"CHANGELOG.md", "11-yenilikler.md", "11-whats-new.md", "YOL_HARITASI.md", "ROADMAP.md"}   # history may name older versions


def repo_files():
    out = []
    for p in ROOT.rglob("*"):
        if not p.is_file():
            continue
        parts = set(p.relative_to(ROOT).parts)
        if parts & {"builds", ".git", "node_modules", "bin", "obj", ".o"}:
            continue
        out.append(str(p.relative_to(ROOT)).replace("\\", "/"))
    return out


def exists(path, files):
    if "..." in path:
        head, tail = path.split("...", 1)
        head, tail = head.rstrip("/"), tail.lstrip("/")
        return any(f.startswith(head) and f.endswith(tail) for f in files)
    return any(f == path or f.endswith("/" + path) for f in files)


def target_major():
    text = (ROOT / "Studio" / "RobotWin.Studio" / "RobotWin.Studio.csproj").read_text(encoding="utf-8")
    m = re.search(r"<TargetFramework>net(\d+)\.0", text)
    return int(m.group(1)) if m else 0


def corpus(paths):
    text = []
    for base in paths:
        for p in (ROOT / base).rglob("*"):
            if p.suffix in (".cs", ".xaml") and "builds" not in p.parts:
                text.append(p.read_text(encoding="utf-8", errors="replace"))
    return "\n".join(text)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    check = "--check" in sys.argv[1:]
    files = repo_files()
    major = target_major()
    problems = []
    code = re.compile(r"`([A-Za-z0-9_.\-/\\]+\.(?:cs|cpp|h|py|ps1|json|xaml|md|csproj|bat|yml))`")
    dotnet = re.compile(r"\.NET (\d+)(?![\d.])")
    tfm = re.compile(r"\bnet(\d+)\.0(?:-windows)?\b")
    bold = re.compile(r"\*\*(.+?)\*\*")
    source = corpus(["Studio/RobotWin.Studio", "CoreSim/src"])
    english = json.loads((ROOT / "Studio" / "RobotWin.Studio" / "Lang" / "en.json").read_text(encoding="utf-8"))
    english_text = source + "\n" + "\n".join(english.get("strings", {}).values()) + "\n" + "\n".join(english.get("patterns", {}).values())

    for doc in DOCS:
        rel = str(doc.relative_to(ROOT)).replace("\\", "/")
        text = doc.read_text(encoding="utf-8", errors="replace")
        lines = text.split("\n")
        for no, line in enumerate(lines, 1):
            for m in code.finditer(line):
                path = m.group(1).replace("\\", "/")
                if "/" in path and path not in IGNORE_PATHS and not exists(path, files):
                    problems.append(f"{rel}:{no}: `{path}` is not a file of the repository")
            if doc.name not in SKIP_VERSION:
                for m in dotnet.finditer(line):
                    if int(m.group(1)) != major and int(m.group(1)) >= 5:
                        problems.append(f"{rel}:{no}: '.NET {m.group(1)}' but the projects target .NET {major}")
                for m in tfm.finditer(line):
                    if int(m.group(1)) != major and int(m.group(1)) >= 5:
                        problems.append(f"{rel}:{no}: 'net{m.group(1)}.0' but the projects target net{major}.0")
            if doc.parent.name in ("kullanim", "guide"):
                known = english_text if doc.parent.name == "guide" else source
                for m in bold.finditer(line):
                    if "→" not in m.group(1):
                        continue
                    # a step in parentheses stands for a name the app fills in ("Kayış tak → (öteki kasnak)")
                    steps = [s.strip().strip("…").strip(".,:;()").strip() for s in m.group(1).split("→") if not s.strip().startswith("(")]
                    if any(len(s) > 40 or ". " in s for s in steps):
                        continue   # prose with an arrow in it, not a menu path
                    for step in steps:
                        if len(step) < 2 or step in IGNORE_STEPS or step.isdigit() or re.match(r"^\d+\. ", step):
                            continue   # a guide chapter ("11. Yenilikler") is the help page's, from the guide itself
                        if step not in known and step + "…" not in known:
                            problems.append(f"{rel}:{no}: menu step '{step}' is not a text the app shows")

    for p in problems:
        print(("::error::" if check else "") + p)
    print(f"{len(DOCS)} documents checked, {len(problems)} problem(s)")
    sys.exit(1 if check and problems else 0)


if __name__ == "__main__":
    main()
