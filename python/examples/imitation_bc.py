"""Imitation learning: record PID-expert demonstrations, fit a behaviour-cloning policy, evaluate it.

Taklit öğrenme: PID uzmanından gösterim kaydet, davranış klonlama politikası öğren, değerlendir.

Pure Python (a linear policy by least squares). With numpy installed the demos are also saved as .npz
for imitation-learning libraries (``imitation``, ``d3rlpy`` …).

    python examples/imitation_bc.py [--episodes 3] [--out demos/obstacle.jsonl]
"""

import argparse
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from robotwin import RobotWinEnv  # noqa: E402
from robotwin.imitation import fit_linear_policy, load_demos, record_episodes, save_npz  # noqa: E402
from robotwin.tasks import TARGET_GAP, front_distance, obstacle_stop  # noqa: E402

from pid_obstacle import expert  # noqa: E402


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--episodes", type=int, default=3)
    ap.add_argument("--steps", type=int, default=120)
    ap.add_argument("--out", default=os.path.join("demos", "obstacle.jsonl"))
    args = ap.parse_args()

    spec = obstacle_stop()
    spec.max_episode_steps = args.steps
    env = RobotWinEnv(spec)
    try:
        n = record_episodes(env, expert(env), args.out, episodes=args.episodes, metadata={"task": "obstacle_stop", "expert": "pid"})
        print(f"recorded {n} steps of {args.episodes} expert episodes -> {args.out}")
        demos = load_demos(args.out)
        try:
            save_npz(os.path.splitext(args.out)[0] + ".npz", demos)
            print("also saved", os.path.splitext(args.out)[0] + ".npz")
        except ImportError:
            pass
        policy = fit_linear_policy(demos)
        policy.low, policy.high = [a["low"] for a in spec.actions], [a["high"] for a in spec.actions]

        obs, info = env.reset(seed=123)   # an unseen start
        total = 0.0
        for _ in range(args.steps):
            obs, reward, terminated, truncated, info = env.step(policy(obs))
            total += reward
            if terminated or truncated:
                break
        gap = front_distance(info["raw"])
        print(f"cloned policy: return={total:.2f} final gap={gap:.3f} m (target {TARGET_GAP} m)")
    finally:
        env.close()


if __name__ == "__main__":
    main()
