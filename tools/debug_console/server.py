import argparse
import json
import os
import platform
import subprocess
import sys
import time
import urllib.request
from http import HTTPStatus
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse


REPO_ROOT = Path(__file__).resolve().parents[2]
STATIC_DIR = REPO_ROOT / "tools" / "debug_console" / "public"
LOG_DIR = REPO_ROOT / "logs" / "debug_console"
START_TIME = time.time()

LOG_AREAS = {
    "debug_console": LOG_DIR,
    "unity": REPO_ROOT / "logs" / "unity",
    "firmware": REPO_ROOT / "logs" / "firmware",
    "native": REPO_ROOT / "logs" / "native",
    "tools": REPO_ROOT / "logs" / "tools",
}

UNITY_PORTS = list(range(8085, 8095))

COM_PORTS_COMMAND = [
    "powershell",
    "-NoProfile",
    "-Command",
    "Get-CimInstance Win32_SerialPort | Select-Object DeviceID,Name,Description,PNPDeviceID | ConvertTo-Json"
]


def now_stamp() -> str:
    return time.strftime("%Y%m%d_%H%M%S")


def safe_name(value: str) -> str:
    keep = []
    for ch in value:
        if ch.isalnum() or ch in ("-", "_"):
            keep.append(ch)
        else:
            keep.append("_")
    return "".join(keep)


def run_command(name: str, command: list[str]) -> dict:
    LOG_DIR.mkdir(parents=True, exist_ok=True)
    stamp = now_stamp()
    log_name = f"{stamp}_{safe_name(name)}.log"
    log_path = LOG_DIR / log_name

    start = time.time()
    result = subprocess.run(
        command,
        cwd=str(REPO_ROOT),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace"
    )
    duration = time.time() - start

    output = (result.stdout or "") + (result.stderr or "")
    log_path.write_text(output, encoding="utf-8", errors="ignore")

    return {
        "name": name,
        "exit_code": result.returncode,
        "duration_sec": round(duration, 2),
        "log_file": log_name,
        "output": output,
    }


TESTS = [
    {
        "name": "core-sim-tests",
        "label": "CoreSim Tests",
        "description": "Run .NET tests for CoreSim.",
        "command": ["dotnet", "test", "CoreSim/tests/RobotTwin.CoreSim.Tests/RobotTwin.CoreSim.Tests.csproj"],
    },
    {
        "name": "physics-tests",
        "label": "Physics Tests",
        "description": "Run physics-focused CoreSim tests.",
        "command": ["dotnet", "test", "CoreSim/tests/RobotTwin.CoreSim.Tests/RobotTwin.CoreSim.Tests.csproj", "--filter", "Category=Physics"],
    },
    {
        "name": "unity-smoke",
        "label": "Unity Smoke Test",
        "description": "Batchmode Unity compile smoke test.",
        "command": [sys.executable, "tools/rt_tool.py", "run-unity-smoke"],
    },
    {
        "name": "build-native",
        "label": "Build Native Engine",
        "description": "Build native C++ engine artifacts.",
        "command": [sys.executable, "tools/rt_tool.py", "build-native"],
    },
    {
        "name": "build-firmware",
        "label": "Build Firmware Engine",
        "description": "Build RoboTwinFirmwareHost.exe.",
        "command": [sys.executable, "tools/rt_tool.py", "build-firmware"],
    },
    {
        "name": "run-qa",
        "label": "Integration QA Tests",
        "description": "Run Node/Jest integration tests.",
        "command": [sys.executable, "tools/rt_tool.py", "run-qa"],
    },
    {
        "name": "validate-physical-overrides",
        "label": "HF-06 Physical Overrides Validation",
        "description": "Validate HF-06 sample component package.",
        "command": [sys.executable, "tools/rt_tool.py", "validate-physical-overrides"],
    },
]


def format_bytes(value: int) -> str:
    size = float(value)
    for suffix in ("B", "KB", "MB", "GB"):
        if size < 1024.0:
            return f"{size:.1f} {suffix}"
        size /= 1024.0
    return f"{size:.1f} TB"


def dir_size(path: Path) -> int:
    if not path.exists():
        return 0
    total = 0
    for root, _, files in os.walk(path):
        for name in files:
            try:
                total += (Path(root) / name).stat().st_size
            except OSError:
                continue
    return total


