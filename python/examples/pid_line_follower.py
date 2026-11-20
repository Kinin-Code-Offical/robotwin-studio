"""PID line follower on the oval track (world mode, virtual line sensors, differential drive).

Çizgi izleyen PID: oval pistte beş sanal çizgi sensörü ve diferansiyel sürüş.

    python examples/pid_line_follower.py [--seconds 20] [--realtime]
"""

import argparse
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from robotwin import RobotWinEnv  # noqa: E402
from robotwin.tasks import line_follow, line_position  # noqa: E402


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--seconds", type=float, default=20.0)
    ap.add_argument("--kp", type=float, default=90.0)
    ap.add_argument("--kd", type=float, default=2.5)
    args = ap.parse_args()

    spec = line_follow(speed=0.15)
    spec.max_episode_steps = int(args.seconds / spec.dt)
    env = RobotWinEnv(spec)
    try:
        obs, info = env.reset(seed=1)
        last, total, steps, lost = 0.0, 0.0, 0, 0
        while True:
            p = line_position(info["raw"])
            if math.isnan(p):
                p = math.copysign(0.03, last)  # keep turning toward where the line was last seen
                lost += 1
            w = args.kp * p + args.kd * (p - last) / spec.dt
            last = p
            obs, reward, terminated, truncated, info = env.step([w])
            total += reward
            steps += 1
            if steps % 100 == 0:
                pose = env.client.get_pose("O1")["position"]
                print(f"t={info['time']:5.2f}s  line={p * 1000:+6.1f} mm  w={w:+5.2f}  pos=({pose[0]:+.2f}, {pose[1]:+.2f})  rtf={info['rtf']:.1f}")
            if terminated or truncated:
                break
        print(f"steps={steps} return={total:.1f} lost_frames={lost} ended_by={'line lost' if terminated else 'time'}")
    finally:
        env.close()


if __name__ == "__main__":
    main()
