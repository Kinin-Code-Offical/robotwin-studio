import os
import struct
import time
from pathlib import Path

MAGIC = b"RPIM"
VERSION = 1
HEADER_SIZE = 64
HEADER_STRUCT = struct.Struct("<4sHHIIIIQQI20s")


class SharedMemoryChannel:
    def __init__(self, path: Path, payload_size: int, width: int = 0, height: int = 0, stride: int = 0):
        self.path = Path(path)
        self.payload_size = int(payload_size)
        self.width = int(width)
        self.height = int(height)
        self.stride = int(stride)
        self.sequence = 0
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._ensure_file()
        self._fh = open(self.path, "r+b", buffering=0)

    def _ensure_file(self) -> None:
        total = HEADER_SIZE + self.payload_size
        if self.path.exists():
            size = self.path.stat().st_size
            if size >= total:
                return
        with open(self.path, "wb") as handle:
            handle.truncate(total)

    def close(self) -> None:
        if self._fh:
            self._fh.close()
            self._fh = None

    def write(self, payload: bytes, *, width: int | None = None, height: int | None = None,
              stride: int | None = None, flags: int = 0, sequence: int | None = None) -> int:
        if payload is None:
            payload = b""
        if len(payload) > self.payload_size:
            payload = payload[: self.payload_size]
        if sequence is None:
            self.sequence += 1
            sequence = self.sequence
        else:
            self.sequence = sequence
        now_us = int(time.time() * 1_000_000)
        header = HEADER_STRUCT.pack(
            MAGIC,
            VERSION,
            HEADER_SIZE,
            int(width if width is not None else self.width),
            int(height if height is not None else self.height),
            int(stride if stride is not None else self.stride),
            int(self.payload_size),
            int(sequence),
            now_us,
            int(flags),
            b"\x00" * 20,
        )
        self._fh.seek(0)
        self._fh.write(header)
        if self.payload_size > 0:
            self._fh.write(payload.ljust(self.payload_size, b"\x00"))
        self._fh.flush()
        return sequence

    def read(self) -> tuple[dict, bytes]:
        self._fh.seek(0)
        header_bytes = self._fh.read(HEADER_SIZE)
        if len(header_bytes) != HEADER_SIZE:
            raise RuntimeError("Incomplete header read")
        magic, version, header_size, width, height, stride, payload_size, sequence, ts, flags, _ = HEADER_STRUCT.unpack(header_bytes)
        payload = b""
        if payload_size:
            payload = self._fh.read(payload_size)
        return (
            {
                "magic": magic,
                "version": version,
                "header_size": header_size,
                "width": width,
                "height": height,
                "stride": stride,
                "payload_size": payload_size,
                "sequence": sequence,
                "timestamp_us": ts,
                "flags": flags,
            },
            payload,
        )

    def read_if_new(self, last_sequence: int) -> tuple[dict | None, bytes | None]:
        header, payload = self.read()
        if header["magic"] != MAGIC:
            return None, None
        if header["sequence"] <= last_sequence:
            return None, None
        return header, payload


def create_channel(path: Path, payload_size: int, width: int = 0, height: int = 0, stride: int = 0) -> SharedMemoryChannel:
    return SharedMemoryChannel(path, payload_size, width, height, stride)
