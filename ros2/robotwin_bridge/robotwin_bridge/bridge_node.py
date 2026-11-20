"""ROS 2 node bridging a RobotWin Studio simulation (rclpy).

Publishes /clock, /tf, /tf_static, /odom, /scan, /imu, /fix, /camera/image_raw, /camera/camera_info,
/joint_states, /range/<sensor>, /robot_description and /robotwin/{events,serial,flight}; subscribes
/cmd_vel (differential drive, or stick setpoints for the quadcopter), /robotwin/arm,
/robotwin/actuator/<name> and /robotwin/command (any API call as JSON); /robotwin/reset service.

Türkçe: RobotWin simülasyonunu ROS 2'ye bağlayan düğüm.

    ros2 run robotwin_bridge bridge --ros-args -p example:=car -p use_sim_time:=true
"""

from __future__ import annotations

import base64
import json
import os
import sys
import threading
from typing import Any, Dict

import rclpy
from rclpy.node import Node
from rclpy.qos import DurabilityPolicy, QoSProfile, ReliabilityPolicy

from geometry_msgs.msg import Twist
from nav_msgs.msg import Odometry
from rosgraph_msgs.msg import Clock
from sensor_msgs.msg import CameraInfo, Image, Imu, JointState, LaserScan, NavSatFix, Range
from std_msgs.msg import Bool, Float64, String
from std_srvs.srv import Empty
from tf2_msgs.msg import TFMessage


def _import_robotwin():
    """The robotwin package: installed (pip install -e python/) or found next to this repository's ros2 folder."""
    try:
        import robotwin  # noqa: F401
    except ImportError:
        here = os.path.dirname(os.path.abspath(__file__))
        for up in range(2, 7):
            cand = os.path.abspath(os.path.join(here, *[".."] * up, "python"))
            if os.path.isdir(os.path.join(cand, "robotwin")):
                sys.path.insert(0, cand)
                break
        if os.environ.get("ROBOTWIN_PYTHON"):
            sys.path.insert(0, os.environ["ROBOTWIN_PYTHON"])
    from robotwin import RobotWinClient, launch
    from robotwin.ros import BridgeConfig, SimBridge
    return RobotWinClient, launch, BridgeConfig, SimBridge


MSG_TYPES = {
    "rosgraph_msgs/msg/Clock": Clock, "tf2_msgs/msg/TFMessage": TFMessage, "nav_msgs/msg/Odometry": Odometry,
    "sensor_msgs/msg/LaserScan": LaserScan, "sensor_msgs/msg/Imu": Imu, "sensor_msgs/msg/NavSatFix": NavSatFix,
    "sensor_msgs/msg/Image": Image, "sensor_msgs/msg/CameraInfo": CameraInfo, "sensor_msgs/msg/JointState": JointState,
    "sensor_msgs/msg/Range": Range, "std_msgs/msg/String": String,
}


try:
    from rosidl_runtime_py.set_message import set_message_fields
except ImportError:  # very old distributions
    set_message_fields = None


def to_msg(cls, data: Dict[str, Any]):
    """ROS-shaped dict → message: rosidl_runtime_py does the work, :func:`fill` is the fallback."""
    if cls is Image and isinstance(data.get("data"), str):
        data = dict(data, data=list(base64.b64decode(data["data"])))
    msg = cls()
    if set_message_fields is not None:
        try:
            set_message_fields(msg, data)
            return msg
        except (TypeError, ValueError, AttributeError, AssertionError):
            msg = cls()
    return fill(msg, data)


def fill(msg, data: Dict[str, Any]):
    """Copies a ROS-shaped dict into a message (recursively; Image.data arrives as base64)."""
    for key, value in data.items():
        if not hasattr(msg, key):
            continue
        current = getattr(msg, key)
        if isinstance(value, dict):
            fill(current, value)
        elif isinstance(value, list) and value and isinstance(value[0], dict):
            cls = type(current[0]) if current else None
            items = []
            for v in value:
                if cls is None:
                    # Element class from the field's type annotation (rosidl slot types).
                    slot = dict(zip(msg.__slots__, msg.SLOT_TYPES))["_" + key]
                    mod = __import__(slot.value_type.namespaces[0] + ".msg", fromlist=[slot.value_type.name])
                    cls = getattr(mod, slot.value_type.name)
                items.append(fill(cls(), v))
            setattr(msg, key, items)
        elif key == "data" and isinstance(msg, Image) and isinstance(value, str):
            msg.data = base64.b64decode(value)
        else:
            if isinstance(current, float) and isinstance(value, int):
                value = float(value)
            setattr(msg, key, value)
    return msg


