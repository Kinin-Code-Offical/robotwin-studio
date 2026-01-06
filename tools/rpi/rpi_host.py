import argparse
import os
import struct
import subprocess
import sys
import time
from pathlib import Path

from rpi_shm import create_channel, MAGIC


def log_line(handle, text: str) -> None:
    stamp = time.strftime("%Y-%m-%d %H:%M:%S")
    handle.write(f"[{stamp}] {text}\n")
    handle.flush()


def build_pattern(width: int, height: int, tick: int) -> bytes:
    buf = bytearray(width * height * 4)
    for y in range(height):
        row_offset = y * width * 4
        for x in range(width):
            idx = row_offset + x * 4
            r = (x + tick) % 256
            g = (y + tick * 2) % 256
            b = (x + y + tick * 3) % 256
            buf[idx] = r
            buf[idx + 1] = g
            buf[idx + 2] = b
            buf[idx + 3] = 255
    return bytes(buf)


def parse_gpio(payload: bytes) -> list[tuple[int, int]]:
    if len(payload) < 4:
        return []
    count = struct.unpack_from("<I", payload, 0)[0]
    entries = []
    offset = 4
    for _ in range(min(count, 32)):
        if offset + 8 > len(payload):
            break
        pin, value = struct.unpack_from("<Ii", payload, offset)
        entries.append((pin, value))
        offset += 8
    return entries


def parse_imu(payload: bytes) -> dict:
    if len(payload) < 36:
        return {}
    values = struct.unpack_from("<9f", payload, 0)
    return {
        "ax": values[0],
        "ay": values[1],
        "az": values[2],
        "gx": values[3],
        "gy": values[4],
        "gz": values[5],
        "mx": values[6],
        "my": values[7],
        "mz": values[8],
    }


def parse_time(payload: bytes) -> tuple[float, int] | None:
    if len(payload) < 16:
        return None
    sim_time, utc_ticks = struct.unpack_from("<dq", payload, 0)
    return sim_time, utc_ticks


def parse_network(payload: bytes) -> int:
    if len(payload) < 4:
        return 0
    return struct.unpack_from("<I", payload, 0)[0]


