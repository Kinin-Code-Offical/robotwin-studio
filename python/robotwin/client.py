"""Standard-library client for the RobotWin Studio automation API.

The server (``RobotWin.Headless --api <port>`` or ``RobotWinStudio.exe --api <port>``) speaks
JSON-RPC 2.0 with one JSON object per line over TCP.  See ``docs/API.md`` for every method.

Türkçe: RobotWin Studio otomasyon API'si için yalnızca standart kütüphane kullanan istemci.
"""

from __future__ import annotations

import base64
import itertools
import json
import os
import shutil
import socket
import struct
import subprocess
import sys
import threading
import time
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Sequence

__all__ = ["RobotWinClient", "RobotWinError", "Server", "launch", "find_server", "decode_image", "save_ppm", "save_png"]

DEFAULT_PORT = 5555


class RobotWinError(RuntimeError):
    """Error returned by the server (JSON-RPC error object)."""

    def __init__(self, code: int, message: str, method: str = ""):
        super().__init__(f"[{code}] {message}" + (f" ({method})" if method else ""))
        self.code = code
        self.message = message
        self.method = method


class RobotWinClient:
    """Connection to one RobotWin simulation.

    >>> with launch(example="car") as server:
    ...     sim = server.client()
    ...     sim.drive(0.2, 0.0)
    ...     sim.step(dt=0.01, steps=100)
    """

    def __init__(self, host: str = "127.0.0.1", port: int = DEFAULT_PORT, timeout: float = 120.0,
                 token: Optional[str] = None, connect_timeout: float = 10.0):
        self.host, self.port, self.timeout = host, port, timeout
        self._ids = itertools.count(1)
        self._lock = threading.Lock()
        deadline = time.monotonic() + connect_timeout
        last: Optional[Exception] = None
        while True:
            try:
                self._sock = socket.create_connection((host, port), timeout=timeout)
                break
            except OSError as exc:  # server still starting
                last = exc
                if time.monotonic() > deadline:
                    raise ConnectionError(f"RobotWin API {host}:{port} unreachable: {exc}") from last
                time.sleep(0.1)
        self._sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        self._buffer = b""
        self.info = self.call("hello", token=token) if token else self.call("hello")

    # ------------------------------------------------------------------ transport

    def close(self) -> None:
        try:
            self._sock.shutdown(socket.SHUT_RDWR)
        except OSError:
            pass
        self._sock.close()

    def __enter__(self) -> "RobotWinClient":
        return self

    def __exit__(self, *exc) -> None:
        self.close()

    def _readline(self) -> bytes:
        while b"\n" not in self._buffer:
            chunk = self._sock.recv(1 << 20)
            if not chunk:
                raise ConnectionError("RobotWin API connection closed")
            self._buffer += chunk
        line, _, self._buffer = self._buffer.partition(b"\n")
        return line

    def call(self, method: str, **params: Any) -> Any:
        """Calls one API method; raises :class:`RobotWinError` on errors."""
        params = {k: v for k, v in params.items() if v is not None}
        request = {"jsonrpc": "2.0", "id": next(self._ids), "method": method, "params": params}
        data = (json.dumps(request, separators=(",", ":"), allow_nan=False) + "\n").encode("utf-8")
        with self._lock:
            self._sock.sendall(data)
            response = json.loads(self._readline().decode("utf-8"))
        if "error" in response and response["error"] is not None:
            err = response["error"]
            raise RobotWinError(err.get("code", -1), err.get("message", "?"), method)
        return response.get("result")

    def batch(self, calls: Sequence[tuple]) -> List[Any]:
        """Several calls in one round trip: ``batch([("sensor.read", {"name": "imu"}), ...])``."""
        reqs = []
        for i, (method, params) in enumerate(calls):
            reqs.append({"jsonrpc": "2.0", "id": i, "method": method, "params": params or {}})
        with self._lock:
            self._sock.sendall((json.dumps(reqs, allow_nan=False) + "\n").encode("utf-8"))
            responses = json.loads(self._readline().decode("utf-8"))
        out: List[Any] = [None] * len(reqs)
        for r in responses:
            if r.get("error"):
                raise RobotWinError(r["error"]["code"], r["error"]["message"], calls[r["id"]][0])
            out[r["id"]] = r.get("result")
        return out

    # ------------------------------------------------------------------ world

    def load(self, path: Optional[str] = None, example: Optional[str] = None, seed: int = 0,
             firmware: bool = True, sensors: Optional[List[dict]] = None, **options: Any) -> dict:
        """Loads a .rwworld / .rwproj file or a built-in example (car, line-follower, obstacle-avoider, quadcopter)."""
        if path is not None:
            path = str(Path(path).resolve())
        return self.call("world.load", path=path, example=example, seed=seed, firmware=firmware, sensors=sensors, **options)

    def reset(self, seed: Optional[int] = None, randomize: Optional[dict] = None,
              observe: Optional[Iterable[str]] = None) -> dict:
        return self.call("world.reset", seed=seed, randomize=randomize, observe=list(observe) if observe else None)

    def describe(self) -> dict:
        return self.call("world.describe")

    def step(self, dt: float = 0.01, steps: int = 1, actions: Optional[List[dict]] = None,
             observe: Optional[Iterable[str]] = None, pace: Optional[float] = None,
             stop_on_failure: bool = False, events: bool = False) -> dict:
        """Advances the simulation ``steps`` × ``dt`` seconds (after applying ``actions``)."""
        return self.call("sim.step", dt=dt, steps=steps, actions=actions,
                         observe=list(observe) if observe else None, pace=pace,
                         stop_on_failure=stop_on_failure or None, events=events or None)

    def status(self) -> dict:
        return self.call("sim.status")

    def events(self, max: int = 1000) -> List[dict]:
        return self.call("events.poll", max=max)

    @property
    def time(self) -> float:
        return self.status()["time"]

    # ------------------------------------------------------------------ objects

    def objects(self) -> List[dict]:
        return self.call("object.list")

    def get_pose(self, object_id: str) -> dict:
        return self.call("object.get_pose", id=object_id)

    def set_pose(self, object_id: str, position: Optional[Sequence[float]] = None,
                 orientation: Optional[Sequence[float]] = None, rpy_deg: Optional[Sequence[float]] = None,
                 keep_velocity: bool = False) -> dict:
        return self.call("object.set_pose", id=object_id, position=_list(position), orientation=_list(orientation),
                         rpy_deg=_list(rpy_deg), keep_velocity=keep_velocity or None)

    # ------------------------------------------------------------------ sensors

    def sensors(self) -> List[dict]:
        return self.call("sensor.list")

    def read_sensor(self, name: str) -> dict:
        return self.call("sensor.read", name=name)

    def read_sensors(self, names: Optional[Iterable[str]] = None) -> Dict[str, dict]:
        return self.call("sensor.read", names=list(names) if names else None)

    def add_sensor(self, type: str, name: Optional[str] = None, object: Optional[str] = None,
                   offset: Optional[Sequence[float]] = None, rpy_deg: Optional[Sequence[float]] = None, **params: Any) -> dict:
        """Attaches a virtual sensor: imu, gps, odometry, lidar, range, camera, depth, line."""
        return self.call("sensor.add", type=type, name=name, object=object, offset=_list(offset), rpy_deg=_list(rpy_deg), **params)

    def remove_sensor(self, name: str) -> bool:
        return self.call("sensor.remove", name=name)

    def camera(self, name: str):
        """Reads a camera/depth sensor and decodes it (numpy array if numpy is installed, else nested lists)."""
        return decode_image(self.read_sensor(name))

    # ------------------------------------------------------------------ actuators

    def actuators(self) -> List[dict]:
        return self.call("actuator.list")

    def set_actuator(self, name: str, mode: str, value: Optional[float] = None, **kw: Any) -> dict:
        """mode: voltage | duty | speed | torque | angle | release (see docs/API.md)."""
        return self.call("actuator.set", name=name, mode=mode, value=value, **kw)

    def release(self, name: Optional[str] = None) -> bool:
        return self.call("actuator.release", name=name)

    def pin_write(self, pin: str, mode: str, board: Optional[str] = None, **kw: Any) -> dict:
        """mode: high | low | input | pullup | pwm (duty, frequency) | servo (angle) | release."""
        return self.call("pin.write", pin=pin, mode=mode, board=board, **kw)

    def pin_read(self, pin: str, board: Optional[str] = None) -> dict:
        return self.call("pin.read", pin=pin, board=board)

    def servo_write(self, pin: str, angle: float, board: Optional[str] = None) -> dict:
        return self.pin_write(pin, "servo", board=board, angle=angle)

    def drive(self, v: float, w: float = 0.0, **kw: Any) -> dict:
        """Differential drive: forward speed v (m/s), turn rate w (rad/s, + = left / CCW)."""
        return self.call("drive.differential", v=v, w=w, **kw)

    def stop(self) -> bool:
        return self.call("drive.stop")

    # ------------------------------------------------------------------ flight (UAV)

    def arm(self, armed: bool = True) -> dict:
        return self.call("flight.arm", armed=armed)

    def flight(self, **setpoints: Any) -> dict:
        """throttle (0..1), roll/pitch/yaw (−1..1), mode ('angle'|'altitude'), target_altitude (m), armed."""
        return self.call("flight.set", **setpoints)

    def flight_status(self) -> dict:
        return self.call("flight.status")

    def motors(self, duties: Sequence[float]) -> dict:
        return self.call("flight.motors", duty=list(duties))

    # ------------------------------------------------------------------ serial / firmware / circuit

    def serial_read(self, board: Optional[str] = None, clear: bool = True) -> Dict[str, str]:
        return self.call("serial.read", board=board, clear=clear)

    def serial_write(self, text: str, board: Optional[str] = None) -> bool:
        return self.call("serial.write", text=text, board=board)

    def set_firmware(self, board: Optional[str] = None, hex: Optional[str] = None, path: Optional[str] = None) -> dict:
        return self.call("firmware.set", board=board, hex=hex, path=path)

    def enable_firmware(self, enabled: bool) -> dict:
        return self.call("firmware.enable", enabled=enabled)

    def voltage(self, node: str) -> Optional[float]:
        return self.call("circuit.voltage", node=node)

    def part(self, ref: str) -> dict:
        return self.call("circuit.part", ref=ref)

    def signals(self, prefix: str = "", max: int = 500) -> Dict[str, float]:
        return self.call("circuit.signals", prefix=prefix, max=max)

    def shutdown(self) -> None:
        try:
            self.call("shutdown")
        except (ConnectionError, OSError):
            pass