def get_system_info() -> dict:
    uptime = time.time() - START_TIME
    unity_url = find_unity_base_url()
    logs_size = dir_size(REPO_ROOT / "logs")
    builds_size = dir_size(REPO_ROOT / "builds")
    return {
        "server_time": time.strftime("%Y-%m-%d %H:%M:%S", time.localtime()),
        "platform": platform.platform(),
        "python": platform.python_version(),
        "repo": str(REPO_ROOT),
        "logs_size": format_bytes(logs_size),
        "builds_size": format_bytes(builds_size),
        "unity_base_url": unity_url or "",
        "uptime": f"{uptime:.0f}s",
    }


def list_logs(area: str) -> list[dict]:
    base = LOG_AREAS.get(area)
    if not base or not base.exists():
        return []
    items = []
    for path in base.glob("*.log"):
        stat = path.stat()
        items.append(
            {
                "name": path.name,
                "size": stat.st_size,
                "modified_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(stat.st_mtime)),
            }
        )
    return sorted(items, key=lambda x: x["modified_utc"], reverse=True)


def read_log(area: str, name: str, tail: int | None) -> str:
    base = LOG_AREAS.get(area)
    if not base or not base.exists():
        return ""
    path = (base / name).resolve()
    if base.resolve() not in path.parents:
        return ""
    if not path.exists():
        return ""
    content = path.read_text(encoding="utf-8", errors="ignore")
    if tail is None:
        return content
    lines = content.splitlines()
    return "\n".join(lines[-tail:])


def classify_line(line: str) -> str | None:
    lowered = line.lower()
    if "error" in lowered or "exception" in lowered or "fatal" in lowered or "fail" in lowered:
        return "error"
    if "warn" in lowered:
        return "warning"
    if "info" in lowered:
        return "info"
    return None


def scan_logs(tail: int) -> list[dict]:
    alerts: list[dict] = []
    for area, base in LOG_AREAS.items():
        if not base.exists():
            continue
        for path in sorted(base.glob("*.log"), key=lambda p: p.stat().st_mtime, reverse=True)[:5]:
            content = read_log(area, path.name, tail)
            if not content:
                continue
            for line in content.splitlines():
                level = classify_line(line)
                if not level:
                    continue
                alerts.append(
                    {
                        "area": area,
                        "log": path.name,
                        "level": level,
                        "message": line.strip(),
                        "timestamp": time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(path.stat().st_mtime)),
                    }
                )
    return alerts[:200]


def get_com_ports() -> list[dict]:
    try:
        result = subprocess.run(
            COM_PORTS_COMMAND,
            capture_output=True,
            text=True
        )
        if result.returncode != 0 or not result.stdout:
            return []
        payload = json.loads(result.stdout)
    except Exception:
        return []

    items = [payload] if isinstance(payload, dict) else payload or []
    ports = []
    for item in items:
        device_id = item.get("DeviceID", "")
        name = item.get("Name", "")
        description = item.get("Description", "")
        pnp = item.get("PNPDeviceID", "")
        is_virtual = "com0com" in pnp.lower() or "cncb" in pnp.lower()
        ports.append(
            {
                "device_id": device_id,
                "name": name,
                "description": description,
                "pnp_device_id": pnp,
                "is_virtual": is_virtual,
            }
        )
    return ports


def find_unity_base_url() -> str | None:
    for port in UNITY_PORTS:
        url = f"http://localhost:{port}"
        try:
            with urllib.request.urlopen(f"{url}/status", timeout=0.5) as res:
                if res.status == 200:
                    return url
        except Exception:
            continue
    return None


def fetch_unity_json(path: str) -> dict | None:
    base = find_unity_base_url()
    if not base:
        return None
    try:
        with urllib.request.urlopen(f"{base}/{path}", timeout=1.0) as res:
            if res.status != 200:
                return None
            data = res.read().decode("utf-8", errors="ignore")
            return json.loads(data)
    except Exception:
        return None


def fetch_unity_text(path: str) -> str | None:
    base = find_unity_base_url()
    if not base:
        return None
    try:
        with urllib.request.urlopen(f"{base}/{path}", timeout=1.0) as res:
            if res.status != 200:
                return None
            return res.read().decode("utf-8", errors="ignore")
    except Exception:
        return None


