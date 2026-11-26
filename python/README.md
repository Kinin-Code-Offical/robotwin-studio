# robotwin — RobotWin Studio için Python / Python for RobotWin Studio

Yalnızca standart kütüphane: istemci, Gymnasium uyumlu ortam, paralel ortamlar, taklit öğrenme, URDF,
rosbridge. `gymnasium`, `numpy`, `stable-baselines3` varsa kendiliğinden kullanılır; yoksa her şey yine çalışır.

Standard library only: client, Gymnasium-compatible env, vector envs, imitation learning, URDF export,
rosbridge server. `gymnasium`, `numpy` and `stable-baselines3` are used when installed and skipped otherwise.

---

## Türkçe

### Kurulum

```bash
dotnet build tools/RobotWin.Headless -c Release      # başsız simülatör (Windows / Linux / macOS)
pip install -e python                                 # ya da: PYTHONPATH=python
```

İstemci sunucuyu şu sırayla arar: `ROBOTWIN_SERVER` ortam değişkeni (exe, yerel ikili ya da `dotnet` ile
çalışan `.dll`), depodaki `builds/RobotWin.Headless/{Release,Debug}/net9.0/`, sonra `PATH`.
Uygulamanın birebir 3B geometrisini kullanmak için `ROBOTWIN_SERVER=…\RobotWinStudio.exe` verin (Windows).

### İlk adımlar

```python
from robotwin import launch

with launch(example="car", seed=1) as server:          # kendi sürecinde başsız sunucu
    sim = server.client()
    sim.add_sensor("lidar", name="scan", object="O1", offset=[0, 0, 0.05])
    sim.add_sensor("odometry", name="odom", object="O1")
    sim.drive(0.2, 0.0)                                 # 0.2 m/s ileri
    r = sim.step(dt=0.01, steps=100, observe=["scan", "odom"])
    print(r["time"], r["rtf"], r["observation"]["odom"]["position"])
    sim.reset(seed=1)                                   # aynı tohum → aynı koşu
```

Çalışan bir sunucuya bağlanmak: `RobotWinClient("127.0.0.1", 5555)`.

### Pekiştirmeli öğrenme

```python
from robotwin import RobotWinEnv
from robotwin.tasks import obstacle_stop

env = RobotWinEnv(obstacle_stop())          # gymnasium.Env (kuruluysa)
obs, info = env.reset(seed=0)
obs, reward, terminated, truncated, info = env.step(env.action_space.sample())
```

`EnvSpec` ile kendi görevinizi tanımlayın: gözlemler (sensör alanları, `pose:<nesne>`, pinler),
eylemler (eyleyici modu, diferansiyel sürüş `v`/`w`, uçuş çubukları, pin PWM/servo), ödül ve bitiş
fonksiyonları, `randomize`, `camera` (render için). Hazır görevler: `tasks.obstacle_stop`,
`tasks.line_follow`, `tasks.hover`.

Paralel: `VectorEnv(spec, n)` (her ortam ayrı sunucu süreci) ya da gymnasium kuruluysa
`vector.make_gym_vector_env(make_spec, n)`.

### Taklit öğrenme

`imitation.record_episodes(env, uzman_politika, "demos/x.jsonl")` gösterimleri JSON Lines olarak kaydeder;
`load_demos`, `to_arrays`/`save_npz` (numpy), `fit_linear_policy` (saf Python davranış klonlama).

### Deneyler (parametre taraması)

```python
from robotwin.experiment import Experiment, Factor, Metric

exp = Experiment(world="araba.rwworld", duration=2, repeats=5,
                 factors=[Factor("environment.AmbientC", [0, 25, 40])],
                 metrics=[Metric("Plaka.distance", "final", "yol")])
sonuc = exp.run()          # RobotWin.Headless --experiment, uygulamanın kendi çözücüleri
kosular, ozet = sonuc.to_pandas()
```

Her birleşim kendi tohumlarıyla çalışır; `sonuc.metric("yol")` ortalamayı ve %95 güven aralığını verir. Uygulamanın **Deney** penceresinde kaydedilen bir dosya `run_spec("deney.json")` ile çalışır. Jupyter örneği: `examples/experiment.ipynb`.

### Örnekler (`python/examples`)

| Dosya | Ne gösterir |
|---|---|
| `random_agent.py` | En küçük RL döngüsü (bağımlılıksız) |
| `pid_obstacle.py` | LiDAR + PID ile duvardan 25 cm önce durma |
| `pid_line_follower.py` | Oval pistte 5 sanal çizgi sensörüyle PID çizgi izleme |
| `quadcopter_waypoints.py` | İHA: kurma, irtifa tutma, konum PID'iyle rota |
| `imitation_bc.py` | PID uzmanından gösterim kaydı + davranış klonlama |
| `train_ppo.py` | Stable-Baselines3 PPO (yalnızca kuruluysa çalışır) |
| `parallel_envs.py` | N paralel simülatörün verimi |
| `firmware_serial.py` | Gerçek Arduino yazılımı, seri çıktı, pinlerle devralma |
| `export_urdf.py` | Robotun URDF'si |

### Testler

```bash
python -m unittest discover -s python/tests -v
```

