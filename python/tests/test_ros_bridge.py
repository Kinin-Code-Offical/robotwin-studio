"""ROS bridge tests that need no ROS: message shapes, the bridge core and the rosbridge WebSocket server."""

import base64
import json
import math
import os
import socket
import struct
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from robotwin import find_server, launch  # noqa: E402
from robotwin import ros  # noqa: E402
from robotwin.rosbridge import RosbridgeServer, _full_text  # noqa: E402

try:
    find_server()
    HAVE_SERVER = True
except FileNotFoundError:
    HAVE_SERVER = False


class MessageShapeTests(unittest.TestCase):
    def test_stamp_and_scan(self):
        self.assertEqual(ros.stamp(12.25), {"sec": 12, "nanosec": 250000000})
        scan = ros.laser_scan({"angle_min": -1.0, "angle_max": 1.0, "angle_increment": 0.5, "range_min": 0.05,
                               "range_max": 8.0, "ranges": [1.0, None, 2.0, 3.0, None]}, 1.0)
        self.assertTrue(math.isinf(scan["ranges"][1]))
        self.assertEqual(scan["header"]["frame_id"], "laser")

    def test_quaternion_from_rpy(self):
        q = ros.quat_from_rpy(0, 0, math.pi / 2)
        self.assertAlmostEqual(q[2], math.sqrt(0.5), places=9)
        self.assertAlmostEqual(q[3], math.sqrt(0.5), places=9)

    def test_message_definitions(self):
        text = _full_text("sensor_msgs/msg/LaserScan")
        self.assertIn("float32[] ranges", text)
        self.assertIn("MSG: std_msgs/Header", text)
        self.assertIn("MSG: builtin_interfaces/Time", text)


class _WsClient:
    """Tiny RFC 6455 client (text frames, masked) for the test."""

    def __init__(self, port):
        self.sock = socket.create_connection(("127.0.0.1", port), timeout=20)
        key = base64.b64encode(os.urandom(16)).decode()
        self.sock.sendall((f"GET / HTTP/1.1\r\nHost: localhost\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
                           f"Sec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n\r\n").encode())
        resp = b""
        while b"\r\n\r\n" not in resp:
            resp += self.sock.recv(4096)
        assert b"101" in resp.split(b"\r\n")[0], resp
        self.buf = resp.split(b"\r\n\r\n", 1)[1]

    def send(self, obj):
        data = json.dumps(obj).encode()
        mask = os.urandom(4)
        n = len(data)
        head = struct.pack("!BB", 0x81, 0x80 | n) if n < 126 else struct.pack("!BBH", 0x81, 0x80 | 126, n)
        self.sock.sendall(head + mask + bytes(b ^ mask[i % 4] for i, b in enumerate(data)))

    def _exact(self, n):
        while len(self.buf) < n:
            chunk = self.sock.recv(65536)
            if not chunk:
                raise ConnectionError
            self.buf += chunk
        out, self.buf = self.buf[:n], self.buf[n:]
        return out

    def recv(self):
        b1, b2 = self._exact(2)
        n = b2 & 0x7F
        if n == 126:
            n = struct.unpack("!H", self._exact(2))[0]
        elif n == 127:
            n = struct.unpack("!Q", self._exact(8))[0]
        return json.loads(self._exact(n).decode())

    def close(self):
        self.sock.close()


@unittest.skipUnless(HAVE_SERVER, "RobotWin.Headless not built")
class BridgeTests(unittest.TestCase):
    def test_bridge_tick_produces_topics(self):
        with launch(example="car") as server:
            client = server.client()
            bridge = ros.SimBridge(client, ros.BridgeConfig(realtime=False, camera={"width": 16, "height": 12, "every": 1}))
            bridge.cmd_vel((0.2, 0, 0), (0, 0, 0))
            topics = {}
            for _ in range(5):
                for topic, rtype, msg in bridge.tick():
                    topics[topic] = (rtype, msg)
            for t in ("/clock", "/tf", "/tf_static", "/odom", "/scan", "/imu", "/fix", "/camera/image_raw", "/camera/camera_info", "/joint_states"):
                self.assertIn(t, topics)
            self.assertEqual(topics["/odom"][0], "nav_msgs/msg/Odometry")
            self.assertEqual(len(topics["/scan"][1]["ranges"]), 360)
            self.assertEqual(topics["/camera/image_raw"][1]["encoding"], "rgb8")
            client.close()

    def test_uav_cmd_vel_maps_to_flight_sticks(self):
        with launch(example="quadcopter") as server:
            client = server.client()
            bridge = ros.SimBridge(client, ros.BridgeConfig(realtime=False, scan=None))
            bridge.arm(True)
            bridge.cmd_vel((0.25, 0, 0.0), (0, 0, 0))
            for _ in range(3):
                bridge.tick()
            st = client.flight_status()
            self.assertTrue(st["armed"])
            self.assertGreater(st["pitch_input"], 0.0)
            client.close()

    def test_rosbridge_websocket(self):
        with launch(example="car") as server:
            client = server.client()
            rb = RosbridgeServer(ros.SimBridge(client, ros.BridgeConfig(realtime=False)), port=0).start()
            ws = _WsClient(rb.port)
            try:
                ws.send({"op": "call_service", "service": "/rosapi/topics", "id": "t1"})
                ws.send({"op": "subscribe", "topic": "/odom"})
                ws.send({"op": "advertise", "topic": "/cmd_vel", "type": "geometry_msgs/msg/Twist"})
                ws.send({"op": "publish", "topic": "/cmd_vel", "msg": {"linear": {"x": 0.2}, "angular": {"z": 0.0}}})
                got_topics, odom = None, None
                for _ in range(200):
                    m = ws.recv()
                    if m.get("op") == "service_response":
                        got_topics = m["values"]["topics"]
                    elif m.get("op") == "publish" and m["topic"] == "/odom":
                        odom = m["msg"]
                    if got_topics and odom:
                        break
                self.assertIn("/scan", got_topics)
                self.assertEqual(odom["child_frame_id"], "base_link")
            finally:
                ws.close()
                rb.stop()
                client.close()


if __name__ == "__main__":
    unittest.main()
