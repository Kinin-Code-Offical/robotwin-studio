# robotwin_bridge — RobotWin Studio ↔ ROS 2

> **Durum / Status:** Bu paket ROS 2 kurulu olmayan bir makinede yazıldı. Simülasyon tarafı
> (`robotwin.ros.SimBridge`, mesaj biçimleri, rosbridge sunucusu) Python testleriyle doğrulandı ve düğüm,
> sahte (stub) rclpy modülleriyle çalıştırıldı; **gerçek bir ROS 2 dağıtımında (Humble/Jazzy) henüz
> denenmedi.** — Written on a machine without ROS 2: the simulation side, message shapes and the rosbridge
> server are tested, and the node was exercised against stub rclpy modules, but it has **not yet been run on a
> real ROS 2 distribution**.

---

## Türkçe

### Ne yapar

| Yön | Konu | Tür |
|---|---|---|
| yayın | `/clock` | `rosgraph_msgs/Clock` (simülasyon zamanı → `use_sim_time:=true`) |
| yayın | `/tf`, `/tf_static` | `odom → base_link`, `map → rw_<nesne>` (her dünya nesnesi), `base_link → laser / camera_link → camera_optical_frame` |
| yayın | `/odom` | `nav_msgs/Odometry` (gövde çerçevesinde hız) |
| yayın | `/scan` | `sensor_msgs/LaserScan` (ışın izlemeli sanal LiDAR, varsayılan 360 ışın, 8 m) |
| yayın | `/imu`, `/fix` | `sensor_msgs/Imu`, `sensor_msgs/NavSatFix` |
| yayın | `/camera/image_raw`, `/camera/camera_info` | `rgb8`, `camera:=true` ile |
| yayın | `/joint_states` | motor milleri / servo kolları (URDF eklem adlarıyla) |
| yayın | `/range/<sensör>` | HC-SR04 parçaları (`sensor_msgs/Range`) |
| yayın | `/robot_description` | URDF (kalıcı/latched) |
| yayın | `/robotwin/events`, `/robotwin/serial`, `/robotwin/flight` | JSON metin (`std_msgs/String`) |
| abone | `/cmd_vel` | `geometry_msgs/Twist` → kara aracında diferansiyel sürüş; quadcopter'da `linear.x/y` → eğim, `linear.z` → tırmanma, `angular.z` → sapma |
| abone | `/robotwin/arm` | `std_msgs/Bool` (İHA kur/çöz) |
| abone | `/robotwin/actuator/<ad>` | `std_msgs/Float64` (motor: volt, servo: derece, sürücü: görev oranı) |
| abone | `/robotwin/command` | `std_msgs/String` JSON: `{"method": "...", "params": {...}}` — tüm API; sonuç `/robotwin/command_result` |
| servis | `/robotwin/reset` | `std_srvs/Empty` |

### Kurulum ve çalıştırma

```bash
# 1) Simülatör (bir kez)
dotnet build tools/RobotWin.Headless -c Release
pip install -e python                     # robotwin Python paketi (ya da ROBOTWIN_PYTHON=<depo>/python)

# 2) ROS 2 çalışma alanı
mkdir -p ~/rw_ws/src && ln -s $(pwd)/ros2/robotwin_bridge ~/rw_ws/src/
cd ~/rw_ws && colcon build --symlink-install && source install/setup.bash

# 3) Çalıştır
ros2 launch robotwin_bridge robotwin_bridge.launch.py example:=car rviz:=true
ros2 topic pub /cmd_vel geometry_msgs/msg/Twist "{linear: {x: 0.2}, angular: {z: 0.5}}" -r 10
ros2 launch robotwin_bridge robotwin_bridge.launch.py world:=/yol/robotum.rwworld camera:=true
ros2 launch robotwin_bridge robotwin_bridge.launch.py example:=quadcopter
ros2 topic pub --once /robotwin/arm std_msgs/msg/Bool "{data: true}"
```

