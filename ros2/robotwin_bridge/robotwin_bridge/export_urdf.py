"""Writes the URDF of a RobotWin world robot / vehicle (root link = base_link).

    ros2 run robotwin_bridge export_urdf --example car --out car.urdf
"""

import argparse
import sys

from .bridge_node import _import_robotwin


def urdf_for(example=None, world=None, base=None, base_frame="base_link"):
    _, launch, BridgeConfig, SimBridge = _import_robotwin()
    with launch(world=world or None, example=None if world else (example or "car")) as server:
        client = server.client()
        try:
            bridge = SimBridge(client, BridgeConfig(base=base, base_frame=base_frame, scan=None, imu=False, gps=False, realtime=False))
            return bridge.urdf()
        finally:
            client.close()


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--example", default="car")
    ap.add_argument("--world")
    ap.add_argument("--base")
    ap.add_argument("--base-frame", default="base_link")
    ap.add_argument("--out", default="-")
    args = ap.parse_args(argv)
    text = urdf_for(args.example, args.world, args.base, args.base_frame)
    if args.out == "-":
        sys.stdout.write(text)
    else:
        with open(args.out, "w", encoding="utf-8") as f:
            f.write(text)


if __name__ == "__main__":
    main()
