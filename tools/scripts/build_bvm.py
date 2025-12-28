import argparse
import os
import re
import struct
import subprocess
import sys
import tarfile
import tempfile
import urllib.request
import zipfile
from pathlib import Path

MAGIC = 0x43534E45  # "CSNE"
VERSION_MAJOR = 1
VERSION_MINOR = 0

SECTION_READ = 1 << 0
SECTION_WRITE = 1 << 1
SECTION_EXEC = 1 << 2
SECTION_TEXT_HEX = 1 << 3
SECTION_TEXT_RAW = 1 << 4

HEADER_SIZE = 64
SECTION_SIZE = 40


def align8(value: int) -> int:
    return (value + 7) & ~7


def shutil_which(cmd: str) -> str | None:
    for path in os.getenv("PATH", "").split(os.pathsep):
        exe = Path(path) / cmd
        if exe.exists():
            return str(exe)
    return None


def platform_archive_name() -> str:
    if sys.platform.startswith("win"):
        return "Windows_64bit.zip"
    if sys.platform == "darwin":
        return "macOS_64bit.tar.gz"
    return "Linux_64bit.tar.gz"


def download_arduino_cli(install_dir: Path) -> Path:
    archive_name = platform_archive_name()
    url = f"https://downloads.arduino.cc/arduino-cli/arduino-cli_latest_{archive_name}"
    install_dir.mkdir(parents=True, exist_ok=True)
    exe_name = "arduino-cli.exe" if sys.platform.startswith("win") else "arduino-cli"

    with tempfile.TemporaryDirectory() as tmpdir:
        tmpdir_path = Path(tmpdir)
        archive_path = tmpdir_path / archive_name
        urllib.request.urlretrieve(url, archive_path)

        extract_dir = tmpdir_path / "extract"
        extract_dir.mkdir(parents=True, exist_ok=True)
        if archive_name.endswith(".zip"):
            with zipfile.ZipFile(archive_path, "r") as zf:
                zf.extractall(extract_dir)
        else:
            with tarfile.open(archive_path, "r:gz") as tf:
                tf.extractall(extract_dir)

        candidates = list(extract_dir.rglob(exe_name))
        if not candidates:
            raise RuntimeError("arduino-cli download did not contain expected binary")

        target = install_dir / exe_name
        target.write_bytes(candidates[0].read_bytes())
        if not sys.platform.startswith("win"):
            target.chmod(0o755)
        return target


def ensure_arduino_cli(auto_cli: bool, update_cli: bool, install_dir: Path) -> str:
    env_path = os.getenv("ARDUINO_CLI_PATH")
    if env_path and Path(env_path).exists():
        return env_path

    if not update_cli:
        path = shutil_which("arduino-cli") or shutil_which("arduino-cli.exe")
        if path:
            return path

        local = install_dir / ("arduino-cli.exe" if sys.platform.startswith("win") else "arduino-cli")
        if local.exists():
            return str(local)

    if not auto_cli:
        raise RuntimeError("arduino-cli not found in PATH; enable auto-install or set ARDUINO_CLI_PATH")

    return str(download_arduino_cli(install_dir))


def collect_includes(source_text: str) -> list[str]:
    includes = []
    for line in source_text.splitlines():
        match = re.match(r'\s*#\s*include\s+"([^"]+)"', line)
        if match:
            includes.append(match.group(1))
    return includes


def load_headers(source_path: Path, include_dirs: list[Path]) -> tuple[str, list[tuple[str, str]]]:
    visited = set()
    macro_list: list[tuple[str, str]] = []
    content_parts = []

    def resolve_header(header: str) -> Path | None:
        candidates = [source_path.parent / header] + [d / header for d in include_dirs]
        for candidate in candidates:
            if candidate.exists():
                return candidate
        return None

    def parse_file(path: Path):
        if path in visited:
            return
        visited.add(path)
        text = path.read_text(encoding="utf-8")
        content_parts.append(f"// {path.name}\n{text}\n")
        for line in text.splitlines():
            match = re.match(r'\s*#\s*define\s+(\w+)(.*)', line)
            if match:
                name = match.group(1)
                value = match.group(2).strip()
                macro_list.append((name, value))
        for header in collect_includes(text):
            resolved = resolve_header(header)
            if resolved:
                parse_file(resolved)

    parse_file(source_path)
    macros_blob = "\n".join(f"{name}={value}" for name, value in macro_list)
    headers_blob = "\n".join(content_parts)
    combined = f"[MACROS]\n{macros_blob}\n\n[HEADERS]\n{headers_blob}"
    return combined, macro_list


