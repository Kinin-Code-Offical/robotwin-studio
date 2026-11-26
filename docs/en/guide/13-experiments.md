# 13. Experiments: parameter sweeps and confidence intervals

Running a world once gives one result. An experiment runs the same world at every combination of the settings you vary, each one several times with its own seed (sensor noise, part tolerances, `random()`), and gives every metric's mean, standard deviation and 95 % confidence interval. Experiments run without a window, in the background, with the app's own circuit, physics and sensor solvers.

## The experiment file

An experiment is a JSON file:

```json
{
  "name": "Free fall",
  "worldFile": "falling-ball.rwworld",
  "duration": 0.3,
  "sampleInterval": 0.005,
  "repeats": 5,
  "seed": 1,
  "tolerances": false,
  "firmware": true,
  "factors": [
    { "target": "environment.Gravity", "values": ["9.81", "1.62"] },
    { "target": "Ball.z", "from": 0.5, "to": 1.5, "steps": 3 }
  ],
  "metrics": [
    { "name": "top speed", "source": "Ball.speed", "reduce": "max" },
    { "name": "drop", "source": "Ball.z", "reduce": "range" }
  ]
}
```

- **duration** is each run's simulated time (s), **sampleInterval** how often the metrics are read.
- **repeats** is how many times each combination runs; the seeds are `seed`, `seed + 1`, … The same seed gives the same noise and the same tolerance draw, so an experiment repeats exactly.
- With **tolerances** on, the part values are drawn again from their tolerance bands in every run (Monte Carlo).
- With **firmware** off the boards' firmware does not run: the circuit and the mechanics only.

### Factors

| Target | What changes |
| --- | --- |
| `environment.<setting>` | The environment: `AmbientC`, `Gravity`, `Friction`, `WindX`, `WallHeight`, `AirDensity` … |
| `<object>.<parameter>` | A part's parameter, by its key: `rpm`, `fault`, `fault_value`, `model` … |
| `<object>.x`, `.y`, `.z`, `.yaw` | Where it is placed (m) and its heading (°) |
| `<object>.mass` | Its mass (kg) |

An object is named by its name or its id. The values are listed with `values`, or a range `from`–`to` in `steps`. Every value of every factor is combined with every other.

### Metrics

| Source | What is read |
| --- | --- |
| `<object>.x`, `.y`, `.z` | Its position (m) |
| `<object>.speed` | Its speed (m/s) |
| `<object>.distance` | The path it travelled through the run (m) |
| `<object>.roll`, `.pitch`, `.yaw` | Its attitude (°) |
| `<object>.temperature` | Its temperature (°C) |
| `node:<net or part.pin>` | A voltage (V) |

**reduce** turns the samples into one number: `final` (the last value), `mean`, `min`, `max`, `rms`, `range` (largest − smallest).

From the window: **Simulation → Experiment: parameter sweep…** offers the open world's factors and metrics in lists, runs the experiment in the background, shows each combination's mean and confidence interval, and writes the files under **Documents\RobotWin\Experiments** with a copy of the world as run. **Save experiment** keeps the settings as JSON; the command line runs the same file.

In a world with nothing random in it (no noisy sensor, no tolerances, no `random()`) every seed gives the same result, and the standard deviation is 0.

## Running it

```powershell
RobotWinStudio.exe --experiment experiment.json --experiment-out results
```

`--experiment-world world.rwworld` takes the place of the experiment's world file. The folder gets:

- **runs.csv:** every run with its combination, seed, factor values and metrics;
- **summary.csv:** each combination's n, mean, standard deviation and 95 % confidence interval per metric;
- **report.md:** what was run, and a table per metric;
- **experiment.json:** the experiment as run, with the world file's full path (to repeat it);
- **progress.log:** progress.

Exit code 0: every run finished; 1: some failed (their error is in `runs.csv` and the report); 2: the experiment could not start.

## Python and Jupyter

`python/robotwin/experiment.py` builds and runs the same experiment from Python (through the headless runner `RobotWin.Headless --experiment`); `to_pandas()` turns the result tables into DataFrames. `python/examples/experiment.ipynb` sweeps temperature and mass and plots them with confidence-interval bars. Details: [python/README.md](https://github.com/Kinin-Code-Offical/robotwin-studio/blob/main/python/README.md).

## Confidence interval

If a metric's mean over a combination's *n* runs is *x̄* and its sample standard deviation *s*, the 95 % confidence interval is *x̄ ± t·s/√n*, with *t* the two-sided 95 % value of Student's t with *n − 1* degrees of freedom (12.71 at *n* = 2, 2.26 at *n* = 10, 1.96 for many runs). A combination with a single run has no interval.
