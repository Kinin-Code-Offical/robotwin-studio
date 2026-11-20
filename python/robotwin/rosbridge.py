"""rosbridge-compatible JSON WebSocket server for RobotWin — no ROS installation needed.

Speaks the rosbridge v2 protocol subset that web tools use (``subscribe``, ``unsubscribe``,
``advertise``, ``publish``, ``call_service``), so roslibjs pages and tools with a "Rosbridge"
connection can see ``/clock``, ``/tf``, ``/odom``, ``/scan``, ``/imu``, ``/fix``,
``/camera/image_raw`` … and drive the robot through ``/cmd_vel``. Pure standard library.

    python -m robotwin.rosbridge --example car --port 9090
    python -m robotwin.rosbridge --world my.rwworld --camera

Türkçe: ROS kurulu olmadan rosbridge uyumlu JSON WebSocket sunucusu.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import math
import socket
import struct
import threading
import time
from typing import Any, Dict, List, Optional, Set

from .client import RobotWinClient, launch
from .ros import RANGE_TYPE, TOPIC_TYPES, BridgeConfig, SimBridge

_GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"

# Minimal ROS 2 message definitions for tools that ask rosapi for raw types (best effort).
_DEFS = {
    "builtin_interfaces/Time": "int32 sec\nuint32 nanosec",
    "std_msgs/Header": "builtin_interfaces/Time stamp\nstring frame_id",
    "std_msgs/String": "string data",
    "geometry_msgs/Vector3": "float64 x\nfloat64 y\nfloat64 z",
    "geometry_msgs/Point": "float64 x\nfloat64 y\nfloat64 z",
    "geometry_msgs/Quaternion": "float64 x\nfloat64 y\nfloat64 z\nfloat64 w",
    "geometry_msgs/Pose": "geometry_msgs/Point position\ngeometry_msgs/Quaternion orientation",
    "geometry_msgs/PoseWithCovariance": "geometry_msgs/Pose pose\nfloat64[36] covariance",
    "geometry_msgs/Twist": "geometry_msgs/Vector3 linear\ngeometry_msgs/Vector3 angular",
    "geometry_msgs/TwistWithCovariance": "geometry_msgs/Twist twist\nfloat64[36] covariance",
    "geometry_msgs/Transform": "geometry_msgs/Vector3 translation\ngeometry_msgs/Quaternion rotation",
    "geometry_msgs/TransformStamped": "std_msgs/Header header\nstring child_frame_id\ngeometry_msgs/Transform transform",
    "rosgraph_msgs/Clock": "builtin_interfaces/Time clock",
    "tf2_msgs/TFMessage": "geometry_msgs/TransformStamped[] transforms",
    "nav_msgs/Odometry": "std_msgs/Header header\nstring child_frame_id\ngeometry_msgs/PoseWithCovariance pose\ngeometry_msgs/TwistWithCovariance twist",
    "sensor_msgs/LaserScan": "std_msgs/Header header\nfloat32 angle_min\nfloat32 angle_max\nfloat32 angle_increment\nfloat32 time_increment\n"
                             "float32 scan_time\nfloat32 range_min\nfloat32 range_max\nfloat32[] ranges\nfloat32[] intensities",
    "sensor_msgs/Imu": "std_msgs/Header header\ngeometry_msgs/Quaternion orientation\nfloat64[9] orientation_covariance\n"
                       "geometry_msgs/Vector3 angular_velocity\nfloat64[9] angular_velocity_covariance\n"
                       "geometry_msgs/Vector3 linear_acceleration\nfloat64[9] linear_acceleration_covariance",
    "sensor_msgs/NavSatStatus": "int8 status\nuint16 service",
    "sensor_msgs/NavSatFix": "std_msgs/Header header\nsensor_msgs/NavSatStatus status\nfloat64 latitude\nfloat64 longitude\nfloat64 altitude\n"
                             "float64[9] position_covariance\nuint8 position_covariance_type",
    "sensor_msgs/Image": "std_msgs/Header header\nuint32 height\nuint32 width\nstring encoding\nuint8 is_bigendian\nuint32 step\nuint8[] data",
    "sensor_msgs/RegionOfInterest": "uint32 x_offset\nuint32 y_offset\nuint32 height\nuint32 width\nbool do_rectify",
    "sensor_msgs/CameraInfo": "std_msgs/Header header\nuint32 height\nuint32 width\nstring distortion_model\nfloat64[] d\nfloat64[9] k\n"
                              "float64[9] r\nfloat64[12] p\nuint32 binning_x\nuint32 binning_y\nsensor_msgs/RegionOfInterest roi",
    "sensor_msgs/JointState": "std_msgs/Header header\nstring[] name\nfloat64[] position\nfloat64[] velocity\nfloat64[] effort",
    "sensor_msgs/Range": "std_msgs/Header header\nuint8 radiation_type\nfloat32 field_of_view\nfloat32 min_range\nfloat32 max_range\nfloat32 range",
}


def _full_text(ros_type: str) -> str:
    """Definition of a type followed by its dependencies (ros2msg layout used by rosbridge's rosapi)."""
    key = ros_type.replace("/msg/", "/")
    seen: List[str] = []

    def deps(t: str):
        for line in _DEFS.get(t, "").splitlines():
            ft = line.split()[0].split("[")[0]
            if "/" in ft and ft not in seen:
                seen.append(ft)
                deps(ft)

    deps(key)
    text = _DEFS.get(key, "")
    for d in seen:
        text += "\n" + "=" * 80 + "\nMSG: " + d + "\n" + _DEFS.get(d, "")
    return text


def _finite(x: Any) -> Any:
    """JSON has no Infinity/NaN: send null like rosbridge does."""
    if isinstance(x, float):
        return x if math.isfinite(x) else None
    if isinstance(x, dict):
        return {k: _finite(v) for k, v in x.items()}
    if isinstance(x, list):
        return [_finite(v) for v in x]
    return x


class _Client:
    def __init__(self, sock: socket.socket, addr):
        self.sock = sock
        self.addr = addr
        self.subs: Dict[str, float] = {}       # topic → throttle (s)
        self.last: Dict[str, float] = {}
        self.lock = threading.Lock()
        self.alive = True

    def send_text(self, text: str) -> None:
        data = text.encode("utf-8")
        n = len(data)
        if n < 126:
            head = struct.pack("!BB", 0x81, n)
        elif n < 65536:
            head = struct.pack("!BBH", 0x81, 126, n)
        else:
            head = struct.pack("!BBQ", 0x81, 127, n)
        with self.lock:
            try:
                self.sock.sendall(head + data)
            except OSError:
                self.alive = False


class RosbridgeServer:
    """WebSocket server (one simulation) that publishes the bridge's topics to subscribed clients."""

    def __init__(self, bridge: SimBridge, host: str = "127.0.0.1", port: int = 9090):
        self.bridge = bridge
        self.host, self.port = host, port
        self.clients: Set[_Client] = set()
        self._gate = threading.Lock()
        self._stop = threading.Event()
        self.topics: Dict[str, str] = dict(TOPIC_TYPES)
        self.ticks = 0

    # ------------------------------------------------------------------ websocket plumbing

    def serve_forever(self) -> None:
        srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        srv.bind((self.host, self.port))
        self.port = srv.getsockname()[1]
        srv.listen(8)
        srv.settimeout(0.5)
        self._listener = srv
        threading.Thread(target=self._sim_loop, daemon=True).start()
        print(f"ROSBRIDGE_READY ws://{self.host}:{self.port}", flush=True)
        while not self._stop.is_set():
            try:
                sock, addr = srv.accept()
            except socket.timeout:
                continue
            except OSError:
                break
            threading.Thread(target=self._serve, args=(sock, addr), daemon=True).start()
        srv.close()

    def start(self) -> "RosbridgeServer":
        threading.Thread(target=self.serve_forever, daemon=True).start()
        for _ in range(100):
            if hasattr(self, "_listener"):
                break
            time.sleep(0.02)
        return self

    def stop(self) -> None:
        self._stop.set()

    def _handshake(self, sock: socket.socket) -> bool:
        data = b""
        while b"\r\n\r\n" not in data:
            chunk = sock.recv(4096)
            if not chunk:
                return False
            data += chunk
        headers = {}
        for line in data.decode("latin-1").split("\r\n")[1:]:
            if ":" in line:
                k, v = line.split(":", 1)
                headers[k.strip().lower()] = v.strip()
        key = headers.get("sec-websocket-key")
        if not key:
            sock.sendall(b"HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\n\r\n")
            return False
        accept = base64.b64encode(hashlib.sha1((key + _GUID).encode()).digest()).decode()
        sock.sendall(("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
                      f"Sec-WebSocket-Accept: {accept}\r\n\r\n").encode())
        return True

    @staticmethod
    def _recv_exact(sock: socket.socket, n: int) -> bytes:
        buf = b""
        while len(buf) < n:
            chunk = sock.recv(n - len(buf))
            if not chunk:
                raise ConnectionError
            buf += chunk
        return buf

    def _read_frame(self, sock: socket.socket):
        b1, b2 = self._recv_exact(sock, 2)
        opcode, masked, n = b1 & 0x0F, b2 & 0x80, b2 & 0x7F
        if n == 126:
            n = struct.unpack("!H", self._recv_exact(sock, 2))[0]
        elif n == 127:
            n = struct.unpack("!Q", self._recv_exact(sock, 8))[0]
        mask = self._recv_exact(sock, 4) if masked else b"\0\0\0\0"
        payload = bytearray(self._recv_exact(sock, n))
        for i in range(n):
            payload[i] ^= mask[i % 4]
        return opcode, bytes(payload)

    def _serve(self, sock: socket.socket, addr) -> None:
        try:
            if not self._handshake(sock):
                sock.close()
                return
        except OSError:
            sock.close()
            return
        client = _Client(sock, addr)
        self.clients.add(client)
        fragments = b""
        try:
            while client.alive and not self._stop.is_set():
                opcode, payload = self._read_frame(sock)
                if opcode == 0x8:      # close
                    break
                if opcode == 0x9:      # ping → pong
                    with client.lock:
                        sock.sendall(struct.pack("!BB", 0x8A, len(payload)) + payload)
                    continue
                if opcode in (0x1, 0x0, 0x2):
                    fragments += payload
                    try:
                        msg = json.loads(fragments.decode("utf-8"))
                    except ValueError:
                        continue
                    fragments = b""
                    self._handle(client, msg)
        except (ConnectionError, OSError, struct.error):
            pass
        finally:
            client.alive = False
            self.clients.discard(client)
            try:
                sock.close()
            except OSError:
                pass

    # ------------------------------------------------------------------ rosbridge protocol

    def _handle(self, client: _Client, msg: dict) -> None:
        op = msg.get("op")
        if op == "subscribe":
            client.subs[msg["topic"]] = float(msg.get("throttle_rate", 0)) / 1000.0
        elif op == "unsubscribe":
            client.subs.pop(msg.get("topic"), None)
        elif op == "advertise":
            self.topics.setdefault(msg["topic"], msg.get("type", "std_msgs/msg/String"))
        elif op == "publish":
            self._publish_in(msg.get("topic", ""), msg.get("msg", {}))
        elif op == "call_service":
            self._service(client, msg)

    def _publish_in(self, topic: str, m: dict) -> None:
        with self._gate:
            if topic == "/cmd_vel":
                lin, ang = m.get("linear", {}), m.get("angular", {})
                self.bridge.cmd_vel((lin.get("x", 0.0), lin.get("y", 0.0), lin.get("z", 0.0)),
                                    (ang.get("x", 0.0), ang.get("y", 0.0), ang.get("z", 0.0)))
            elif topic == "/robotwin/arm":
                self.bridge.arm(bool(m.get("data", True)))
            elif topic.startswith("/robotwin/actuator/"):
                self.bridge.actuator(topic.rsplit("/", 1)[1], float(m.get("data", 0.0)))
            elif topic == "/robotwin/command":
                self.bridge.call(m.get("data", "{}"))

    def _service(self, client: _Client, msg: dict) -> None:
        service, args = msg.get("service", ""), msg.get("args") or {}
        values: Any
        ok = True
        if service in ("/rosapi/topics", "/rosapi/topics_and_raw_types"):
            topics = sorted(self.topics)
            values = {"topics": topics, "types": [self.topics[t] for t in topics]}
            if service.endswith("raw_types"):
                values["typedefs_full_text"] = [_full_text(self.topics[t]) for t in topics]
        elif service == "/rosapi/services":
            values = {"services": ["/robotwin/reset", "/robotwin/call", "/rosapi/topics", "/rosapi/topics_and_raw_types"]}
        elif service == "/robotwin/reset":
            with self._gate:
                self.bridge.reset(args.get("seed"))
            values = {}
        elif service == "/robotwin/call":
            with self._gate:
                values = {"response": self.bridge.call(args.get("request", "{}"))}
        else:
            ok, values = False, f"unknown service {service}"
        reply = {"op": "service_response", "service": service, "values": values, "result": ok}
        if "id" in msg:
            reply["id"] = msg["id"]
        client.send_text(json.dumps(reply))

    # ------------------------------------------------------------------ simulation loop

    def _sim_loop(self) -> None:
        while not self._stop.is_set():
            with self._gate:
                try:
                    messages = self.bridge.tick()
                except (ConnectionError, OSError):
                    self._stop.set()
                    return
            self.ticks += 1
            now = time.monotonic()
            for topic, rtype, m in messages:
                self.topics.setdefault(topic, rtype)
                for c in list(self.clients):
                    throttle = c.subs.get(topic)
                    if throttle is None or now - c.last.get(topic, 0.0) < throttle:
                        continue
                    c.last[topic] = now
                    c.send_text(json.dumps({"op": "publish", "topic": topic, "msg": _finite(m)}, ensure_ascii=False))


def main(argv: Optional[List[str]] = None) -> None:
    ap = argparse.ArgumentParser(description="rosbridge-compatible WebSocket server for a RobotWin simulation")
    ap.add_argument("--example", default=None)
    ap.add_argument("--world", default=None)
    ap.add_argument("--connect", default=None, help="host:port of a running RobotWin API server instead of launching one")
    ap.add_argument("--port", type=int, default=9090)
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--seed", type=int, default=0)
    ap.add_argument("--camera", action="store_true", help="publish /camera/image_raw (ray-cast camera)")
    ap.add_argument("--no-scan", action="store_true")
    ap.add_argument("--fast", action="store_true", help="run faster than real time")
    args = ap.parse_args(argv)
    server = None
    if args.connect:
        h, _, p = args.connect.rpartition(":")
        client = RobotWinClient(h or "127.0.0.1", int(p))
        if args.example or args.world:
            client.load(path=args.world, example=args.example, seed=args.seed)
    else:
        server = launch(world=args.world, example=args.example or (None if args.world else "car"), seed=args.seed)
        client = server.client()
    cfg = BridgeConfig(realtime=not args.fast)
    if args.no_scan:
        cfg.scan = None
    if args.camera:
        cfg.camera = {"width": 160, "height": 120, "fov_deg": 70, "offset": [0.08, 0, 0.06], "every": 5}
    try:
        RosbridgeServer(SimBridge(client, cfg), args.host, args.port).serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        client.close()
        if server:
            server.close()


if __name__ == "__main__":
    main()