def read_file(path: Path) -> bytes:
    return path.read_bytes()


def make_section(name: str, data: bytes, flags: int) -> dict:
    return {
        "name": name.encode("ascii")[:8].ljust(8, b"\x00"),
        "data": data,
        "flags": flags,
    }


def build_bvm(sections: list[dict], entry_offset: int) -> bytes:
    section_count = len(sections)
    section_table_offset = HEADER_SIZE
    data_offset = align8(section_table_offset + section_count * SECTION_SIZE)

    offsets = []
    cursor = data_offset
    for sec in sections:
        cursor = align8(cursor)
        offsets.append(cursor)
        cursor += len(sec["data"])

    header = struct.pack(
        "<IHHIIQQQQQQ",
        MAGIC,
        VERSION_MAJOR,
        VERSION_MINOR,
        HEADER_SIZE,
        section_count,
        entry_offset,
        section_table_offset,
        0,
        0,
        0,
        0,
    )

    table = bytearray()
    for sec, offset in zip(sections, offsets):
        table.extend(struct.pack("<8sQQQQ", sec["name"], offset, len(sec["data"]), sec["flags"], 0))

    blob = bytearray()
    blob.extend(header)
    blob.extend(table)
    if len(blob) < data_offset:
        blob.extend(b"\x00" * (data_offset - len(blob)))

    for sec, offset in zip(sections, offsets):
        if len(blob) < offset:
            blob.extend(b"\x00" * (offset - len(blob)))
        blob.extend(sec["data"])
    return bytes(blob)


def compile_with_arduino_cli(ino_path: Path, fqbn: str, build_dir: Path, auto_cli: bool, update_cli: bool, install_dir: Path) -> Path:
    cli = ensure_arduino_cli(auto_cli, update_cli, install_dir)

    build_dir.mkdir(parents=True, exist_ok=True)
    cmd = [
        cli,
        "compile",
        "--fqbn",
        fqbn,
        "--build-path",
        str(build_dir),
        str(ino_path),
    ]
    subprocess.check_call(cmd)
    hex_files = list(build_dir.glob("*.hex"))
    if not hex_files:
        raise RuntimeError("arduino-cli compile succeeded but no .hex found")
    return hex_files[0]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--hex", type=Path, help="Input Intel HEX file")
    parser.add_argument("--ino", type=Path, help="Input Arduino .ino file")
    parser.add_argument("--fqbn", type=str, help="Arduino FQBN (e.g., arduino:avr:uno)")
    parser.add_argument("--include", action="append", type=Path, default=[])
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--entry", type=int, default=0)
    parser.add_argument("--raw-text", action="store_true", help="Treat .text as raw binary instead of HEX")
    parser.add_argument("--no-auto-cli", action="store_true", help="Disable automatic Arduino CLI download")
    parser.add_argument("--update-cli", action="store_true", help="Force Arduino CLI download/update")
    args = parser.parse_args()

    if not args.hex and not args.ino:
        raise SystemExit("Provide --hex or --ino")

    if args.ino and not args.fqbn:
        raise SystemExit("--ino requires --fqbn")

    auto_cli = not args.no_auto_cli
    repo_root = Path(__file__).resolve().parents[2]
    install_dir = repo_root / "tools" / "arduino-cli"

    if args.ino:
        build_dir = args.out.parent / "bvm_build"
        args.hex = compile_with_arduino_cli(args.ino, args.fqbn, build_dir, auto_cli, args.update_cli, install_dir)

    text_data = read_file(args.hex)
    text_flags = SECTION_READ | SECTION_EXEC | (SECTION_TEXT_RAW if args.raw_text else SECTION_TEXT_HEX)

    rodata = b""
    if args.ino:
        rodata_str, _ = load_headers(args.ino, args.include)
        rodata = rodata_str.encode("utf-8")

    sections = [make_section(".text", text_data, text_flags)]
    if rodata:
        sections.append(make_section(".rodata", rodata, SECTION_READ))

    bvm_blob = build_bvm(sections, args.entry)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_bytes(bvm_blob)
    print(f"Wrote {args.out} ({len(bvm_blob)} bytes)")


if __name__ == "__main__":
    main()
