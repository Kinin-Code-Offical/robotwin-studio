"""Experiment specs and results (no server needed: the result folder is written by hand)."""

import json
import math
import tempfile
from pathlib import Path

from robotwin.experiment import Experiment, ExperimentResult, Factor, Metric


def test_spec_is_what_robotwin_reads():
    exp = Experiment(world="car.rwworld", name="Yük", duration=2, repeats=4, seed=9,
                     factors=[Factor("Plaka.mass", [0.1, 0.4]), Factor("environment.AmbientC", start=0, stop=40, steps=3)],
                     metrics=[Metric("Plaka.distance", "final", "yol")])
    spec = exp.spec()
    assert spec["name"] == "Yük" and spec["repeats"] == 4 and spec["seed"] == 9
    assert spec["factors"][0] == {"target": "Plaka.mass", "values": ["0.1", "0.4"]}
    assert spec["factors"][1] == {"target": "environment.AmbientC", "from": 0.0, "to": 40.0, "steps": 3}
    assert spec["metrics"] == [{"name": "yol", "source": "Plaka.distance", "reduce": "final"}]
    assert spec["worldFile"].endswith("car.rwworld")


def test_a_metric_is_needed():
    try:
        Experiment(world="w.rwworld").spec()
    except ValueError:
        return
    raise AssertionError("no metric should be refused")


def test_result_folder_reads_back():
    folder = Path(tempfile.mkdtemp())
    (folder / "runs.csv").write_text("combination,seed,Plaka.mass,yol,error\n1,3,0.1,1.02,\n1,4,0.1,1.01,\n2,3,0.4,,boom\n", encoding="utf-8")
    (folder / "summary.csv").write_text("combination,Plaka.mass,yol n,yol mean,yol sd,yol ci95_low,yol ci95_high\n1,0.1,2,1.015,0.007,0.95,1.08\n2,0.4,0,,,,\n", encoding="utf-8")
    (folder / "experiment.json").write_text(json.dumps({"factors": [{"target": "Plaka.mass", "values": ["0.1", "0.4"]}]}), encoding="utf-8")
    (folder / "report.md").write_text("# Yük\n", encoding="utf-8")
    r = ExperimentResult.load(folder)
    assert len(r.runs) == 3 and len(r.summary) == 2
    assert r.runs[0]["yol"] == 1.02 and r.runs[0]["seed"] == 3
    assert [x["error"] for x in r.failed] == ["boom"]
    rows = r.metric("yol")
    assert rows[0] == ((0.1,), 1.015, 0.95, 1.08)
    assert math.isnan(rows[1][1])
    assert r.report.startswith("# Yük")
