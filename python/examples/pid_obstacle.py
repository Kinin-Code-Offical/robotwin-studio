"""PID controller: drive toward a wall and stop 25 cm before it (LiDAR + odometry + differential drive).

Engel PID'i: duvara yaklaş ve 25 cm önünde dur (LiDAR + odometri + diferansiyel sürüş).

    python examples/pid_obstacle.py
"""

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from robotwin import RobotWinEnv  # noqa: E402
from robotwin.tasks import TARGET_GAP, front_distance, obstacle_stop  # noqa: E402


class Pid:
    def __init__(self, kp: float, ki: float, kd: float, limit: float):
        self.kp, self.ki, self.kd, self.limit = kp, ki, kd, limit
        self.i = 0.0
        self.last = None

    def __call__(self, error: float, dt: float) -> float:
        self.i = max(-self.limit, min(self.limit, self.i + error * dt))
        d = 0.0 if self.last is None else (error - self.last) / dt
        self.last = error
        return max(-self.limit, min(self.limit, self.kp * error + self.ki * self.i + self.kd * d))


def expert(env: RobotWinEnv):
    """Returns a policy obs → action using the raw readings of the env (also used as the imitation expert)."""
    speed = Pid(1.2, 0.05, 0.05, 0.3)
    heading = Pid(2.0, 0.0, 0.1, 1.0)

    def policy(obs):
        raw = env._last_raw
        gap = front_distance(raw) - TARGET_GAP
        v = speed(gap, env.spec_.dt)
        yaw = (raw.get("odom") or {}).get("rpy", [0, 0, 0])[2]
        return [v, heading(-yaw, env.spec_.dt)]

    return policy


def main() -> None:
    env = RobotWinEnv(obstacle_stop())
    try:
        obs, info = env.reset(seed=0)
        policy = expert(env)
        for k in range(160):
            obs, reward, terminated, truncated, info = env.step(policy(obs))
            if k % 20 == 0:
                print(f"t={info['time']:5.2f}s  front={front_distance(info['raw']):.3f} m  v={info['action'][0]['drive']['v']:+.3f}  reward={reward:+.3f}")
            if terminated or truncated:
                break
        d = front_distance(info["raw"])
        print(f"final gap {d:.3f} m (target {TARGET_GAP} m) -> {'OK' if abs(d - TARGET_GAP) < 0.05 else 'off'}")
    finally:
        env.close()


if __name__ == "__main__":
    main()
