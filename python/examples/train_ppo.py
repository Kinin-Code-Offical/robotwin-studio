"""Minimal RL training: PPO (Stable-Baselines3) on the obstacle-stop task with parallel simulators.

Asgari PPO eğitimi (Stable-Baselines3), paralel simülatörlerle. Requires: pip install gymnasium stable-baselines3

    python examples/train_ppo.py [--envs 4] [--timesteps 20000]
"""

import argparse
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))


def make_spec():
    from robotwin.tasks import obstacle_stop
    return obstacle_stop()


def main() -> int:
    try:
        import gymnasium  # noqa: F401
        from stable_baselines3 import PPO
        from stable_baselines3.common.vec_env import SubprocVecEnv, VecMonitor
    except ImportError as exc:
        print(f"skipped: {exc.name} is not installed (pip install gymnasium stable-baselines3)")
        return 0

    from robotwin import RobotWinEnv

    ap = argparse.ArgumentParser()
    ap.add_argument("--envs", type=int, default=4)
    ap.add_argument("--timesteps", type=int, default=20000)
    ap.add_argument("--out", default="ppo_obstacle")
    args = ap.parse_args()

    def factory(i):
        def make():
            spec = make_spec()
            spec.seed = 1000 * i
            return RobotWinEnv(spec)
        return make

    # Each SB3 worker process starts its own headless RobotWin server.
    venv = VecMonitor(SubprocVecEnv([factory(i) for i in range(args.envs)]))
    model = PPO("MlpPolicy", venv, n_steps=256, batch_size=256, learning_rate=3e-4, verbose=1, seed=0)
    model.learn(total_timesteps=args.timesteps)
    model.save(args.out)
    venv.close()

    env = RobotWinEnv(make_spec())
    obs, _ = env.reset(seed=999)
    total = 0.0
    for _ in range(200):
        action, _ = model.predict(obs, deterministic=True)
        obs, reward, terminated, truncated, info = env.step(action)
        total += reward
        if terminated or truncated:
            break
    print(f"evaluation return {total:.2f}")
    env.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
