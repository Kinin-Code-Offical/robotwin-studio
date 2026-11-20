"""End-to-end tests: start a headless RobotWin server and drive it through the Python package.

Run from the repository root (after building the headless tool):

    dotnet build tools/RobotWin.Headless -c Debug
    python -m unittest discover -s python/tests -v

Skipped when no server binary is found (set ROBOTWIN_SERVER to point at one).
"""

import json
import math
import os
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from robotwin import RobotWinClient, RobotWinEnv, RobotWinError, VectorEnv, decode_image, find_server, launch  # noqa: E402
from robotwin.imitation import DemoRecorder, fit_linear_policy, load_demos  # noqa: E402
from robotwin.tasks import hover, line_follow, line_position, obstacle_stop  # noqa: E402
from robotwin.urdf import world_to_urdf  # noqa: E402

try:
    find_server()
    HAVE_SERVER = True
except FileNotFoundError:
    HAVE_SERVER = False


@unittest.skipUnless(HAVE_SERVER, "RobotWin.Headless not built (dotnet build tools/RobotWin.Headless)")
class ApiTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = launch()
        cls.sim: RobotWinClient = cls.server.client()

    @classmethod
    def tearDownClass(cls):
        cls.sim.close()
        cls.server.close()

    def test_hello_and_methods(self):
        info = self.sim.call("hello")
        self.assertEqual(info["api_version"], "1.0")
        methods = self.sim.call("methods")
        for m in ("world.load", "sim.step", "sensor.read", "actuator.set", "flight.set", "serial.read", "circuit.part"):
            self.assertIn(m, methods)

    def test_deterministic_with_same_seed(self):
        def run(seed):
            self.sim.load(example="car", seed=seed, sensors=[{"type": "imu", "name": "imu", "object": "O1", "accel_noise": 0.05}])
            self.sim.drive(0.15, 0.5)
            r = self.sim.step(dt=0.01, steps=50, observe=["imu", "pose:O1"])
            return r["observation"]

        a, b, c = run(4), run(4), run(5)
        self.assertEqual(a["pose:O1"]["position"], b["pose:O1"]["position"])
        self.assertEqual(a["imu"], b["imu"])
        self.assertNotEqual(a["imu"]["linear_acceleration"], c["imu"]["linear_acceleration"])

    def test_reset_and_exact_time(self):
        self.sim.load(example="car", seed=1)
        p0 = self.sim.get_pose("O1")["position"]
        r = self.sim.step(dt=0.013, steps=7)
        self.assertAlmostEqual(r["time"], 0.091, places=9)
        first = self.sim.step(dt=0.01, steps=20, observe=["pose:O1"])["observation"]["pose:O1"]["position"]
        reset = self.sim.reset()
        self.assertEqual(reset["time"], 0)
        self.assertEqual(self.sim.get_pose("O1")["position"], p0)
        self.sim.step(dt=0.013, steps=7)
        again = self.sim.step(dt=0.01, steps=20, observe=["pose:O1"])["observation"]["pose:O1"]["position"]
        self.assertEqual(first, again)

    def test_set_pose_moves_the_assembly(self):
        self.sim.load(example="car")
        pose = self.sim.set_pose("O1", position=[0.5, -0.2, 0.05], rpy_deg=[0, 0, 45])
        self.assertAlmostEqual(pose["rpy"][2], math.radians(45), places=6)
        wheel = self.sim.get_pose("O4")["position"]
        self.assertLess(math.dist(wheel[:2], [0.5, -0.2]), 0.1)

    def test_lidar_camera_and_obstacles(self):
        self.sim.load(example="car", environment={"obstacles": [{"x": 0.6, "y": 0, "size_x": 0.1, "size_y": 1.0, "height": 0.3}]})
        self.sim.add_sensor("lidar", name="scan", object="O1", offset=[0, 0, 0.05], samples=8)
        self.sim.add_sensor("camera", name="cam", object="O1", offset=[0.08, 0, 0.05], width=20, height=10)
        self.sim.add_sensor("depth", name="depth", object="O1", offset=[0.08, 0, 0.05], width=8, height=4)
        scan = self.sim.read_sensor("scan")
        self.assertEqual(len(scan["ranges"]), 8)
        self.assertGreater(scan["ranges"][4], 0.4)
        self.assertLess(scan["ranges"][4], 0.6)
        img = decode_image(self.sim.read_sensor("cam"))
        self.assertEqual(len(img), 10)
        depth = decode_image(self.sim.read_sensor("depth"))
        self.assertEqual(len(depth[0]), 8)
        self.assertTrue(any(0.3 < float(v) < 0.7 for v in depth[2]))

    def test_quadcopter_arm_and_hold(self):
        self.sim.load(example="quadcopter")
        st = self.sim.flight(mode="altitude", target_altitude=0.6, armed=True)
        self.assertTrue(st["armed"])
        self.sim.step(dt=0.02, steps=200)
        self.assertAlmostEqual(self.sim.flight_status()["altitude"], 0.6, delta=0.15)
        fix = self.sim.read_sensor("gps")
        self.assertAlmostEqual(fix["latitude"], 41.0082, delta=1e-3)

    def test_firmware_serial_and_pins(self):
        self.sim.load(example="obstacle-avoider")
        self.sim.step(dt=0.01, steps=40)
        text = "".join(self.sim.serial_read().values())
        self.assertIn("hazir", text)
        self.sim.enable_firmware(False)
        pin = self.sim.pin_write("D5", "pwm", duty=0.5)
        self.assertTrue(pin["overridden"])
        self.assertAlmostEqual(pin["pwm_duty"], 0.5)
        part = self.sim.part("U3")
        self.assertTrue(part["devices"])

    def test_errors(self):
        with self.assertRaises(RobotWinError) as ctx:
            self.sim.call("no.such.method")
        self.assertEqual(ctx.exception.code, -32601)
        self.sim.load(example="car")
        with self.assertRaises(RobotWinError) as ctx:
            self.sim.read_sensor("nope")
        self.assertEqual(ctx.exception.code, -32002)
        with self.assertRaises(RobotWinError):
            self.sim.flight(armed=True)   # not a quadcopter

    def test_batch(self):
        self.sim.load(example="quadcopter")
        res = self.sim.batch([("flight.arm", {}), ("sim.step", {"dt": 0.01, "steps": 3}), ("sim.status", {})])
        self.assertTrue(res[0]["armed"])
        self.assertAlmostEqual(res[2]["time"], 0.03, places=9)

    def test_urdf_export(self):
        self.sim.load(example="car")
        urdf = world_to_urdf(self.sim.describe(), root="O1")
        root = ET.fromstring(urdf)
        links = {l.get("name") for l in root.findall("link")}
        self.assertIn("O1", links)
        self.assertIn("O2_shaft", links)
        self.assertEqual(len(root.findall("joint")), len(links) - 1)   # a tree


