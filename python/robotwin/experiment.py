"""Designed experiments: parameter sweeps with seeded repeats, run headless, read back as tables.

Türkçe: Deneyler — parametre taraması, tohumlu tekrarlar, başsız çalıştırma ve sonuç tabloları.

    from robotwin.experiment import Experiment, Factor, Metric

    exp = Experiment(world="car.rwworld", duration=2, repeats=5,
                     factors=[Factor("environment.AmbientC", [0, 25, 40])],
                     metrics=[Metric("Plaka.distance", "final", "yol")])
    result = exp.run()
    runs, summary = result.to_pandas()

The run is RobotWin Studio's own (``RobotWin.Headless --experiment``): the same circuit, physics and sensor solvers
as the app, every combination on a fresh copy of the world, each repeat with its own seed. The folder it writes
(runs.csv, summary.csv, report.md, experiment.json) is what :class:`ExperimentResult` reads.
"""

from __future__ import annotations

import csv
import json
import os
import subprocess
import tempfile
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable, Dict, List, Optional, Sequence, Tuple, Union

from .client import find_server

__all__ = ["Factor", "Metric", "Experiment", "ExperimentResult", "run_spec"]


@dataclass
class Factor:
    """What to vary: ``environment.AmbientC``, ``<object>.<parameter>``, ``<object>.x|y|z|yaw``, ``<object>.mass``.

    Give the levels as ``values``, or as a range ``start``..``stop`` in ``steps``.
    """

    target: str
    values: Sequence = ()
    start: Optional[float] = None
    stop: Optional[float] = None
    steps: int = 3

    def to_json(self) -> dict:
        d: Dict[str, object] = {"target": self.target}
        if self.values:
            d["values"] = [str(v) for v in self.values]
        else:
            if self.start is None or self.stop is None:
                raise ValueError(f"factor {self.target!r} needs values or start/stop")
            d.update({"from": float(self.start), "to": float(self.stop), "steps": int(self.steps)})
        return d


@dataclass
class Metric:
    """What to measure: ``<object>.x|y|z|speed|distance|roll|pitch|yaw|temperature`` or ``node:<net or part.pin>``.

    ``reduce`` turns a run's samples into one number: final, mean, min, max, rms, range.
    """

    source: str
    reduce: str = "final"
    name: str = ""

    def to_json(self) -> dict:
        return {"name": self.name, "source": self.source, "reduce": self.reduce}


@dataclass
class Experiment:
    world: Union[str, Path]
    factors: List[Factor] = field(default_factory=list)
    metrics: List[Metric] = field(default_factory=list)
    name: str = "Deney"
    duration: float = 5.0
    sample_interval: float = 0.01
    repeats: int = 3
    seed: int = 1
    tolerances: bool = False
    firmware: bool = True

    def spec(self) -> dict:
        """The experiment as RobotWin reads it (the JSON of ``--experiment``)."""
        if not self.metrics:
            raise ValueError("an experiment needs at least one metric")
        return {
            "name": self.name,
            "worldFile": str(Path(self.world).resolve()),
            "duration": float(self.duration),
            "sampleInterval": float(self.sample_interval),
            "repeats": int(self.repeats),
            "seed": int(self.seed),
            "tolerances": bool(self.tolerances),
            "firmware": bool(self.firmware),
            "factors": [f.to_json() for f in self.factors],
            "metrics": [m.to_json() for m in self.metrics],
        }

    def run(self, out: Optional[Union[str, Path]] = None, progress: Optional[Callable[[int, int], None]] = None,
            server: Optional[Sequence[str]] = None, timeout: Optional[float] = None) -> "ExperimentResult":
        """Runs it headless and returns the result; ``out`` is the folder (a new temporary one by default)."""
        folder = Path(out) if out else Path(tempfile.mkdtemp(prefix="robotwin-experiment-"))
        folder.mkdir(parents=True, exist_ok=True)
        spec_path = folder / "spec.json"
        spec_path.write_text(json.dumps(self.spec(), ensure_ascii=False, indent=2), encoding="utf-8")
        return run_spec(spec_path, out=folder, progress=progress, server=server, timeout=timeout)


def run_spec(spec: Union[str, Path], world: Optional[Union[str, Path]] = None, out: Optional[Union[str, Path]] = None,
             progress: Optional[Callable[[int, int], None]] = None, server: Optional[Sequence[str]] = None,
             timeout: Optional[float] = None) -> "ExperimentResult":
    """Runs an experiment file (as saved by the app's Experiment window) and returns its result."""
    folder = Path(out) if out else Path(tempfile.mkdtemp(prefix="robotwin-experiment-"))
    cmd = list(server or find_server()) + ["--experiment", str(Path(spec).resolve()), "--out", str(folder.resolve())]
    if world:
        cmd += ["--world", str(Path(world).resolve())]
    started = time.monotonic()
    proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8")
    assert proc.stdout is not None
    for line in proc.stdout:
        line = line.strip()
        if line.startswith("progress ") and progress:
            done, total = line[len("progress "):].split("/")
            progress(int(done), int(total))
        if timeout is not None and time.monotonic() - started > timeout:
            proc.kill()
            raise TimeoutError(f"experiment still running after {timeout} s")
    code = proc.wait()
    err = proc.stderr.read() if proc.stderr else ""
    if code == 2:
        raise RuntimeError("the experiment could not start: " + err.strip())
    return ExperimentResult.load(folder)


def _num(text: str):
    if text == "":
        return float("nan")
    try:
        return float(text)
    except ValueError:
        return text


class ExperimentResult:
    """An experiment's folder read back: every run, every combination's statistics, the report and the spec."""

    def __init__(self, folder: Path, runs: List[dict], summary: List[dict], report: str, spec: dict):
        self.folder = folder
        self.runs = runs
        self.summary = summary
        self.report = report
        self.spec = spec

    @classmethod
    def load(cls, folder: Union[str, Path]) -> "ExperimentResult":
        folder = Path(folder)

        def table(name: str) -> List[dict]:
            path = folder / name
            if not path.exists():
                return []
            with path.open(encoding="utf-8", newline="") as f:
                return [{k: _num(v) if k not in ("error",) else v for k, v in row.items()} for row in csv.DictReader(f)]

        report = (folder / "report.md").read_text(encoding="utf-8") if (folder / "report.md").exists() else ""
        spec = json.loads((folder / "experiment.json").read_text(encoding="utf-8")) if (folder / "experiment.json").exists() else {}
        return cls(folder, table("runs.csv"), table("summary.csv"), report, spec)

    @property
    def failed(self) -> List[dict]:
        return [r for r in self.runs if r.get("error")]

    def metric(self, name: str) -> List[Tuple[Tuple, float, float, float]]:
        """Per combination: (factor levels, mean, 95 % CI low, 95 % CI high) of one metric."""
        factors = [f["target"] for f in self.spec.get("factors", [])]
        rows = []
        for s in self.summary:
            levels = tuple(s[f] for f in factors)
            rows.append((levels, s[f"{name} mean"], s[f"{name} ci95_low"], s[f"{name} ci95_high"]))
        return rows

    def to_pandas(self):
        """(runs, summary) as pandas DataFrames (pandas is needed for this one call only)."""
        import pandas as pd  # optional dependency

        return pd.DataFrame(self.runs), pd.DataFrame(self.summary)

    def __repr__(self) -> str:
        return f"ExperimentResult({len(self.runs)} runs, {len(self.summary)} combinations, folder={str(self.folder)!r})"