def _list(v):
    return None if v is None else [float(x) for x in v]


def decode_image(reading: dict):
    """Decodes a camera reading: RGB → H×W×3 uint8, depth → H×W float32 (numpy if available)."""
    w, h, enc = reading["width"], reading["height"], reading["encoding"]
    raw = base64.b64decode(reading["data"])
    try:
        import numpy as np  # optional
        if enc == "rgb8":
            return np.frombuffer(raw, dtype=np.uint8).reshape(h, w, 3)
        return np.frombuffer(raw, dtype="<f4").reshape(h, w)
    except ImportError:
        if enc == "rgb8":
            return [[tuple(raw[(r * w + c) * 3:(r * w + c) * 3 + 3]) for c in range(w)] for r in range(h)]
        floats = struct.unpack("<%df" % (w * h), raw)
        return [list(floats[r * w:(r + 1) * w]) for r in range(h)]


def save_ppm(reading: dict, path: str) -> None:
    """Writes an RGB camera reading as a binary PPM (viewable everywhere, no dependencies)."""
    raw = base64.b64decode(reading["data"])
    with open(path, "wb") as f:
        f.write(b"P6\n%d %d\n255\n" % (reading["width"], reading["height"]))
        f.write(raw)


def save_png(reading: dict, path: str, scale: int = 1) -> None:
    """Writes an RGB camera reading as PNG using only zlib (``scale`` enlarges pixels for viewing)."""
    import zlib
    w, h = reading["width"], reading["height"]
    raw = base64.b64decode(reading["data"])
    rows = []
    for r in range(h):
        row = raw[r * w * 3:(r + 1) * w * 3]
        if scale > 1:
            row = b"".join(row[i:i + 3] * scale for i in range(0, len(row), 3))
        rows.extend([b"\x00" + row] * scale)

    def chunk(kind: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w * scale, h * scale, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(b"".join(rows), 6)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


# ====================================================================== server processes

def _repo_root() -> Path:
    return Path(__file__).resolve().parents[2]


def find_server() -> List[str]:
    """Command that starts a headless server.

    Order: $ROBOTWIN_SERVER (an .exe, a native binary or a .dll run with ``dotnet``), the repository's
    build output (builds/RobotWin.Headless/{Release,Debug}/net9.0), then ``RobotWin.Headless`` on PATH.
    """
    env = os.environ.get("ROBOTWIN_SERVER")
    candidates: List[Path] = [Path(env)] if env else []
    root = _repo_root() / "builds" / "RobotWin.Headless"
    for config in ("Release", "Debug"):
        base = root / config / "net9.0"
        candidates += [base / "RobotWin.Headless.exe", base / "RobotWin.Headless", base / "RobotWin.Headless.dll"]
    for c in candidates:
        if c.is_file():
            if c.suffix.lower() == ".dll":
                return [shutil.which("dotnet") or "dotnet", str(c)]
            return [str(c)]
    on_path = shutil.which("RobotWin.Headless")
    if on_path:
        return [on_path]
    raise FileNotFoundError(
        "RobotWin.Headless not found. Build it: dotnet build tools/RobotWin.Headless -c Release, "
        "or set ROBOTWIN_SERVER to its path.")


class Server:
    """A headless RobotWin process listening on a free local port (stopped on close / exit)."""

    def __init__(self, args: Sequence[str] = (), port: int = 0, startup_timeout: float = 60.0,
                 command: Optional[List[str]] = None, quiet: bool = True):
        cmd = list(command or find_server()) + ["--api", str(port), "--parent-pid", str(os.getpid())] + list(args)
        flags = subprocess.CREATE_NO_WINDOW if sys.platform == "win32" else 0
        self.process = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE if quiet else None,
                                        stdin=subprocess.DEVNULL, creationflags=flags)
        self.port = -1
        deadline = time.monotonic() + startup_timeout
        lines = []
        while time.monotonic() < deadline:
            line = self.process.stdout.readline()
            if not line:
                break
            text = line.decode("utf-8", "replace").strip()
            lines.append(text)
            if text.startswith("ROBOTWIN_API_READY"):
                fields = dict(f.split("=", 1) for f in text.split()[1:] if "=" in f)
                self.port = int(fields["port"])
                break
        if self.port < 0:
            err = b""
            if self.process.poll() is not None and self.process.stderr is not None:
                err = self.process.stderr.read() or b""
            self.close()
            raise RuntimeError("RobotWin server did not start: " + " | ".join(lines) + err.decode("utf-8", "replace"))
        # Keep draining stdout so the server never blocks on a full pipe.
        threading.Thread(target=self._drain, daemon=True).start()

    def _drain(self) -> None:
        try:
            for _ in iter(self.process.stdout.readline, b""):
                pass
        except (OSError, ValueError):
            pass

    def client(self, **kw: Any) -> RobotWinClient:
        return RobotWinClient(port=self.port, **kw)

    def close(self) -> None:
        if self.process.poll() is None:
            try:
                with RobotWinClient(port=self.port, connect_timeout=1.0, timeout=5.0) as c:
                    c.shutdown()
                self.process.wait(timeout=5)
            except (OSError, ConnectionError, subprocess.TimeoutExpired, RobotWinError, ValueError):
                self.process.kill()
        for stream in (self.process.stdout, self.process.stderr):
            try:
                if stream is not None:
                    stream.close()
            except OSError:
                pass

    def __enter__(self) -> "Server":
        return self

    def __exit__(self, *exc) -> None:
        self.close()

    def __del__(self):
        try:
            if self.process.poll() is None:
                self.process.kill()
        except Exception:
            pass


def launch(world: Optional[str] = None, example: Optional[str] = None, seed: int = 0,
           firmware: bool = True, extra_args: Sequence[str] = (), **kw: Any) -> Server:
    """Starts a headless server (optionally preloading a world/project/example)."""
    args: List[str] = ["--seed", str(seed)]
    if world:
        p = str(Path(world).resolve())
        args += ["--project" if not p.lower().endswith(".rwworld") else "--world", p]
    if example:
        args += ["--example", example]
    if not firmware:
        args.append("--no-firmware")
    return Server(list(args) + list(extra_args), **kw)
