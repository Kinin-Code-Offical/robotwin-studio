"""ROS-shaped messages from RobotWin readings, and the simulation side of the ROS bridges.

Everything here is plain Python (no ROS needed): messages are dicts whose field names match the
ROS 2 message definitions, so the rclpy node (``ros2/robotwin_bridge``) copies them into real
messages and the rosbridge-compatible WebSocket server (:mod:`robotwin.rosbridge`) sends them as JSON.

Türkçe: RobotWin okumalarından ROS biçimli mesajlar ve ROS köprülerinin simülasyon tarafı.
"""

from __future__ import annotations

import json
import math
from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional, Tuple

from .client import RobotWinClient, RobotWinError

TOPIC_TYPES: Dict[str, str] = {
    "/clock": "rosgraph_msgs/msg/Clock",
    "/tf": "tf2_msgs/msg/TFMessage",
    "/tf_static": "tf2_msgs/msg/TFMessage",
    "/odom": "nav_msgs/msg/Odometry",
    "/scan": "sensor_msgs/msg/LaserScan",
    "/imu": "sensor_msgs/msg/Imu",
    "/fix": "sensor_msgs/msg/NavSatFix",
    "/camera/image_raw": "sensor_msgs/msg/Image",
    "/camera/camera_info": "sensor_msgs/msg/CameraInfo",
    "/joint_states": "sensor_msgs/msg/JointState",
    "/robotwin/events": "std_msgs/msg/String",
    "/robotwin/serial": "std_msgs/msg/String",
    "/robotwin/flight": "std_msgs/msg/String",
}
RANGE_TYPE = "sensor_msgs/msg/Range"


# ====================================================================== message builders

def stamp(t: float) -> Dict[str, int]:
    sec = int(math.floor(t))
    return {"sec": sec, "nanosec": int(round((t - sec) * 1e9)) % 1_000_000_000}


def header(t: float, frame: str) -> dict:
    return {"stamp": stamp(t), "frame_id": frame}


def clock(t: float) -> dict:
    return {"clock": stamp(t)}


def _v3(v) -> dict:
    return {"x": float(v[0]), "y": float(v[1]), "z": float(v[2])}


def _q(q) -> dict:
    return {"x": float(q[0]), "y": float(q[1]), "z": float(q[2]), "w": float(q[3])}


def transform(t: float, parent: str, child: str, position, orientation) -> dict:
    return {"header": header(t, parent), "child_frame_id": child,
            "transform": {"translation": _v3(position), "rotation": _q(orientation)}}


def quat_from_rpy(roll: float, pitch: float, yaw: float) -> Tuple[float, float, float, float]:
    cr, sr = math.cos(roll / 2), math.sin(roll / 2)
    cp, sp = math.cos(pitch / 2), math.sin(pitch / 2)
    cy, sy = math.cos(yaw / 2), math.sin(yaw / 2)
    return (sr * cp * cy - cr * sp * sy, cr * sp * cy + sr * cp * sy, cr * cp * sy - sr * sp * cy, cr * cp * cy + sr * sp * sy)


def odometry(r: dict, t: float, odom_frame: str = "odom", base_frame: str = "base_link") -> dict:
    cov = [0.0] * 36
    return {
        "header": header(t, odom_frame), "child_frame_id": base_frame,
        "pose": {"pose": {"position": _v3(r["position"]), "orientation": _q(r["orientation"])}, "covariance": cov},
        "twist": {"twist": {"linear": _v3(r["linear_velocity"]), "angular": _v3(r["angular_velocity"])}, "covariance": list(cov)},
    }


