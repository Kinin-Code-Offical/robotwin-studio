"""UAV: arm the micro quadcopter, hold altitude, fly a square of waypoints with a position PID.

İHA: mikro quadcopter'ı kur, irtifa tut, konum PID'i ile kare rota uç.

The flight controller of the app stabilises attitude; this script closes the position loop on top of
it (outer loop: position error → pitch/roll stick, like a companion computer sending setpoints).

    python examples/quadcopter_waypoints.py
"""

import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from robotwin import launch  # noqa: E402

WAYPOINTS = [(0.0, 0.0, 1.0), (1.0, 0.0, 1.0), (1.0, 1.0, 1.2), (0.0, 1.0, 1.0), (0.0, 0.0, 0.8)]


def clamp(x, lim):
    return max(-lim, min(lim, x))


def main() -> None:
    with launch(example="quadcopter", seed=3) as server:
        sim = server.client()
        sim.add_sensor("gps", name="fix", object="vehicle", noise=0.02)
        print(sim.flight(mode="altitude", target_altitude=WAYPOINTS[0][2], armed=True))
        dt = 0.02
        ix = iy = 0.0
        for wx, wy, wz in WAYPOINTS:
            sim.flight(target_altitude=wz)
            reached = False
            for k in range(int(8.0 / dt)):
                r = sim.step(dt=dt, observe=["odom"])
                odom = r["observation"]["odom"]
                x, y, z = odom["position"]
                vx, vy, _ = odom["linear_velocity_world"]
                yaw = odom["rpy"][2]
                # World-frame PID → body-frame stick inputs (pitch forward = +x body, roll right = −y body).
                ix = clamp(ix + (wx - x) * dt, 1.5)
                iy = clamp(iy + (wy - y) * dt, 1.5)
                ax = clamp(0.45 * (wx - x) + 0.12 * ix - 0.75 * vx, 1.0)
                ay = clamp(0.45 * (wy - y) + 0.12 * iy - 0.75 * vy, 1.0)
                fwd = math.cos(yaw) * ax + math.sin(yaw) * ay
                left = -math.sin(yaw) * ax + math.cos(yaw) * ay
                sim.flight(pitch=clamp(fwd, 0.6), roll=clamp(-left, 0.6), yaw=clamp(-1.5 * yaw, 0.5))
                if math.dist((x, y, z), (wx, wy, wz)) < 0.1 and math.hypot(vx, vy) < 0.15:
                    reached = True
                    break
            fix = sim.read_sensor("fix")
            print(f"waypoint ({wx:.1f}, {wy:.1f}, {wz:.1f}) {'reached' if reached else 'timeout'} at t={r['time']:.1f}s: pos=({x:+.2f}, {y:+.2f}, {z:.2f})"
                  f"  gps=({fix['latitude']:.6f}, {fix['longitude']:.6f})")
        # Land: sticks centred, slow descent to the floor, then disarm.
        sim.flight(pitch=0.0, roll=0.0, yaw=0.0, target_altitude=0.0, throttle=0.35)
        sim.step(dt=0.02, steps=300)
        print(sim.flight_status())
        sim.arm(False)
        for e in sim.events():
            print(f"  {e['time']:.2f}s {e['source']}: {e['message']}")


if __name__ == "__main__":
    main()
