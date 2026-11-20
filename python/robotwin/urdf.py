"""URDF export of a RobotWin robot (for RViz / robot_state_publisher / MoveIt previews).

Built from ``world.describe``: every object of the robot's assembly becomes a link with box / cylinder /
sphere collision geometry and a box inertia; fixed joints become ``fixed``, hinges ``revolute`` (or
``continuous`` without limits), sliders ``prismatic``; a servo horn / motor shaft becomes its own small
link on a ``continuous``/``revolute`` joint named ``<object>_<port>``. Collision shapes are the
simulator's simplified bodies (bounding boxes), not the detailed visual meshes.

Türkçe: Dünya robotunun URDF dışa aktarımı (RViz, robot_state_publisher).
"""

from __future__ import annotations

import math
import re
from typing import Dict, List, Optional, Sequence, Tuple
from xml.sax.saxutils import quoteattr

Vec = Tuple[float, float, float]
Quat = Tuple[float, float, float, float]  # x, y, z, w


def _qmul(a: Quat, b: Quat) -> Quat:
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)


def _qconj(q: Quat) -> Quat:
    return (-q[0], -q[1], -q[2], q[3])


def _rot(q: Quat, v: Vec) -> Vec:
    x, y, z = _qmul(_qmul(q, (v[0], v[1], v[2], 0.0)), _qconj(q))[:3]
    return (x, y, z)


def _rpy(q: Quat) -> Vec:
    x, y, z, w = q
    roll = math.atan2(2 * (w * x + y * z), 1 - 2 * (x * x + y * y))
    pitch = math.asin(max(-1.0, min(1.0, 2 * (w * y - z * x))))
    yaw = math.atan2(2 * (w * z + x * y), 1 - 2 * (y * y + z * z))
    return (roll, pitch, yaw)


def _name(s: str) -> str:
    return re.sub(r"[^A-Za-z0-9_]", "_", s)


def _f(v: float) -> str:
    return f"{v:.6g}"


def _origin(xyz: Vec, rpy: Vec = (0.0, 0.0, 0.0)) -> str:
    return f'<origin xyz="{_f(xyz[0])} {_f(xyz[1])} {_f(xyz[2])}" rpy="{_f(rpy[0])} {_f(rpy[1])} {_f(rpy[2])}"/>'


def _geometry(g: dict) -> Tuple[str, Vec]:
    mn, mx = g["min"], g["max"]
    size = tuple(max(1e-4, mx[i] - mn[i]) for i in range(3))
    centre = tuple((mx[i] + mn[i]) / 2 for i in range(3))
    shape = g.get("shape", "box")
    if shape == "sphere":
        return f'<sphere radius="{_f(max(size) / 2)}"/>', centre
    if shape.startswith("cylinder"):
        axis = shape[-1]
        if axis == "y":
            return f'<cylinder radius="{_f(max(size[0], size[2]) / 2)}" length="{_f(size[1])}"/>', centre
        if axis == "x":
            return f'<cylinder radius="{_f(max(size[1], size[2]) / 2)}" length="{_f(size[0])}"/>', centre
        return f'<cylinder radius="{_f(max(size[0], size[1]) / 2)}" length="{_f(size[2])}"/>', centre
    return f'<box size="{_f(size[0])} {_f(size[1])} {_f(size[2])}"/>', centre


def _cyl_rpy(shape: str) -> Vec:
    # URDF cylinders run along z: turn them onto the body's axle.
    if shape == "cylinder-y":
        return (math.pi / 2, 0.0, 0.0)
    if shape == "cylinder-x":
        return (0.0, math.pi / 2, 0.0)
    return (0.0, 0.0, 0.0)


