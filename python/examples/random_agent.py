"""Random agent on the obstacle-stop task — the smallest possible RL loop (no dependencies).

Rastgele ajan: en küçük pekiştirmeli öğrenme döngüsü (bağımlılık yok).

    python examples/random_agent.py [--episodes 2]
"""

import argparse
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from robotwin import RobotWinEnv  # noqa: E402
from robotwin.tasks import obstacle_stop  # noqa: E402


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--episodes", type=int, default=2)
    ap.add_argument("--steps", type=int, default=60)
    args = ap.parse_args()
    spec = obstacle_stop()
    spec.max_episode_steps = args.steps
    env = RobotWinEnv(spec)
    try:
        print("observation space:", env.observation_space)
        print("action space:     ", env.action_space)
        for ep in range(args.episodes):
            obs, info = env.reset(seed=ep)
            total, done = 0.0, False
            while not done:
                obs, reward, terminated, truncated, info = env.step(env.action_space.sample())
                total += reward
                done = terminated or truncated
            print(f"episode {ep}: return={total:.2f} steps={info['step']} sim_time={info['time']:.2f}s rtf={info['rtf']:.1f}")
    finally:
        env.close()


if __name__ == "__main__":
    main()
