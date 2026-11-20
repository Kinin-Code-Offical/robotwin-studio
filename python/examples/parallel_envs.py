"""Throughput of N simulators in parallel (one headless server process each, no dependencies).

Paralel N simülatörün verimi (her biri ayrı başsız sunucu süreci).

    python examples/parallel_envs.py [--envs 4] [--steps 100]
"""

import argparse
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from robotwin import VectorEnv  # noqa: E402
from robotwin.tasks import hover  # noqa: E402


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--envs", type=int, default=4)
    ap.add_argument("--steps", type=int, default=100)
    args = ap.parse_args()
    with VectorEnv(hover(), args.envs) as venv:
        obs, infos = venv.reset()
        t = time.time()
        for _ in range(args.steps):
            actions = [venv.action_space.sample() for _ in range(args.envs)]
            obs, rewards, terms, truncs, infos = venv.step(actions)
        wall = time.time() - t
        sim = args.steps * args.envs * venv.envs[0].spec_.dt
        print(f"{args.envs} envs × {args.steps} steps: {args.steps * args.envs / wall:.0f} env-steps/s, "
              f"{sim / wall:.1f} simulated s per wall s (aggregate)")


if __name__ == "__main__":
    main()
