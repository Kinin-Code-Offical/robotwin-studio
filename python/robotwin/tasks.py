"""Ready-made tasks (environment specs) on the built-in examples.

All are plain top-level functions so they pickle into worker processes (gymnasium AsyncVectorEnv).

* :func:`obstacle_stop` — the two-wheel car drives toward a wall and must stop 25 cm before it (LiDAR).
* :func:`line_follow` — the car follows the black line of the oval track (five virtual line sensors).
* :func:`hover` — the micro quadcopter holds a 3D position (UAV, attitude/throttle commands).

Türkçe: Yerleşik örnekler üzerinde hazır görevler (engelde durma, çizgi izleme, havada asılı kalma).
"""

from __future__ import annotations

import math
from typing import Any, Dict

from .env import EnvSpec

__all__ = ["obstacle_stop", "line_follow", "hover", "LINE_OFFSETS", "line_position", "front_distance"]

WALL_X = 1.2
TARGET_GAP = 0.25
LINE_OFFSETS = (-0.024, -0.012, 0.0, 0.012, 0.024)


def front_distance(raw: Dict[str, Any]) -> float:
    """Nearest LiDAR return in the ±10° cone ahead (m); 3.0 when nothing is seen."""
    scan = raw.get("scan") or {}
    ranges = scan.get("ranges") or []
    if not ranges:
        return 3.0
    n = len(ranges)
    mid = ranges[n // 2 - 2:n // 2 + 3]
    vals = [r for r in mid if r is not None]
    return min(vals) if vals else 3.0


def _obstacle_reward(raw: Dict[str, Any], info: Dict[str, Any]) -> float:
    d = front_distance(raw)
    v = (raw.get("odom") or {}).get("linear_velocity", [0, 0, 0])[0]
    return -abs(d - TARGET_GAP) - 0.05 * abs(v) * (1.0 if abs(d - TARGET_GAP) < 0.05 else 0.0)


def _obstacle_done(raw: Dict[str, Any], info: Dict[str, Any]) -> bool:
    return front_distance(raw) < 0.1


def obstacle_stop(seed: int = 0, samples: int = 31) -> EnvSpec:
    """Car + wall at x = 1.2 m: observation = 7 LiDAR sectors + forward speed; action = (v, w)."""
    return EnvSpec(
        example="car",
        seed=seed,
        dt=0.05,
        max_episode_steps=200,
        load_options={"environment": {"obstacles": [{"x": WALL_X, "y": 0.0, "size_x": 0.1, "size_y": 1.2, "height": 0.25}]}},
        sensors=[
            {"type": "lidar", "name": "scan", "object": "O1", "offset": [0.08, 0, 0.03], "samples": samples,
             "angle_min": -0.6, "angle_max": 0.6, "range_max": 3.0},
            {"type": "odometry", "name": "odom", "object": "O1"},
        ],
        observations=[
            {"sensor": "scan", "field": "ranges", "size": 7, "low": 0.0, "high": 3.0},
            {"sensor": "odom", "field": "linear_velocity", "index": [0], "low": -1.0, "high": 1.0},
        ],
        actions=[{"drive": "v", "low": -0.3, "high": 0.3}, {"drive": "w", "low": -1.0, "high": 1.0}],
        reward=_obstacle_reward,
        terminate=_obstacle_done,
        randomize={"position": 0.03, "yaw_deg": 4},
    )


def line_position(raw: Dict[str, Any]) -> float:
    """Line position under the sensor bar (m, + = left), NaN when no sensor sees the line."""
    weights, total = 0.0, 0.0
    for i, off in enumerate(LINE_OFFSETS):
        r = raw.get(f"line{i}") or {}
        darkness = max(0.0, 0.85 - float(r.get("value", 0.85)))
        weights += darkness * off
        total += darkness
    return weights / total if total > 1e-6 else float("nan")


def _line_reward(raw: Dict[str, Any], info: Dict[str, Any]) -> float:
    p = line_position(raw)
    return -1.0 if math.isnan(p) else 1.0 - abs(p) / 0.024


def _line_lost(raw: Dict[str, Any], info: Dict[str, Any]) -> bool:
    return math.isnan(line_position(raw))


def _place_on_track(client) -> None:
    start = client.describe()["environment"].get("start") or [0.0, -0.45, 0.0]
    client.set_pose("O1", position=[start[0], start[1], 0.045], rpy_deg=[0, 0, math.degrees(start[2])])


def line_follow(seed: int = 0, speed: float = 0.15) -> EnvSpec:
    """Car on the oval track at constant speed; observation = 5 line sensors; action = turn rate w."""
    return EnvSpec(
        example="car",
        seed=seed,
        dt=0.02,
        max_episode_steps=1500,
        load_options={"environment": {"track": "oval"}},
        sensors=[{"type": "line", "name": f"line{i}", "object": "O1", "offset": [0.075, off, 0]} for i, off in enumerate(LINE_OFFSETS)],
        observations=[{"sensor": f"line{i}", "field": "value", "low": 0.0, "high": 1.0} for i in range(len(LINE_OFFSETS))],
        actions=[{"drive": "w", "low": -3.0, "high": 3.0}],
        drive_options={"v": speed},
        reward=_line_reward,
        terminate=_line_lost,
        on_reset=_place_on_track,
    )


HOVER_TARGET = (0.0, 0.0, 0.8)


def _hover_error(raw: Dict[str, Any]):
    p = (raw.get("odom") or {}).get("position", [0, 0, 0])
    return [p[i] - HOVER_TARGET[i] for i in range(3)]


def _hover_reward(raw: Dict[str, Any], info: Dict[str, Any]) -> float:
    e = _hover_error(raw)
    return -math.sqrt(sum(x * x for x in e))


def _hover_crashed(raw: Dict[str, Any], info: Dict[str, Any]) -> bool:
    rpy = (raw.get("odom") or {}).get("rpy", [0, 0, 0])
    return abs(rpy[0]) > 1.2 or abs(rpy[1]) > 1.2


def _arm(client) -> None:
    client.flight(mode="angle", throttle=0.0, armed=True)


def hover(seed: int = 0) -> EnvSpec:
    """Micro quadcopter in angle mode: observation = position error, velocity, attitude; action = throttle, roll, pitch, yaw."""
    return EnvSpec(
        example="quadcopter",
        seed=seed,
        dt=0.02,
        max_episode_steps=500,
        sensors=[],
        observations=[
            {"sensor": "odom", "field": "position", "low": -5, "high": 5},
            {"sensor": "odom", "field": "linear_velocity_world", "low": -5, "high": 5},
            {"sensor": "odom", "field": "rpy", "low": -3.2, "high": 3.2},
        ],
        actions=[{"flight": "throttle", "low": 0.0, "high": 1.0}, {"flight": "roll", "low": -1, "high": 1},
                 {"flight": "pitch", "low": -1, "high": 1}, {"flight": "yaw", "low": -1, "high": 1}],
        reward=_hover_reward,
        terminate=_hover_crashed,
        on_reset=_arm,
    )