def laser_scan(r: dict, t: float, frame: str = "laser", scan_time: float = 0.0) -> dict:
    rmax = float(r["range_max"])
    ranges = [float(x) if x is not None else math.inf for x in r["ranges"]]
    n = len(ranges) // max(1, int(r.get("layers", 1)))
    return {"header": header(t, frame), "angle_min": float(r["angle_min"]), "angle_max": float(r["angle_max"]),
            "angle_increment": float(r["angle_increment"]), "time_increment": 0.0, "scan_time": scan_time,
            "range_min": float(r["range_min"]), "range_max": rmax, "ranges": ranges[:n], "intensities": []}


def imu(r: dict, t: float, frame: str = "imu_link", accel_var: float = 0.0, gyro_var: float = 0.0) -> dict:
    return {"header": header(t, frame), "orientation": _q(r["orientation"]), "orientation_covariance": [0.0] * 9,
            "angular_velocity": _v3(r["angular_velocity"]), "angular_velocity_covariance": [gyro_var, 0, 0, 0, gyro_var, 0, 0, 0, gyro_var],
            "linear_acceleration": _v3(r["linear_acceleration"]),
            "linear_acceleration_covariance": [accel_var, 0, 0, 0, accel_var, 0, 0, 0, accel_var]}


def nav_sat_fix(r: dict, t: float, frame: str = "gps_link") -> dict:
    c = r.get("covariance_diagonal") or [0.0, 0.0, 0.0]
    return {"header": header(t, frame), "status": {"status": 0, "service": 1},
            "latitude": float(r["latitude"]), "longitude": float(r["longitude"]), "altitude": float(r["altitude"]),
            "position_covariance": [c[0], 0, 0, 0, c[1], 0, 0, 0, c[2]], "position_covariance_type": 2 if any(c) else 0}


def image(r: dict, t: float, frame: str = "camera_optical_frame") -> dict:
    """sensor_msgs/Image with ``data`` as base64 text (rosbridge convention); the rclpy node decodes it."""
    return {"header": header(t, frame), "height": int(r["height"]), "width": int(r["width"]),
            "encoding": r["encoding"], "is_bigendian": 0, "step": int(r["step"]), "data": r["data"]}


def camera_info(r: dict, t: float, frame: str = "camera_optical_frame") -> dict:
    fx, fy, cx, cy = float(r["fx"]), float(r["fy"]), float(r["cx"]), float(r["cy"])
    return {"header": header(t, frame), "height": int(r["height"]), "width": int(r["width"]),
            "distortion_model": "plumb_bob", "d": [0.0] * 5, "k": [fx, 0, cx, 0, fy, cy, 0, 0, 1],
            "r": [1.0, 0, 0, 0, 1, 0, 0, 0, 1], "p": [fx, 0, cx, 0, 0, fy, cy, 0, 0, 0, 1, 0],
            "binning_x": 0, "binning_y": 0, "roi": {"x_offset": 0, "y_offset": 0, "height": 0, "width": 0, "do_rectify": False}}


def range_msg(r: dict, t: float, frame: str, fov: float = 0.26, ultrasound: bool = True) -> dict:
    value = r.get("range")
    return {"header": header(t, frame), "radiation_type": 0 if ultrasound else 1, "field_of_view": fov,
            "min_range": float(r.get("range_min", 0.02)), "max_range": float(r.get("range_max", 4.0)),
            "range": float(value) if value is not None else math.inf}


def joint_state(encoders: Dict[str, dict], t: float) -> dict:
    names = sorted(encoders)
    return {"header": header(t, ""), "name": names,
            "position": [float(encoders[n].get("angle") or 0.0) for n in names],
            "velocity": [float(encoders[n].get("velocity") or 0.0) for n in names], "effort": []}


def string(text: str) -> dict:
    return {"data": text}


# ====================================================================== bridge core