Windows'ta uygulamanın birebir geometrisi için önce `RobotWinStudio.exe --api 5555 --world robot.rwworld`
çalıştırıp düğümü `host:=127.0.0.1 port:=5555` ile bağlayabilirsiniz.

---

## English

### Topics

Published: `/clock`, `/tf` (odom→base_link, map→rw_&lt;object&gt; for every world object), `/tf_static`
(base_link→laser, base_link→camera_link→camera_optical_frame), `/odom`, `/scan`, `/imu`, `/fix`,
`/camera/image_raw` + `/camera/camera_info` (`camera:=true`), `/joint_states` (URDF joint names
`<object>_<port>_joint`), `/range/<sensor>` (HC-SR04 parts), `/robot_description` (latched URDF),
`/robotwin/events`, `/robotwin/serial`, `/robotwin/flight` (JSON strings).

Subscribed: `/cmd_vel` (ground robots: differential drive; quadcopter: `linear.x` → pitch, `linear.y` → roll,
`linear.z` → climb rate in altitude hold, `angular.z` → yaw rate; `max_speed` maps m/s to full stick),
`/robotwin/arm` (Bool), `/robotwin/actuator/<name>` (Float64: motor volts, servo degrees, driver duty),
`/robotwin/command` (String JSON `{"method", "params"}` → any API method, reply on `/robotwin/command_result`).
Service: `/robotwin/reset` (`std_srvs/Empty`). Commands time out after `cmd_vel_timeout` (0.5 s sim time).

### Build and run

```bash
dotnet build tools/RobotWin.Headless -c Release   # the simulator
pip install -e python                              # the robotwin package (or set ROBOTWIN_PYTHON)
cd ~/ros2_ws/src && ln -s <repo>/ros2/robotwin_bridge . && cd .. && colcon build --symlink-install
source install/setup.bash
ros2 launch robotwin_bridge robotwin_bridge.launch.py example:=car rviz:=true
```

Launch arguments: `example` (car | line-follower | obstacle-avoider | quadcopter), `world` (.rwworld/.rwproj),
`host`/`port` (attach to a running `RobotWin.Headless --api` or `RobotWinStudio.exe --api` instead of launching
one), `seed`, `realtime` (false = as fast as possible; everything is stamped with `/clock`), `camera`, `scan`,
`rate`, `robot_state_publisher` (exports the URDF and starts robot_state_publisher), `rviz`.
All node parameters are listed in `config/bridge.yaml`.

`ros2 run robotwin_bridge export_urdf --example car --out car.urdf` writes the URDF (also
`python/examples/export_urdf.py`). Links are the simulator's simplified bodies (boxes, cylinders, spheres)
with box inertias; fixed joints → `fixed`, hinges → `revolute`/`continuous`, sliders → `prismatic`, motor
shafts / servo horns → their own `continuous`/`revolute` joints.

### Without ROS: rosbridge-compatible WebSocket

```bash
python -m robotwin.rosbridge --example car --port 9090 [--camera] [--fast]
```

Implements the rosbridge v2 operations `subscribe`, `unsubscribe`, `advertise`, `publish`,
`call_service` (`/rosapi/topics`, `/rosapi/topics_and_raw_types` with message definitions, `/robotwin/reset`,
`/robotwin/call`), so roslibjs pages can subscribe to the same topics and publish `/cmd_vel`. Images are
sent with base64 `data` and infinite ranges as `null` (rosbridge conventions). Foxglove's rosbridge
connection is expected to work through `/rosapi/topics_and_raw_types` but has not been tested.

### Notes

* Real-time pacing slows the simulation to wall-clock speed; scenes heavier than real time (an Arduino robot
  with cycle-accurate firmware ≈ 0.8×) publish with sim-time stamps slower than wall time — use `use_sim_time`.
* The LiDAR, camera, IMU, GPS and odometry are virtual sensors mounted on the robot body (`base` parameter,
  auto-detected); parts from the app (HC-SR04, encoders) are published as they are.