@unittest.skipUnless(HAVE_SERVER, "RobotWin.Headless not built")
class EnvTests(unittest.TestCase):
    def test_env_reset_step_and_spaces(self):
        env = RobotWinEnv(obstacle_stop())
        try:
            obs, info = env.reset(seed=3)
            self.assertEqual(len(obs), env.observation_space.shape[0])
            obs2, reward, term, trunc, info = env.step([0.2, 0.0])
            self.assertEqual(len(obs2), 8)
            self.assertIsInstance(reward, float)
            self.assertAlmostEqual(info["time"], 0.05, places=9)
            # Same seed → same start.
            a, _ = env.reset(seed=3)
            b, _ = env.reset(seed=3)
            self.assertEqual(list(map(float, a)), list(map(float, b)))
        finally:
            env.close()

    def test_line_task_sees_the_line(self):
        env = RobotWinEnv(line_follow())
        try:
            obs, info = env.reset(seed=0)
            self.assertFalse(math.isnan(line_position(info["raw"])), "the car must start on the line")
        finally:
            env.close()

    def test_vector_env(self):
        with VectorEnv(hover(), 2) as venv:
            obs, infos = venv.reset()
            self.assertEqual(len(obs), 2)
            obs, rewards, terms, truncs, infos = venv.step([[0.6, 0, 0, 0], [0.6, 0, 0, 0]])
            self.assertEqual(len(rewards), 2)

    def test_demo_recording_and_cloning(self):
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "demos.jsonl")
            with DemoRecorder(path, {"task": "unit"}) as rec:
                for ep in range(2):
                    for t in range(20):
                        x = [t / 20.0, ep]
                        rec.record(x, [2 * x[0] - x[1] + 0.5], reward=1.0, truncated=t == 19)
            demos = load_demos(path)
            self.assertEqual(len(demos), 2)
            policy = fit_linear_policy(demos)
            self.assertAlmostEqual(policy([0.5, 1.0])[0], 0.5, places=3)
            with open(path, encoding="utf-8") as f:
                self.assertEqual(json.loads(f.readline())["format"], "robotwin.demos")


if __name__ == "__main__":
    unittest.main()
