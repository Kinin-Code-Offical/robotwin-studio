"""Imitation learning helpers: record demonstrations, load them, fit a behaviour-cloning baseline.

Demonstrations are JSON Lines files (one step per line, stdlib only); ``to_arrays`` gives numpy arrays
when numpy is installed, and :func:`save_npz` writes the common ``.npz`` layout
(observations, actions, rewards, terminals, episode_starts) used by imitation-learning libraries.

Türkçe: Taklit öğrenme — gösterimleri kaydet/yükle, basit davranış klonlama.
"""

from __future__ import annotations

import gzip
import json
import math
import time
from pathlib import Path
from typing import Any, Callable, Dict, Iterator, List, Optional, Sequence

__all__ = ["DemoRecorder", "load_demos", "to_arrays", "save_npz", "fit_linear_policy", "LinearPolicy", "record_episodes"]


def _plain(x: Any) -> Any:
    if hasattr(x, "tolist"):
        return x.tolist()
    if isinstance(x, (list, tuple)):
        return [_plain(v) for v in x]
    if isinstance(x, float) and not math.isfinite(x):
        return None
    return x


class DemoRecorder:
    """Writes (observation, action, reward, done) steps of episodes to ``path`` (.jsonl or .jsonl.gz)."""

    def __init__(self, path: str, metadata: Optional[dict] = None):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        opener = gzip.open if self.path.suffix == ".gz" else open
        self._f = opener(self.path, "wt", encoding="utf-8")
        self.episode = 0
        self.steps = 0
        header = {"type": "header", "format": "robotwin.demos", "version": 1, "created": time.time(), "metadata": metadata or {}}
        self._f.write(json.dumps(header) + "\n")

    def record(self, observation, action, reward: float = 0.0, terminated: bool = False, truncated: bool = False,
               info: Optional[dict] = None) -> None:
        row = {"type": "step", "episode": self.episode, "t": self.steps, "obs": _plain(observation), "action": _plain(action),
               "reward": float(reward), "terminated": bool(terminated), "truncated": bool(truncated)}
        if info:
            row["info"] = {k: _plain(v) for k, v in info.items() if k in ("time", "failed", "seed")}
        self._f.write(json.dumps(row, allow_nan=False) + "\n")
        self.steps += 1
        if terminated or truncated:
            self.end_episode()

    def end_episode(self) -> None:
        if self.steps:
            self.episode += 1
            self.steps = 0

    def close(self) -> None:
        self._f.close()

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()


def load_demos(path: str) -> List[Dict[str, list]]:
    """Episodes as dicts of lists: observations, actions, rewards, terminated, truncated."""
    p = Path(path)
    opener = gzip.open if p.suffix == ".gz" else open
    episodes: Dict[int, Dict[str, list]] = {}
    with opener(p, "rt", encoding="utf-8") as f:
        for line in f:
            row = json.loads(line)
            if row.get("type") != "step":
                continue
            ep = episodes.setdefault(row["episode"], {"observations": [], "actions": [], "rewards": [], "terminated": [], "truncated": []})
            ep["observations"].append(row["obs"])
            ep["actions"].append(row["action"])
            ep["rewards"].append(row["reward"])
            ep["terminated"].append(row["terminated"])
            ep["truncated"].append(row["truncated"])
    return [episodes[k] for k in sorted(episodes)]


def to_arrays(episodes: Sequence[Dict[str, list]]):
    """Concatenated numpy arrays (requires numpy): obs, actions, rewards, terminals, episode_starts."""
    import numpy as np
    obs = np.asarray([o for e in episodes for o in e["observations"]], dtype=np.float32)
    act = np.asarray([a for e in episodes for a in e["actions"]], dtype=np.float32)
    rew = np.asarray([r for e in episodes for r in e["rewards"]], dtype=np.float32)
    term = np.asarray([t for e in episodes for t in e["terminated"]], dtype=bool)
    starts = np.asarray([i == 0 for e in episodes for i in range(len(e["actions"]))], dtype=bool)
    return obs, act, rew, term, starts


def save_npz(path: str, episodes: Sequence[Dict[str, list]]) -> None:
    import numpy as np
    obs, act, rew, term, starts = to_arrays(episodes)
    np.savez_compressed(path, observations=obs, actions=act, rewards=rew, terminals=term, episode_starts=starts)


def record_episodes(env, policy: Callable[[Any], Any], path: str, episodes: int = 5, metadata: Optional[dict] = None) -> int:
    """Runs ``policy`` (an expert: scripted controller, teleoperation callback …) and records it."""
    total = 0
    with DemoRecorder(path, metadata) as rec:
        for _ in range(episodes):
            obs, info = env.reset()
            done = False
            while not done:
                action = policy(obs)
                nxt, reward, term, trunc, info = env.step(action)
                rec.record(obs, action, reward, term, trunc, info)
                obs, done = nxt, term or trunc
                total += 1
    return total


class LinearPolicy:
    """a = W·[obs, 1] — the behaviour-cloning baseline from :func:`fit_linear_policy`."""

    def __init__(self, weights: List[List[float]], low: Optional[Sequence[float]] = None, high: Optional[Sequence[float]] = None):
        self.weights = weights
        self.low, self.high = low, high

    def __call__(self, obs) -> List[float]:
        x = [float(v) for v in (obs.tolist() if hasattr(obs, "tolist") else obs)] + [1.0]
        out = [sum(w * xi for w, xi in zip(row, x)) for row in self.weights]
        if self.low is not None and self.high is not None:
            out = [min(h, max(l, v)) for v, l, h in zip(out, self.low, self.high)]
        return out


def fit_linear_policy(episodes: Sequence[Dict[str, list]], ridge: float = 1e-3) -> LinearPolicy:
    """Least-squares behaviour cloning (ridge regression), pure Python — enough for small sensor vectors."""
    xs = [[float(v) for v in o] + [1.0] for e in episodes for o in e["observations"]]
    ys = [[float(v) for v in (a if isinstance(a, list) else [a])] for e in episodes for a in e["actions"]]
    n, m = len(xs[0]), len(ys[0])
    # Normal equations (XᵀX + λI) W = XᵀY.
    a = [[sum(x[i] * x[j] for x in xs) + (ridge if i == j else 0.0) for j in range(n)] for i in range(n)]
    b = [[sum(x[i] * y[k] for x, y in zip(xs, ys)) for k in range(m)] for i in range(n)]
    w = _solve(a, b)
    return LinearPolicy([[w[i][k] for i in range(n)] for k in range(m)])


def _solve(a: List[List[float]], b: List[List[float]]) -> List[List[float]]:
    """Gaussian elimination with partial pivoting for A X = B."""
    n = len(a)
    a = [row[:] + rhs[:] for row, rhs in zip(a, b)]
    m = len(b[0])
    for c in range(n):
        p = max(range(c, n), key=lambda r: abs(a[r][c]))
        a[c], a[p] = a[p], a[c]
        piv = a[c][c] if abs(a[c][c]) > 1e-12 else 1e-12
        for j in range(c, n + m):
            a[c][j] /= piv
        for r in range(n):
            if r != c and a[r][c] != 0.0:
                f = a[r][c]
                for j in range(c, n + m):
                    a[r][j] -= f * a[c][j]
    return [row[n:] for row in a]
