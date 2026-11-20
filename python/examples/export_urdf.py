"""Export a world's robot (or a vehicle) as URDF for RViz / robot_state_publisher.

Dünyadaki robotu URDF olarak dışa aktar.

    python examples/export_urdf.py [--world my.rwworld | --example car] [--root O1] [--out robot.urdf]
"""

import argparse
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from robotwin import launch  # noqa: E402
from robotwin.urdf import world_to_urdf  # noqa: E402


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--world")
    ap.add_argument("--example", default="car")
    ap.add_argument("--root")
    ap.add_argument("--out", default="robot.urdf")
    args = ap.parse_args()
    with launch(world=args.world, example=None if args.world else args.example) as server:
        sim = server.client()
        urdf = world_to_urdf(sim.describe(), root=args.root)
    with open(args.out, "w", encoding="utf-8") as f:
        f.write(urdf)
    print(f"wrote {args.out} ({urdf.count('<link ')} links, {urdf.count('<joint ')} joints)")


if __name__ == "__main__":
    main()