def main() -> int:
    parser = argparse.ArgumentParser(description="RobotWin Raspberry Pi Host (QEMU or Mock)")
    parser.add_argument("--shm-dir", default="logs/rpi/shm", help="Shared memory directory")
    parser.add_argument("--display-width", type=int, default=320)
    parser.add_argument("--display-height", type=int, default=200)
    parser.add_argument("--camera-width", type=int, default=320)
    parser.add_argument("--camera-height", type=int, default=200)
    parser.add_argument("--gpio-count", type=int, default=32)
    parser.add_argument("--frame-rate", type=int, default=10)
    parser.add_argument("--mock", action="store_true", help="Use mock QEMU loop")
    parser.add_argument("--qemu", help="Path to qemu-system-* executable")
    parser.add_argument("--image", help="Path to QEMU image")
    parser.add_argument("--net-mode", default="nat", choices=["down", "nat", "bridge"])
    parser.add_argument("--log", default="logs/rpi/rpi_host.log")
    args = parser.parse_args()

    repo_root = Path(__file__).resolve().parents[2]
    shm_dir = Path(args.shm_dir)
    if not shm_dir.is_absolute():
        shm_dir = repo_root / shm_dir
    log_path = Path(args.log)
    if not log_path.is_absolute():
        log_path = repo_root / log_path
    log_path.parent.mkdir(parents=True, exist_ok=True)

    with open(log_path, "a", encoding="utf-8") as log_handle:
        log_line(log_handle, "RpiHost starting")
        log_line(log_handle, f"shm_dir={shm_dir}")
        log_line(log_handle, f"display={args.display_width}x{args.display_height} camera={args.camera_width}x{args.camera_height}")
        log_line(log_handle, f"net_mode={args.net_mode}")

        shm_dir.mkdir(parents=True, exist_ok=True)
        display = create_channel(
            shm_dir / "rpi_display.shm",
            args.display_width * args.display_height * 4,
            args.display_width,
            args.display_height,
            args.display_width * 4,
        )
        camera = create_channel(
            shm_dir / "rpi_camera.shm",
            args.camera_width * args.camera_height * 4,
            args.camera_width,
            args.camera_height,
            args.camera_width * 4,
        )
        gpio = create_channel(shm_dir / "rpi_gpio.shm", 256)
        imu = create_channel(shm_dir / "rpi_imu.shm", 64)
        time_sync = create_channel(shm_dir / "rpi_time.shm", 32)
        network = create_channel(shm_dir / "rpi_net.shm", 16)

        use_mock = args.mock
        qemu_proc = None
        restart_backoff = 1.0

        qemu_path = Path(args.qemu) if args.qemu else None
        if qemu_path and qemu_path.exists():
            use_mock = False
        else:
            if args.qemu:
                log_line(log_handle, f"QEMU not found: {args.qemu}; falling back to mock")
            use_mock = True

        if not use_mock and qemu_path:
            log_line(log_handle, f"Launching QEMU: {qemu_path}")
            qemu_args = [str(qemu_path)]
            if args.image:
                qemu_args += ["-drive", f"file={args.image},format=raw"]
            qemu_proc = subprocess.Popen(qemu_args, cwd=str(repo_root))
            log_line(log_handle, f"QEMU PID {qemu_proc.pid}")

        last_display = 0
        last_camera = 0
        last_gpio = 0
        last_imu = 0
        last_time = 0
        last_net = 0
        last_frame = 0.0
        tick = 0

        try:
            while True:
                now = time.time()
                if use_mock and now - last_frame >= 1.0 / max(1, args.frame_rate):
                    frame = build_pattern(args.display_width, args.display_height, tick)
                    display.write(frame)
                    tick += 1
                    last_frame = now

                header, payload = camera.read_if_new(last_camera)
                if header and header["magic"] == MAGIC:
                    last_camera = header["sequence"]
                    log_line(log_handle, f"Camera frame {last_camera} bytes={len(payload)}")

                header, payload = gpio.read_if_new(last_gpio)
                if header and header["magic"] == MAGIC:
                    last_gpio = header["sequence"]
                    entries = parse_gpio(payload)
                    if entries:
                        log_line(log_handle, f"GPIO update {entries}")

                header, payload = imu.read_if_new(last_imu)
                if header and header["magic"] == MAGIC:
                    last_imu = header["sequence"]
                    imu_data = parse_imu(payload)
                    if imu_data:
                        log_line(log_handle, f"IMU update {imu_data}")

                header, payload = time_sync.read_if_new(last_time)
                if header and header["magic"] == MAGIC:
                    last_time = header["sequence"]
                    time_data = parse_time(payload)
                    if time_data:
                        sim_time, utc_ticks = time_data
                        log_line(log_handle, f"Time sync sim={sim_time:.3f}s utc_ticks={utc_ticks}")

                header, payload = network.read_if_new(last_net)
                if header and header["magic"] == MAGIC:
                    last_net = header["sequence"]
                    net_mode = parse_network(payload)
                    log_line(log_handle, f"Network mode {net_mode}")

                if qemu_proc and qemu_proc.poll() is not None:
                    log_line(log_handle, f"QEMU exited with code {qemu_proc.returncode}")
                    time.sleep(restart_backoff)
                    restart_backoff = min(restart_backoff * 2.0, 5.0)
                    qemu_proc = subprocess.Popen([str(qemu_path)], cwd=str(repo_root))
                    log_line(log_handle, f"QEMU restarted pid={qemu_proc.pid}")

                time.sleep(0.02)
        except KeyboardInterrupt:
            log_line(log_handle, "Shutdown requested")
        finally:
            if qemu_proc and qemu_proc.poll() is None:
                qemu_proc.terminate()
                try:
                    qemu_proc.wait(timeout=3)
                except Exception:
                    qemu_proc.kill()
            display.close()
            camera.close()
            gpio.close()
            imu.close()
            time_sync.close()
            network.close()
            log_line(log_handle, "RpiHost stopped")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