class RobotWinBridge(Node):
    def __init__(self):
        super().__init__("robotwin_bridge")
        RobotWinClient, launch, BridgeConfig, SimBridge = _import_robotwin()
        p = self.declare_parameter
        host = p("host", "").value
        port = p("port", 5555).value
        example = p("example", "car").value
        world = p("world", "").value
        seed = p("seed", 0).value
        firmware = p("firmware", True).value
        self._server = None
        if host:
            self.client = RobotWinClient(host, port)
            if world or example:
                self.client.load(path=world or None, example=None if world else example, seed=seed, firmware=firmware)
        else:
            self._server = launch(world=world or None, example=None if world else example, seed=seed, firmware=firmware)
            self.client = self._server.client()
        cfg = BridgeConfig(
            base=p("base", "").value or None,
            odom_frame=p("odom_frame", "odom").value,
            base_frame=p("base_frame", "base_link").value,
            world_frame=p("world_frame", "map").value,
            dt=p("dt", 0.005).value,
            rate=p("rate", 50.0).value,
            realtime=p("realtime", True).value,
            imu=p("imu", True).value,
            gps=p("gps", True).value,
            object_tf=p("object_tf", True).value,
            max_speed=p("max_speed", 0.5).value,
        )
        if p("scan", True).value:
            cfg.scan = {"samples": p("scan_samples", 360).value, "range_max": p("scan_range_max", 8.0).value,
                        "offset": list(p("scan_offset", [0.0, 0.0, 0.06]).value)}
        else:
            cfg.scan = None
        if p("camera", False).value:
            cfg.camera = {"width": p("camera_width", 160).value, "height": p("camera_height", 120).value,
                          "fov_deg": p("camera_fov_deg", 70.0).value, "offset": list(p("camera_offset", [0.08, 0.0, 0.06]).value),
                          "every": p("camera_every", 5).value}
        self.bridge = SimBridge(self.client, cfg)
        self.lock = threading.Lock()
        self.pubs: Dict[str, Any] = {}
        latched = QoSProfile(depth=1, durability=DurabilityPolicy.TRANSIENT_LOCAL, reliability=ReliabilityPolicy.RELIABLE)
        self.static_qos = latched
        desc = self.create_publisher(String, "/robot_description", latched)
        try:
            desc.publish(String(data=self.bridge.urdf()))
        except Exception as exc:  # URDF is a convenience; never stop the bridge for it
            self.get_logger().warning(f"URDF export failed: {exc}")
        self.cmd_result = self.create_publisher(String, "/robotwin/command_result", 10)
        self.create_subscription(Twist, "/cmd_vel", self._on_cmd_vel, 10)
        self.create_subscription(Bool, "/robotwin/arm", lambda m: self._locked(self.bridge.arm, m.data), 10)
        self.create_subscription(String, "/robotwin/command", self._on_command, 10)
        for a in self.client.actuators():
            name = a["name"]
            topic = "/robotwin/actuator/" + "".join(c if c.isalnum() or c == "_" else "_" for c in name)
            mode = {"motor": "voltage", "servo": "angle", "joint": "speed"}.get(a["type"], "duty")
            self.create_subscription(Float64, topic, lambda m, n=name, md=mode: self._locked(self.bridge.actuator, n, m.data, md), 10)
        self.create_service(Empty, "/robotwin/reset", self._on_reset)
        self.get_logger().info(f"RobotWin bridge: {self.bridge.kind} '{self.client.describe()['title']}', base={self.bridge.base}, "
                               f"{len(self.bridge.sensors)} sensors, realtime={cfg.realtime}")
        self._running = True
        self._thread = threading.Thread(target=self._loop, daemon=True)
        self._thread.start()

    def _locked(self, fn, *args):
        with self.lock:
            fn(*args)

    def _on_cmd_vel(self, m: Twist):
        self._locked(self.bridge.cmd_vel, (m.linear.x, m.linear.y, m.linear.z), (m.angular.x, m.angular.y, m.angular.z))

    def _on_command(self, m: String):
        with self.lock:
            result = self.bridge.call(m.data)
        self.cmd_result.publish(String(data=result))

    def _on_reset(self, request, response):
        self._locked(self.bridge.reset)
        return response

    def _publisher(self, topic: str, rtype: str):
        pub = self.pubs.get(topic)
        if pub is None:
            cls = MSG_TYPES[rtype]
            qos = self.static_qos if topic == "/tf_static" else 10
            pub = self.pubs[topic] = self.create_publisher(cls, topic, qos)
        return pub

    def _loop(self):
        while self._running and rclpy.ok():
            with self.lock:
                try:
                    messages = self.bridge.tick()
                except (ConnectionError, OSError) as exc:
                    self.get_logger().error(f"simulation connection lost: {exc}")
                    break
            for topic, rtype, data in messages:
                if rtype not in MSG_TYPES:
                    continue
                self._publisher(topic, rtype).publish(to_msg(MSG_TYPES[rtype], data))

    def destroy_node(self):
        self._running = False
        self._thread.join(timeout=2)
        try:
            self.client.close()
        finally:
            if self._server is not None:
                self._server.close()
        super().destroy_node()


def main(args=None):
    rclpy.init(args=args)
    node = RobotWinBridge()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()


if __name__ == "__main__":
    main()
