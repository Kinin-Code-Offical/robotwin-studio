"""Gymnasium-compatible reinforcement-learning environment over a RobotWin simulation.

Works with ``gymnasium`` (and Stable-Baselines3) when installed; without it the same class offers the
same ``reset`` / ``step`` / ``render`` API with small built-in ``Box`` spaces, so scripts and tests run
on a bare Python installation.

Türkçe: RobotWin simülasyonu üzerinde Gymnasium uyumlu pekiştirmeli öğrenme ortamı.
"""

from __future__ import annotations

import math
import random
from dataclasses import dataclass, field
from typing import Any, Callable, Dict, List, Optional, Sequence, Tuple

from .client import RobotWinClient, Server, decode_image, launch

try:  # optional dependencies
    import gymnasium as _gym
    from gymnasium import spaces as _spaces
except ImportError:  # pragma: no cover - exercised when gymnasium is missing
    _gym = None
    _spaces = None

try:
    import numpy as _np
except ImportError:  # pragma: no cover
    _np = None

__all__ = ["EnvSpec", "RobotWinEnv", "Box", "HAS_GYMNASIUM", "HAS_NUMPY"]

HAS_GYMNASIUM = _gym is not None
HAS_NUMPY = _np is not None


class Box:
    """Minimal stand-in for ``gymnasium.spaces.Box`` (used only when gymnasium is not installed)."""

    def __init__(self, low: Sequence[float], high: Sequence[float], seed: Optional[int] = None):
        self.low, self.high = list(low), list(high)
        self.shape = (len(self.low),)
        self.dtype = "float32"
        self._rng = random.Random(seed)

    def seed(self, seed: Optional[int] = None) -> None:
        self._rng.seed(seed)

    def sample(self):
        values = [self._rng.uniform(lo if math.isfinite(lo) else -1.0, hi if math.isfinite(hi) else 1.0)
                  for lo, hi in zip(self.low, self.high)]
        return _np.asarray(values, dtype=_np.float32) if _np is not None else values

    def contains(self, x) -> bool:
        return len(x) == len(self.low) and all(lo <= v <= hi for v, lo, hi in zip(x, self.low, self.high))

    def __repr__(self) -> str:
        return f"Box({self.low}, {self.high})"


@dataclass
class EnvSpec:
    """What the environment observes, how actions map to commands, and the task (reward/termination).

    observations: list of items, each
        ``{"sensor": name, "field": "ranges" | "position" | ..., "index": [i, ...]?, "low": x, "high": y,
           "size": n?, "scale": s?}`` (sensor fields), or ``{"pose": object_id, "field": "position"|"rpy"|...}``,
        or ``{"pin": "D2", "board": "U1"?, "field": "voltage"}``.
        Missing / null values (no LiDAR return) read as ``high``.
    actions: list of items, each one action dimension
        ``{"actuator": name, "mode": "voltage"|"duty"|"speed"|"torque"|"angle", "low": a, "high": b}``,
        ``{"drive": "v"|"w", "low", "high"}`` (differential drive), ``{"flight": "throttle"|"roll"|"pitch"|"yaw"|"target_altitude"}``,
        ``{"pin": "D5", "mode": "pwm"|"servo", "board"?}`` (duty 0..1 or angle 0..180).
    load_options go to world.load (e.g. environment={"obstacles": [...]}); drive_options to drive.differential.
    reward(obs_dict, info) -> float, terminate(obs_dict, info) -> bool; obs_dict maps sensor names (and
    "pose:<id>") to the raw readings, info carries time, step, failed, action.
    """

    example: Optional[str] = None
    world: Optional[str] = None
    seed: int = 0
    firmware: bool = True
    dt: float = 0.02
    frame_skip: int = 1
    max_episode_steps: int = 500
    sensors: List[dict] = field(default_factory=list)
    observations: List[dict] = field(default_factory=list)
    actions: List[dict] = field(default_factory=list)
    reward: Optional[Callable[[Dict[str, Any], Dict[str, Any]], float]] = None
    terminate: Optional[Callable[[Dict[str, Any], Dict[str, Any]], bool]] = None
    randomize: Optional[dict] = None
    camera: Optional[str] = None
    load_options: Dict[str, Any] = field(default_factory=dict)
    drive_options: Dict[str, Any] = field(default_factory=dict)
    on_reset: Optional[Callable[[RobotWinClient], None]] = None
    terminate_on_failure: bool = True


