import subprocess
import sys
import time
from pathlib import Path

from rpi_shm import create_channel


def main() -> int:
    repo_root = Path(__file__).resolve().parents[2]
    shm_dir = repo_root / "logs" / "rpi" / "smoke_shm"
    log_path = repo_root / "logs" / "rpi" / "rpi_smoke.log"
    shm_dir.mkdir(parents=True, exist_ok=True)
    log_path.parent.mkdir(parents=True, exist_ok=True)

    host_cmd = [
        sys.executable,
        str(repo_root / "tools" / "rpi" / "rpi_host.py"),
        "--mock",
        "--shm-dir",
        str(shm_dir),
        "--display-width",
        "96",
        "--display-height",
        "64",
        "--camera-width",
        "96",
        "--camera-height",
        "64",
        "--log",
        str(log_path),
    ]

    proc = subprocess.Popen(host_cmd, cwd=str(repo_root))
    time.sleep(0.4)

    display = create_channel(shm_dir / "rpi_display.shm", 96 * 64 * 4, 96, 64, 96 * 4)
    camera = create_channel(shm_dir / "rpi_camera.shm", 96 * 64 * 4, 96, 64, 96 * 4)
    gpio = create_channel(shm_dir / "rpi_gpio.shm", 256)
    imu = create_channel(shm_dir / "rpi_imu.shm", 64)
    time_sync = create_channel(shm_dir / "rpi_time.shm", 32)
    network = create_channel(shm_dir / "rpi_net.shm", 16)

    try:
        start = time.time()
        seen_display = False
        while time.time() - start < 3.0:
            header, _ = display.read()
            if header["sequence"] > 0:
                seen_display = True
                break
            time.sleep(0.1)
        if not seen_display:
            raise RuntimeError("Display channel did not update")

        camera.write(b"\x7f" * (96 * 64 * 4))
        gpio.write(b"\x02\x00\x00\x00" + b"\x11\x00\x00\x00\x01\x00\x00\x00" + b"\x12\x00\x00\x00\x00\x00\x00\x00")
        imu.write(b"\x00" * 64)
        time_sync.write(b"\x00" * 16)
        network.write((1).to_bytes(4, "little") + b"\x00" * 12)

        time.sleep(0.6)
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=3)
        except Exception:
            proc.kill()

    if not log_path.exists():
        raise RuntimeError("Smoke log missing")

    log_text = log_path.read_text(encoding="utf-8", errors="ignore")
    if "Camera frame" not in log_text:
        raise RuntimeError("Camera update not observed in log")
    if "GPIO update" not in log_text:
        raise RuntimeError("GPIO update not observed in log")

    print("RPI smoke test OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
