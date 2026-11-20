"""Many simulations in parallel: one headless server process per environment.

The simulation work happens in the server processes, so a thread per environment on the Python side is
enough to keep every core busy (no pickling, works the same on Windows and Linux). With gymnasium
installed, :func:`make_gym_vector_env` returns a ``gymnasium.vector.AsyncVectorEnv`` instead.

Türkçe: Paralel ortamlar — her ortam kendi başsız sunucu sürecinde çalışır.
"""

from __future__ import annotations

from concurrent.futures import ThreadPoolExecutor
from typing import Any, Callable, List, Optional, Sequence

from .env import EnvSpec, RobotWinEnv, _np

__all__ = ["VectorEnv", "make_gym_vector_env"]


class VectorEnv:
    """``n`` copies of an environment stepped together; finished episodes reset automatically.

    ``step(actions)`` returns stacked (obs, rewards, terminated, truncated, infos); when an episode ends,
    ``infos[i]["final_observation"]`` holds its last observation and ``obs[i]`` is the first of the next.
    """

    def __init__(self, spec: EnvSpec, n: int, seeds: Optional[Sequence[int]] = None):
        self.n = n
        self.seeds = list(seeds) if seeds is not None else [spec.seed + 1000 * i for i in range(n)]
        self._pool = ThreadPoolExecutor(max_workers=n)
        specs = []
        for s in self.seeds:
            copy = EnvSpec(**{**spec.__dict__})
            copy.seed = s
            specs.append(copy)
        self.envs: List[RobotWinEnv] = list(self._pool.map(lambda sp: RobotWinEnv(sp), specs))
        self.observation_space = self.envs[0].observation_space
        self.action_space = self.envs[0].action_space

    def _stack(self, items):
        return _np.stack(items) if _np is not None else list(items)

    def reset(self, seed: Optional[int] = None):
        def one(i):
            return self.envs[i].reset(seed=None if seed is None else seed + i)
        results = list(self._pool.map(one, range(self.n)))
        return self._stack([r[0] for r in results]), [r[1] for r in results]

    def step(self, actions):
        def one(i):
            env = self.envs[i]
            obs, rew, term, trunc, info = env.step(actions[i])
            if term or trunc:
                info = dict(info)
                info["final_observation"] = obs
                obs, _ = env.reset()
            return obs, rew, term, trunc, info
        results = list(self._pool.map(one, range(self.n)))
        obs = self._stack([r[0] for r in results])
        rewards = [r[1] for r in results]
        terms = [r[2] for r in results]
        truncs = [r[3] for r in results]
        if _np is not None:
            rewards, terms, truncs = _np.asarray(rewards, dtype=_np.float32), _np.asarray(terms), _np.asarray(truncs)
        return obs, rewards, terms, truncs, [r[4] for r in results]

    def close(self):
        list(self._pool.map(lambda e: e.close(), self.envs))
        self._pool.shutdown()

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()


def make_gym_vector_env(make_spec: Callable[[], EnvSpec], n: int, asynchronous: bool = True) -> Any:
    """gymnasium.vector env of ``n`` RobotWin environments (each worker process starts its own server).

    ``make_spec`` must be a picklable top-level function when ``asynchronous`` is true.
    """
    import gymnasium  # noqa: F401  (required here)
    from gymnasium.vector import AsyncVectorEnv, SyncVectorEnv

    def factory(i):
        def make():
            spec = make_spec()
            spec.seed = spec.seed + 1000 * i
            return RobotWinEnv(spec)
        return make

    cls = AsyncVectorEnv if asynchronous else SyncVectorEnv
    return cls([factory(i) for i in range(n)])