_BaseEnv = _gym.Env if _gym is not None else object


class RobotWinEnv(_BaseEnv):
    """One simulation = one environment (its own headless server process unless a client is passed)."""

    metadata = {"render_modes": ["rgb_array"], "render_fps": 25}

    def __init__(self, spec: EnvSpec, client: Optional[RobotWinClient] = None, render_mode: Optional[str] = None,
                 server_kwargs: Optional[dict] = None):
        self.spec_ = spec
        self.render_mode = render_mode
        self._server: Optional[Server] = None
        if client is None:
            self._server = launch(**(server_kwargs or {}))
            client = self._server.client()
        self.client = client
        self.client.load(path=spec.world, example=spec.example, seed=spec.seed, firmware=spec.firmware,
                         sensors=spec.sensors or None, **spec.load_options)
        self._sizes = [self._item_size(o) for o in spec.observations]
        self._obs_names = sorted({self._source(o) for o in spec.observations})
        self._render_name = spec.camera
        low, high = [], []
        for item, n in zip(spec.observations, self._sizes):
            low += [float(item.get("low", -math.inf))] * n
            high += [float(item.get("high", math.inf))] * n
        alow = [float(a.get("low", -1.0)) for a in spec.actions]
        ahigh = [float(a.get("high", 1.0)) for a in spec.actions]
        if _spaces is not None:
            self.observation_space = _spaces.Box(_np.asarray(low, dtype=_np.float32), _np.asarray(high, dtype=_np.float32), dtype=_np.float32)
            self.action_space = _spaces.Box(_np.asarray(alow, dtype=_np.float32), _np.asarray(ahigh, dtype=_np.float32), dtype=_np.float32)
        else:
            self.observation_space = Box(low, high)
            self.action_space = Box(alow, ahigh)
        self._episode = 0
        self._steps = 0
        self._last_raw: Dict[str, Any] = {}

    # ------------------------------------------------------------------ helpers

    @staticmethod
    def _source(item: dict) -> str:
        if "sensor" in item:
            return item["sensor"]
        if "pose" in item:
            return "pose:" + item["pose"]
        if "pin" in item:
            return "pin:" + (item["board"] + "." if item.get("board") else "") + item["pin"]
        raise ValueError(f"observation item needs sensor/pose/pin: {item}")

    @staticmethod
    def _item_size(item: dict) -> int:
        if "index" in item:
            return len(item["index"])
        return int(item.get("size", 1 if item.get("field") in (None, "range", "value", "voltage", "altitude") else 3))

    def _extract(self, raw: Dict[str, Any]) -> List[float]:
        out: List[float] = []
        for item, n in zip(self.spec_.observations, self._sizes):
            reading = raw.get(self._source(item), {})
            value = reading.get(item.get("field", "value")) if isinstance(reading, dict) else reading
            if isinstance(value, list):
                if "index" in item:
                    value = [value[i] for i in item["index"]]
                elif len(value) != n:
                    value = _resample(value, n)
            else:
                value = [value] * n if n > 1 else [value]
            hi = float(item.get("high", math.inf))
            scale = float(item.get("scale", 1.0))
            for v in value:
                if v is None or (isinstance(v, float) and not math.isfinite(v)):
                    v = hi if math.isfinite(hi) else 0.0
                if isinstance(v, bool):
                    v = 1.0 if v else 0.0
                out.append(float(v) * scale)
        return out

    def _to_obs(self, values: List[float]):
        if _np is not None:
            return _np.asarray(values, dtype=_np.float32)
        return values

    def _actions(self, action) -> List[dict]:
        acts: List[dict] = []
        drive: Dict[str, float] = {}
        flight: Dict[str, float] = {}
        values = [float(x) for x in (action.tolist() if hasattr(action, "tolist") else action)]
        if len(values) != len(self.spec_.actions):
            raise ValueError(f"expected {len(self.spec_.actions)} action values, got {len(values)}")
        for item, v in zip(self.spec_.actions, values):
            lo, hi = float(item.get("low", -1.0)), float(item.get("high", 1.0))
            v = min(hi, max(lo, v))
            if "drive" in item:
                drive[item["drive"]] = v
            elif "flight" in item:
                flight[item["flight"]] = v
            elif "pin" in item:
                a = {"pin": item["pin"], "mode": item.get("mode", "pwm"), "board": item.get("board")}
                a["angle" if a["mode"] == "servo" else "duty"] = v
                acts.append({k: x for k, x in a.items() if x is not None})
            else:
                acts.append({"actuator": item["actuator"], "mode": item.get("mode", "voltage"), "value": v,
                             **{k: item[k] for k in ("max_torque", "supply") if k in item}})
        if drive:
            opts = dict(self.spec_.drive_options)
            acts.append({"drive": {**opts, "v": drive.get("v", opts.get("v", 0.0)), "w": drive.get("w", opts.get("w", 0.0))}})
        if flight:
            acts.append({"flight": flight})
        return acts

    # ------------------------------------------------------------------ gymnasium API

    def reset(self, *, seed: Optional[int] = None, options: Optional[dict] = None):
        if _gym is not None:
            super().reset(seed=seed)
        if seed is None:
            seed = self.spec_.seed + self._episode
        self._episode += 1
        self._steps = 0
        result = self.client.reset(seed=seed, randomize=(options or {}).get("randomize", self.spec_.randomize))
        if self.spec_.on_reset is not None:
            self.spec_.on_reset(self.client)
        raw = self.client.read_sensors([n for n in self._obs_names if not n.startswith(("pose:", "pin:"))]) if self._obs_names else {}
        for name in self._obs_names:
            if name.startswith("pose:"):
                raw[name] = self.client.get_pose(name[5:])
            elif name.startswith("pin:"):
                raw[name] = self._read_pin(name)
        self._last_raw = raw
        info = {"time": result["time"], "seed": seed, "raw": raw}
        return self._to_obs(self._extract(raw)), info

    def _read_pin(self, name: str) -> dict:
        spec = name[4:]
        board, _, pin = spec.rpartition(".")
        return self.client.pin_read(pin, board=board or None)

    def step(self, action):
        acts = self._actions(action)
        r = self.client.step(dt=self.spec_.dt, steps=self.spec_.frame_skip, actions=acts, observe=self._obs_names or None)
        self._steps += 1
        raw = r.get("observation", {})
        self._last_raw = raw
        info = {"time": r["time"], "step": self._steps, "failed": r["failed"], "halted": r["halted"],
                "rtf": r["rtf"], "action": acts, "raw": raw}
        reward = float(self.spec_.reward(raw, info)) if self.spec_.reward else 0.0
        terminated = bool(self.spec_.terminate(raw, info)) if self.spec_.terminate else False
        if self.spec_.terminate_on_failure and (r["failed"] or r["halted"]):
            terminated = True
        truncated = self._steps >= self.spec_.max_episode_steps
        return self._to_obs(self._extract(raw)), reward, terminated, truncated, info

    def render(self):
        if self._render_name is None:
            return None
        return decode_image(self.client.read_sensor(self._render_name))

    def close(self):
        try:
            self.client.close()
            if self._server is not None:
                self._server.close()
        finally:
            self._server = None


def _resample(values: List[Any], n: int) -> List[Any]:
    """Down-samples a long list (a 360-beam scan) to n values, keeping the nearest return in each bin."""
    if n <= 0:
        return []
    m = len(values)
    out = []
    for i in range(n):
        chunk = [v for v in values[i * m // n:max(i * m // n + 1, (i + 1) * m // n)] if v is not None]
        out.append(min(chunk) if chunk else None)
    return out