def _link(name: str, g: Optional[dict], mass: float, colour: str = "0.7 0.7 0.72 1") -> str:
    if g is None:
        return f'  <link name={quoteattr(name)}/>\n'
    geo, c = _geometry(g)
    mn, mx = g["min"], g["max"]
    sx, sy, sz = (max(1e-4, mx[i] - mn[i]) for i in range(3))
    m = max(1e-4, mass)
    ixx, iyy, izz = m / 12 * (sy * sy + sz * sz), m / 12 * (sx * sx + sz * sz), m / 12 * (sx * sx + sy * sy)
    rpy = _cyl_rpy(g.get("shape", "box"))
    return (f'  <link name={quoteattr(name)}>\n'
            f'    <inertial>{_origin(c)}<mass value="{_f(m)}"/>'
            f'<inertia ixx="{_f(ixx)}" ixy="0" ixz="0" iyy="{_f(iyy)}" iyz="0" izz="{_f(izz)}"/></inertial>\n'
            f'    <visual>{_origin(c, rpy)}<geometry>{geo}</geometry><material name="m_{_name(name)}"><color rgba="{colour}"/></material></visual>\n'
            f'    <collision>{_origin(c, rpy)}<geometry>{geo}</geometry></collision>\n'
            f'  </link>\n')


def _relative(parent_pose: Tuple[Vec, Quat], child_pose: Tuple[Vec, Quat]) -> Tuple[Vec, Quat]:
    (pp, pq), (cp, cq) = parent_pose, child_pose
    inv = _qconj(pq)
    d = (cp[0] - pp[0], cp[1] - pp[1], cp[2] - pp[2])
    return _rot(inv, d), _qmul(inv, cq)


def joint_name(object_id: str, port: str) -> str:
    """URDF joint of an object's moving port (also the name the ROS bridge uses in /joint_states)."""
    return _name(f"{object_id}_{port}") + "_joint"


def world_to_urdf(describe: dict, root: Optional[str] = None, robot_name: Optional[str] = None,
                  root_link: Optional[str] = None) -> str:
    """URDF of the assembly containing ``root`` (default: the object with the most joint connections).

    ``root_link`` renames the root object's link (e.g. "base_link" to match the bridge's TF tree).
    """
    if describe.get("kind") == "vehicle":
        return vehicle_to_urdf(describe, robot_name)
    objects = {o["id"]: o for o in describe["objects"]}
    joints = [j for j in describe["joints"] if j.get("parent")]
    # Undirected adjacency to find the assembly.
    adj: Dict[str, List[str]] = {k: [] for k in objects}
    for j in joints:
        if j["child"] in adj and j["parent"] in adj:
            adj[j["child"]].append(j["parent"])
            adj[j["parent"]].append(j["child"])
    if root is None:
        root = max(objects, key=lambda k: (len(adj[k]), objects[k].get("geometry", {}).get("mass", 0)))
    # Breadth-first tree from the root (the world's child→parent joints may point either way).
    parent_of: Dict[str, Tuple[str, dict]] = {}
    seen, order, tree = {root}, [root], [root]
    while order:
        cur = order.pop(0)
        for j in joints:
            other = j["parent"] if j["child"] == cur else j["child"] if j["parent"] == cur else None
            if other and other not in seen and other in objects:
                seen.add(other)
                parent_of[other] = (cur, j)
                order.append(other)
                tree.append(other)

    def pose(o):
        return tuple(o.get("position", (0, 0, 0))), tuple(o.get("orientation", (0, 0, 0, 1)))

    def lname(oid: str) -> str:
        return root_link if (root_link and oid == root) else _name(oid)

    name = robot_name or _name(describe.get("title", "robotwin")) or "robotwin"
    out = [f'<?xml version="1.0"?>\n<!-- Exported by RobotWin Studio (robotwin.urdf) -->\n<robot name={quoteattr(name)}>\n']
    port_links: Dict[str, str] = {}
    for oid in tree:
        o = objects[oid]
        g = o.get("geometry")
        out.append(_link(lname(oid), g, (g or {}).get("mass", 0.01)))
        port = (g or {}).get("port")
        if port:
            ln = _name(f"{oid}_{port['name']}")
            port_links[oid] = ln
            out.append(f'  <link name="{ln}"><inertial><mass value="0.002"/><inertia ixx="1e-7" ixy="0" ixz="0" iyy="1e-7" iyz="0" izz="1e-7"/></inertial></link>\n')
            ax = port["axis"]
            kind = "continuous" if o.get("part") == "dc-motor" else "revolute"
            limit = '' if kind == "continuous" else '<limit lower="-1.5708" upper="1.5708" effort="0.2" velocity="6"/>'
            out.append(f'  <joint name="{joint_name(oid, port["name"])}" type="{kind}"><parent link="{lname(oid)}"/><child link="{ln}"/>'
                       f'{_origin(tuple(port["pivot"]))}<axis xyz="{_f(ax[0])} {_f(ax[1])} {_f(ax[2])}"/>{limit}</joint>\n')
    for child in tree[1:]:
        parent, j = parent_of[child]
        parent_link = lname(parent)
        # A joint onto a port hangs the child on the rotor link.
        if j.get("parent_port") and j["parent"] == parent and parent in port_links:
            parent_link = port_links[parent]
        xyz, q = _relative(pose(objects[parent]), pose(objects[child]))
        if parent_link != lname(parent):
            pivot = tuple(objects[parent]["geometry"]["port"]["pivot"])
            xyz = (xyz[0] - pivot[0], xyz[1] - pivot[1], xyz[2] - pivot[2])
        kind = {"fixed": "fixed", "hinge": "revolute", "slider": "prismatic"}.get(j["kind"], "fixed")
        extra = ""
        if kind != "fixed":
            ax = j.get("axis") or [0, 0, 1]
            ax = _rot(_qconj(pose(objects[parent])[1]), tuple(ax))
            lo, hi = j.get("lower"), j.get("upper")
            if kind == "revolute" and lo is None and hi is None:
                kind = "continuous"
            elif kind == "revolute":
                lo, hi = math.radians(lo or -180), math.radians(hi or 180)
            extra = f'<axis xyz="{_f(ax[0])} {_f(ax[1])} {_f(ax[2])}"/>'
            if kind in ("revolute", "prismatic"):
                extra += f'<limit lower="{_f(lo or 0)}" upper="{_f(hi or 0)}" effort="10" velocity="5"/>'
        out.append(f'  <joint name={quoteattr(_name(j["id"]))} type="{kind}"><parent link="{parent_link}"/><child link="{lname(child)}"/>'
                   f'{_origin(xyz, _rpy(q))}{extra}</joint>\n')
    out.append("</robot>\n")
    return "".join(out)