---

## English

### Install

```bash
dotnet build tools/RobotWin.Headless -c Release
pip install -e python            # or PYTHONPATH=python
```

Server lookup: `$ROBOTWIN_SERVER` (an .exe, a native binary, or a .dll run through `dotnet`), the repo's
`builds/RobotWin.Headless/{Release,Debug}/net9.0/`, then `PATH`. Point `ROBOTWIN_SERVER` at
`RobotWinStudio.exe` (Windows) to use the app's exact 3D geometry.

### Client API (`robotwin.RobotWinClient`)

| Area | Methods |
|---|---|
| Session | `call(method, **params)`, `batch([(method, params), ...])`, `close()`, `shutdown()` |
| World | `load(path=, example=, seed=, firmware=, sensors=, **options)`, `reset(seed=, randomize=, observe=)`, `describe()`, `step(dt, steps, actions, observe, pace, stop_on_failure, events)`, `status()`, `events()` |
| Objects | `objects()`, `get_pose(id)`, `set_pose(id, position, orientation, rpy_deg)` |
| Sensors | `sensors()`, `read_sensor(name)`, `read_sensors(names)`, `add_sensor(type, name, object, offset, rpy_deg, **params)`, `remove_sensor(name)`, `camera(name)` → image |
| Actuators | `actuators()`, `set_actuator(name, mode, value, **kw)`, `release(name=None)`, `pin_write(pin, mode, board, **kw)`, `pin_read`, `servo_write(pin, angle)`, `drive(v, w)`, `stop()` |
| UAV | `arm(armed)`, `flight(throttle=, roll=, pitch=, yaw=, mode=, target_altitude=)`, `flight_status()`, `motors([d1..d4])` |
| Board / circuit | `serial_read(board)`, `serial_write(text)`, `set_firmware(board, hex/path)`, `enable_firmware(bool)`, `voltage(node)`, `part(ref)`, `signals(prefix)` |

Helpers: `launch(world=, example=, seed=, firmware=)` → `Server` (own process, exits with Python),
`decode_image(reading)` (numpy array if available), `save_png` / `save_ppm` (stdlib).

Every method and parameter is documented in [`docs/API.md`](../docs/API.md).

### Reinforcement learning

`RobotWinEnv(EnvSpec(...))` is a `gymnasium.Env` when gymnasium is installed (otherwise the same API with a
tiny built-in `Box`). The spec declares:

* `observations`: `{"sensor": name, "field": "ranges", "size": 7, "low": 0, "high": 3}` (long lists are
  down-sampled keeping the nearest return per bin; null → `high`), `{"pose": "O1", "field": "position"}`,
  `{"pin": "D2", "field": "voltage"}`, `"index": [..]` to pick components, `"scale"`.
* `actions`: `{"actuator": "O2", "mode": "voltage", "low": -6, "high": 6}`, `{"drive": "v"|"w"}`,
  `{"flight": "throttle"|"roll"|"pitch"|"yaw"}`, `{"pin": "D5", "mode": "pwm"|"servo"}`.
* `reward(raw, info)`, `terminate(raw, info)`, `max_episode_steps`, `dt`, `frame_skip`, `randomize`,
  `on_reset(client)`, `camera` (→ `render()` RGB array), `load_options` (e.g. `environment={"obstacles": [...]}`),
  `drive_options`.

`reset(seed=None)` uses `spec.seed + episode` so runs are reproducible; failures (burned parts, crashes)
terminate an episode unless `terminate_on_failure=False`.

Parallel training: `VectorEnv(spec, n)` (threads on our side, one server process per env) or
`make_gym_vector_env(make_spec, n)` / SB3 `SubprocVecEnv` (see `examples/train_ppo.py`).

### Experiments (parameter sweeps)

```python
from robotwin.experiment import Experiment, Factor, Metric

exp = Experiment(world="car.rwworld", duration=2, repeats=5,
                 factors=[Factor("environment.AmbientC", [0, 25, 40])],
                 metrics=[Metric("Plaka.distance", "final", "distance")])
result = exp.run()        # RobotWin.Headless --experiment, the app's own solvers
runs, summary = result.to_pandas()
```

Every combination runs with its own seeds; `result.metric("distance")` gives the mean and its 95 % confidence interval. A file saved from the app's **Experiment** window runs with `run_spec("experiment.json")`. Jupyter example: `examples/experiment.ipynb`.

### Imitation learning

```python
from robotwin.imitation import record_episodes, load_demos, fit_linear_policy, save_npz
record_episodes(env, expert_policy, "demos/run.jsonl", episodes=10)
demos = load_demos("demos/run.jsonl")
save_npz("demos/run.npz", demos)          # observations, actions, rewards, terminals, episode_starts
policy = fit_linear_policy(demos)         # pure-Python behaviour-cloning baseline
```

### ROS without ROS

`python -m robotwin.rosbridge --example car --port 9090` serves the bridge topics over a rosbridge-v2
compatible WebSocket (roslibjs, web dashboards). The ROS 2 node lives in `ros2/robotwin_bridge`.

### Tests

```bash
python -m unittest discover -s python/tests -v
```