class DebugHandler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(STATIC_DIR), **kwargs)

    def send_json(self, payload: dict, status: int = 200) -> None:
        body = json.dumps(payload, indent=2).encode("utf-8")
        self.send_response(status)
        self._extracted_from_do_GET_4("application/json", body)

    def do_GET(self) -> None:
        parsed = urlparse(self.path)
        if parsed.path == "/favicon.ico":
            icon_path = STATIC_DIR / "favicon.svg"
            if icon_path.exists():
                data = icon_path.read_bytes()
                self.send_response(HTTPStatus.OK)
                self._extracted_from_do_GET_4("image/svg+xml", data)
                return
            self.send_response(HTTPStatus.NO_CONTENT)
            self.end_headers()
            return
        if parsed.path == "/api/health":
            self.send_json({"status": "ok"})
            return
        if parsed.path == "/status":
            self.send_json({"status": "ok"})
            return
        if parsed.path == "/api/tests":
            self.send_json({"tests": TESTS})
            return
        if parsed.path == "/api/system-info":
            self.send_json(get_system_info())
            return
        if parsed.path == "/api/com-ports":
            self.send_json({"ports": get_com_ports()})
            return
        if parsed.path == "/api/unity-status":
            status = fetch_unity_json("status")
            query_scene = fetch_unity_json("query?target=CurrentScene")
            query_run = fetch_unity_json("query?target=#RunMode")
            self.send_json(
                {
                    "connected": status is not None,
                    "status": status,
                    "scene": (query_scene or {}).get("value"),
                    "run_mode": (query_run or {}).get("value"),
                }
            )
            return
        if parsed.path == "/api/unity-telemetry":
            if telemetry := fetch_unity_text("telemetry"):
                try:
                    payload = json.loads(telemetry)
                    self.send_json(payload)
                    return
                except Exception:
                    self.send_json({"error": "Invalid telemetry payload"}, status=500)
                    return
            self.send_json({"error": "Unity not reachable"}, status=503)
            return
        if parsed.path == "/api/bridge-status":
            if bridge := fetch_unity_json("bridge"):
                self.send_json(bridge)
                return
            self.send_json({"ready": False, "reason": "Unity not reachable"}, status=503)
            return
        if parsed.path == "/api/firmware-mode":
            query = parse_qs(parsed.query)
            mode = query.get("mode", ["lockstep"])[0]
            payload = fetch_unity_text(f"firmware-mode?mode={mode}")
            if payload:
                try:
                    self.send_json(json.loads(payload))
                    return
                except Exception:
                    self.send_json({"error": "Invalid Unity response"}, status=502)
                    return
            self.send_json({"error": "Unity not reachable"}, status=503)
            return
        if parsed.path == "/api/logs":
            query = parse_qs(parsed.query)
            area = query.get("area", ["debug_console"])[0]
            self.send_json({"area": area, "logs": list_logs(area)})
            return
        if parsed.path == "/api/log-scan":
            query = parse_qs(parsed.query)
            tail_raw = query.get("tail", ["400"])[0]
            try:
                tail = int(tail_raw)
            except ValueError:
                tail = 400
            self.send_json({"alerts": scan_logs(tail)})
            return
        if parsed.path == "/api/log":
            query = parse_qs(parsed.query)
            area = query.get("area", ["debug_console"])[0]
            name = query.get("name", [""])[0]
            tail_raw = query.get("tail", [None])[0]
            tail = int(tail_raw) if tail_raw else None
            content = read_log(area, name, tail)
            self.send_response(HTTPStatus.OK)
            self.send_header("Content-Type", "text/plain; charset=utf-8")
            self.end_headers()
            self.wfile.write(content.encode("utf-8", errors="ignore"))
            return
        super().do_GET()

    # TODO Rename this here and in `send_json` and `do_GET`
    def _extracted_from_do_GET_4(self, arg0, arg1):
        self.send_header("Content-Type", arg0)
        self.send_header("Content-Length", str(len(arg1)))
        self.end_headers()
        self.wfile.write(arg1)

    def do_POST(self) -> None:
        parsed = urlparse(self.path)
        if parsed.path == "/api/run":
            query = parse_qs(parsed.query)
            name = query.get("name", [""])[0]
            test = next((t for t in TESTS if t["name"] == name), None)
            if not test:
                self.send_json({"error": "Unknown test name."}, status=400)
                return
            result = run_command(test["name"], test["command"])
            self.send_json(result)
            return
        self.send_json({"error": "Unsupported endpoint."}, status=404)


def main() -> int:
    parser = argparse.ArgumentParser(description="RobotWin Debug Console")
    parser.add_argument("--port", type=int, default=8090)
    args = parser.parse_args()

    LOG_DIR.mkdir(parents=True, exist_ok=True)
    server = ThreadingHTTPServer(("0.0.0.0", args.port), DebugHandler)
    print(f"[Debug Console] http://localhost:{args.port}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n[Debug Console] Shutting down.")
    finally:
        server.shutdown()
        server.server_close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
