"""Real firmware in the loop: run the obstacle-avoider's compiled Arduino sketch, read its Serial output,
watch the HC-SR04 range and the board's pins, then take over the motors from Python.

Gerçek yazılım döngüde: derlenmiş Arduino çizimini çalıştır, Serial çıktısını oku, pinleri izle.

    python examples/firmware_serial.py
"""

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from robotwin import launch  # noqa: E402


def main() -> None:
    with launch(example="obstacle-avoider") as server:
        sim = server.client()
        info = sim.describe()
        print("boards:", [(b["refdes"], b["mcu"], b["firmware"], b["running"]) for b in info["boards"]])
        for _ in range(4):
            r = sim.step(dt=0.05, steps=10, observe=["U2", "pin:U1.D9", "odom"])
            o = r["observation"]
            print(f"t={r['time']:.1f}s  sonar={o['U2']['range']:.2f} m  D9(trig) mode={o['pin:U1.D9']['mode']}  "
                  f"pos=({o['odom']['position'][0]:+.2f}, {o['odom']['position'][1]:+.2f})  rtf={r['rtf']:.2f}")
        print("serial:", sim.serial_read())
        print("motor driver U3:", sim.part("U3")["devices"][0]["signals"])
        # Firmware off: Python becomes the controller and drives the TB6612 PWM inputs itself.
        sim.enable_firmware(False)
        sim.pin_write("D5", "pwm", duty=0.5)
        sim.pin_write("D6", "pwm", duty=0.8)
        r = sim.step(dt=0.05, steps=40, observe=["odom"])
        print("after 2 s of Python PWM (left 50 %, right 80 %): yaw =", round(r["observation"]["odom"]["rpy"][2], 2), "rad")


if __name__ == "__main__":
    main()