@dataclass
class BridgeConfig:
    """Topics and sensors of a bridge (defaults suit the built-in car / quadcopter examples)."""

    base: Optional[str] = None            # object id of the robot body (auto: vehicle / the most connected object)
    odom_frame: str = "odom"
    base_frame: str = "base_link"
    world_frame: str = "map"
    dt: float = 0.005                      # simulation step (s)
    rate: float = 50.0                     # publish rate (Hz of simulated time)
    realtime: bool = True                  # pace the simulation to the wall clock
    scan: Optional[dict] = field(default_factory=lambda: {"samples": 360, "range_max": 8.0, "offset": [0.0, 0.0, 0.06]})
    imu: bool = True
    gps: bool = True
    camera: Optional[dict] = None          # e.g. {"width": 160, "height": 120, "fov_deg": 70, "offset": [0.08, 0, 0.06], "every": 5}
    object_tf: bool = True                 # map → every world object
    cmd_vel_timeout: float = 0.5           # stop when no /cmd_vel for this long (simulated s)
    max_speed: float = 0.5                 # m/s mapped to full stick (UAV) / driver duty (vehicle)


class SimBridge:
    """Turns a RobotWin simulation into ROS topics: ``tick()`` steps and returns [(topic, type, msg)]."""

    def __init__(self, client: RobotWinClient, config: Optional[BridgeConfig] = None):
        self.sim = client
        self.cfg = config or BridgeConfig()
        self._cmd: Optional[Tuple[float, dict]] = None
        self._pending: List[dict] = []
        self._ticks = 0
        self.setup()

    # ------------------------------------------------------------------ setup

    def setup(self) -> None:
        d = self.sim.describe()
        self.kind = d["kind"]
        self.vehicle = d.get("vehicle", {})
        self.uav = self.vehicle.get("type") == "quadcopter"
        self.base = self.cfg.base or ("vehicle" if self.kind == "vehicle" else _most_connected(d))
        existing = {s["name"] for s in d.get("sensors", [])}
        self.sensors: List[str] = []
        # Odometry is ground truth here, so map and odom coincide.
        self.static_tf: List[dict] = [transform(0, self.cfg.world_frame, self.cfg.odom_frame, (0, 0, 0), (0, 0, 0, 1))]
        if "odom" not in existing:
            self.sim.add_sensor("odometry", name="odom", object=self.base)
        self.sensors.append("odom")
        if self.cfg.imu:
            if "imu" not in existing:
                self.sim.add_sensor("imu", name="imu", object=self.base)
            self.sensors.append("imu")
        if self.cfg.gps:
            if "gps" not in existing:
                self.sim.add_sensor("gps", name="gps", object=self.base)
            self.sensors.append("gps")
        if self.cfg.scan:
            sc = dict(self.cfg.scan)
            off = sc.pop("offset", [0, 0, 0.06])
            self.sim.add_sensor("lidar", name="ros_scan", object=self.base, offset=off, **sc)
            self.sensors.append("ros_scan")
            self.static_tf.append(transform(0, self.cfg.base_frame, "laser", off, (0, 0, 0, 1)))
        self.camera_every = 0
        if self.cfg.camera:
            cam = dict(self.cfg.camera)
            off = cam.pop("offset", [0.08, 0, 0.06])
            rpy = cam.pop("rpy_deg", [0, 0, 0])
            self.camera_every = int(cam.pop("every", 5))
            self.sim.add_sensor("camera", name="ros_camera", object=self.base, offset=off, rpy_deg=rpy, **cam)
            self.static_tf.append(transform(0, self.cfg.base_frame, "camera_link", off, quat_from_rpy(*[math.radians(a) for a in rpy])))
            self.static_tf.append(transform(0, "camera_link", "camera_optical_frame", (0, 0, 0), quat_from_rpy(-math.pi / 2, 0, -math.pi / 2)))
        for s in self.sim.sensors():
            if s["type"] in ("ultrasonic", "encoder", "line", "obstacle") and s["name"] not in self.sensors:
                self.sensors.append(s["name"])
        self.sensor_types = {s["name"]: s["type"] for s in self.sim.sensors()}
        self.objects = [o["id"] for o in d["objects"]]
        # /joint_states uses the URDF joint names of robotwin.urdf (object_port_joint).
        from .urdf import joint_name
        self.joint_names = {o["id"]: joint_name(o["id"], o["geometry"]["port"]["name"])
                            for o in d["objects"] if (o.get("geometry") or {}).get("port")}
        self.describe = d

    # ------------------------------------------------------------------ inputs

    def cmd_vel(self, linear: Tuple[float, float, float], angular: Tuple[float, float, float]) -> None:
        """geometry_msgs/Twist: differential drive for ground robots, stick setpoints for the quadcopter."""
        if self.uav:
            vmax = max(1e-3, self.cfg.max_speed)
            self._cmd = (self._now(), {"flight": {
                "pitch": _clamp(linear[0] / vmax, 1), "roll": _clamp(-linear[1] / vmax, 1),
                "throttle": _clamp(0.5 + linear[2] / 1.6, 1, 0), "yaw": _clamp(angular[2] / 3.0, 1)}})
        else:
            self._cmd = (self._now(), {"drive": {"v": float(linear[0]), "w": float(angular[2]), "max_speed": self.cfg.max_speed}})

    def actuator(self, name: str, value: float, mode: Optional[str] = None) -> None:
        a = {"actuator": name, "value": float(value)}
        if mode:
            a["mode"] = mode
        self._pending.append(a)

    def arm(self, armed: bool) -> None:
        self._pending.append({"flight": {"armed": bool(armed), **({"mode": "altitude"} if armed else {})}})

    def call(self, request_json: str) -> str:
        """Any API method: '{"method": "...", "params": {...}}' → JSON result (for the String command topic / service)."""
        req = json.loads(request_json)
        try:
            return json.dumps({"result": self.sim.call(req["method"], **req.get("params", {}))})
        except RobotWinError as exc:
            return json.dumps({"error": {"code": exc.code, "message": exc.message}})

    def urdf(self) -> str:
        """URDF of the bridged robot with its root link named like the base frame."""
        from .urdf import world_to_urdf
        return world_to_urdf(self.describe, root=None if self.kind == "vehicle" else self.base, root_link=self.cfg.base_frame)

    def reset(self, seed: Optional[int] = None) -> None:
        self.sim.reset(seed=seed)
        self._cmd = None
        self._pending.clear()

    def _now(self) -> float:
        return getattr(self, "_time", 0.0)

    # ------------------------------------------------------------------ stepping

    def tick(self) -> List[Tuple[str, str, dict]]:
        actions = list(self._pending)
        self._pending.clear()
        if self._cmd is not None:
            at, act = self._cmd
            if self._now() - at > self.cfg.cmd_vel_timeout:
                act = {"flight": {"pitch": 0, "roll": 0, "yaw": 0, "throttle": 0.5}} if self.uav else {"drive": {"v": 0.0, "w": 0.0, "max_speed": self.cfg.max_speed}}
                self._cmd = None
            actions.append(act)
        period = 1.0 / self.cfg.rate
        steps = max(1, int(round(period / self.cfg.dt)))
        observe = list(self.sensors)
        cam = self.camera_every > 0 and self._ticks % self.camera_every == 0
        if cam:
            observe.append("ros_camera")
        if self.cfg.object_tf and self.kind == "world":
            observe += ["pose:" + o for o in self.objects]
        if self.uav:
            observe.append("flight")
        try:
            r = self.sim.step(dt=self.cfg.dt, steps=steps, actions=actions or None, observe=observe,
                              pace=1.0 if self.cfg.realtime else None, events=True)
        except RobotWinError as exc:
            return [("/robotwin/events", TOPIC_TYPES["/robotwin/events"], string(json.dumps({"error": exc.message})))]
        self._ticks += 1
        t = float(r["time"])
        self._time = t
        obs = r.get("observation", {})
        out: List[Tuple[str, str, dict]] = [("/clock", TOPIC_TYPES["/clock"], clock(t))]
        tfs = []
        odom = obs.get("odom")
        if odom and odom.get("valid", True):
            out.append(("/odom", TOPIC_TYPES["/odom"], odometry(odom, t, self.cfg.odom_frame, self.cfg.base_frame)))
            tfs.append(transform(t, self.cfg.odom_frame, self.cfg.base_frame, odom["position"], odom["orientation"]))
        if self.cfg.object_tf:
            for o in self.objects:
                p = obs.get("pose:" + o)
                if p:
                    tfs.append(transform(t, self.cfg.world_frame, "rw_" + o, p["position"], p["orientation"]))
        if tfs:
            out.append(("/tf", TOPIC_TYPES["/tf"], {"transforms": tfs}))
        if self.static_tf and self._ticks == 1:
            out.append(("/tf_static", TOPIC_TYPES["/tf_static"], {"transforms": [dict(x, header=header(t, x["header"]["frame_id"])) for x in self.static_tf]}))
        if "imu" in obs and obs["imu"].get("valid", True) and "orientation" in obs["imu"]:
            out.append(("/imu", TOPIC_TYPES["/imu"], imu(obs["imu"], t, self.cfg.base_frame)))
        if "gps" in obs and obs["gps"].get("valid", True):
            out.append(("/fix", TOPIC_TYPES["/fix"], nav_sat_fix(obs["gps"], t, self.cfg.base_frame)))
        if "ros_scan" in obs and obs["ros_scan"].get("valid", True):
            out.append(("/scan", TOPIC_TYPES["/scan"], laser_scan(obs["ros_scan"], t, "laser", period)))
        if cam and obs.get("ros_camera", {}).get("valid", True) and "ros_camera" in obs:
            out.append(("/camera/image_raw", TOPIC_TYPES["/camera/image_raw"], image(obs["ros_camera"], t)))
            out.append(("/camera/camera_info", TOPIC_TYPES["/camera/camera_info"], camera_info(obs["ros_camera"], t)))
        encoders = {}
        for name in self.sensors:
            kind = self.sensor_types.get(name)
            if kind == "ultrasonic" and name in obs:
                out.append(("/range/" + _topic(name), RANGE_TYPE, range_msg(obs[name], t, "rw_" + name.split(".")[0])))
            elif kind == "encoder" and name in obs and obs[name].get("valid"):
                oid = name.split(".")[0]
                encoders[self.joint_names.get(oid, oid)] = obs[name]
        if encoders:
            out.append(("/joint_states", TOPIC_TYPES["/joint_states"], joint_state(encoders, t)))
        if "flight" in obs:
            out.append(("/robotwin/flight", TOPIC_TYPES["/robotwin/flight"], string(json.dumps(obs["flight"]))))
        if r.get("events"):
            out.append(("/robotwin/events", TOPIC_TYPES["/robotwin/events"], string(json.dumps(r["events"], ensure_ascii=False))))
        serial = self.sim.serial_read() if self._ticks % 5 == 0 else {}
        text = "".join(serial.values()) if serial else ""
        if text:
            out.append(("/robotwin/serial", TOPIC_TYPES["/robotwin/serial"], string(text)))
        return out


def _clamp(x: float, hi: float, lo: Optional[float] = None) -> float:
    lo = -hi if lo is None else lo
    return max(lo, min(hi, float(x)))


def _topic(name: str) -> str:
    return "".join(c if c.isalnum() or c == "_" else "_" for c in name)


def _most_connected(d: dict) -> str:
    counts: Dict[str, int] = {o["id"]: 0 for o in d["objects"]}
    for j in d.get("joints", []):
        for k in (j.get("child"), j.get("parent")):
            if k in counts:
                counts[k] += 1
    mass = {o["id"]: (o.get("geometry") or {}).get("mass", 0) for o in d["objects"]}
    return max(counts, key=lambda k: (counts[k], mass[k])) if counts else "vehicle"