def vehicle_to_urdf(describe: dict, robot_name: Optional[str] = None) -> str:
    """Simple URDF of a Montaj-page vehicle: chassis box with wheels or propellers."""
    v = describe["vehicle"]
    name = robot_name or _name(describe.get("title", "vehicle")) or "vehicle"
    length, width = v.get("length", 0.2), v.get("width", 0.13)
    out = [f'<?xml version="1.0"?>\n<robot name={quoteattr(name)}>\n']
    chassis = {"min": [-length / 2, -width / 2, 0], "max": [length / 2, width / 2, 0.02], "shape": "box"}
    out.append(_link("base_link", chassis, v.get("mass", 0.3)))
    if v.get("type") == "quadcopter":
        arm = v.get("arm_length", 0.045)
        r = v.get("prop_diameter", 0.055) / 2
        for i, (sx, sy) in enumerate(((1, -1), (-1, -1), (-1, 1), (1, 1))):
            ln = f"prop_{i + 1}"
            geo = {"min": [-r, -r, 0], "max": [r, r, 0.002], "shape": "cylinder-z"}
            out.append(_link(ln, geo, 0.001))
            out.append(f'  <joint name="{ln}_joint" type="continuous"><parent link="base_link"/><child link="{ln}"/>'
                       f'{_origin((sx * arm, sy * arm, 0.02))}<axis xyz="0 0 1"/></joint>\n')
    else:
        r = v.get("wheel_radius", 0.033)
        for side, y in (("left", width / 2), ("right", -width / 2)):
            ln = f"wheel_{side}"
            geo = {"min": [-r, -0.013, -r], "max": [r, 0.013, r], "shape": "cylinder-y"}
            out.append(_link(ln, geo, 0.03, "0.1 0.1 0.1 1"))
            out.append(f'  <joint name="{ln}_joint" type="continuous"><parent link="base_link"/><child link="{ln}"/>'
                       f'{_origin((-0.035, y, r))}<axis xyz="0 1 0"/></joint>\n')
    out.append("</robot>\n")
    return "".join(out)
