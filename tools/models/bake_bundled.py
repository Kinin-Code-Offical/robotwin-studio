"""Bakes the bundled part models (Studio/RobotWin.Studio/Models3D/Bundled/*.obj).

Steps, done once by a developer (nothing here runs in the app or the build):
  1. pip install cascadio==0.1.1 trimesh==5.2.0   (both MIT; cascadio wraps Open CASCADE)
  2. Download the STEP files listed in THIRD_PARTY_NOTICES.md from github.com/adafruit/Adafruit_CAD_Parts
     into one folder, convert each to GLB:  cascadio.step_to_glb(step, glb, 0.05, 0.5)
  3. python bake_bundled.py <folder with the .glb files> <output folder>

Each model is rotated and moved into the frame its generator in Models3D/GenMechanical.cs expects
(millimetres, z up); moving sub-parts (TT shaft, servo spline) go to their own files.
"""
import sys, os
sys.stdout.reconfigure(encoding="utf-8")
import trimesh, numpy as np
src, out = sys.argv[1], sys.argv[2]
NOTE = "# Source: Adafruit_CAD_Parts (github.com/adafruit/Adafruit_CAD_Parts), {}; MIT licence, Copyright (c) 2016 Adafruit Industries.\n# Converted from STEP for RobotWin Studio: millimetres, Z up, placed in the part's model frame.\n"

def parts(name):
    sc = trimesh.load(os.path.join(src, name + ".glb"), force="scene")
    res = []
    for node in sc.graph.nodes_geometry:
        T, gname = sc.graph[node]
        g = sc.geometry[gname]
        v = trimesh.transform_points(g.vertices, T) * 1000
        c = np.asarray(g.visual.material.baseColorFactor, float)[:3] / 255
        res.append((gname, v, np.asarray(g.faces), c))
    return res

def write(path, items, R, t, source):
    R = np.asarray(R, float); assert abs(np.linalg.det(R) - 1) < 1e-9
    lines = [NOTE.format(source)]; base = 1; tris = 0
    for v, f, c in items:
        p = v @ R.T + t
        for x in p: lines.append(f"v {x[0]:.3f} {x[1]:.3f} {x[2]:.3f} {c[0]:.3f} {c[1]:.3f} {c[2]:.3f}\n")
        for a, b, cc in f: lines.append(f"f {a+base} {b+base} {cc+base}\n")
        base += len(v); tris += len(f)
    open(path, "w", encoding="utf-8", newline="\n").write("".join(lines))
    allp = np.vstack([v @ R.T + t for v, f, c in items])
    print(os.path.basename(path), "tris", tris, "min", np.round(allp.min(0), 2), "max", np.round(allp.max(0), 2), "bytes", os.path.getsize(path))

# TT gear motor (3777): shaft along y at (x, z) = (-22.7, 11.2); gearbox front toward -x, can toward +x.
tt = parts("3777 TT Motor")
R = [[0, 0, -1], [0, 1, 0], [1, 0, 0]]; t = np.array([-22.7, 0, 11.2])
write(os.path.join(out, "tt-motor.obj"), [(v, f, c) for n, v, f, c in tt if not n.endswith("_1")], R, t, "3777 TT Motor")
write(os.path.join(out, "tt-motor-shaft.obj"), [(v, f, c) for n, v, f, c in tt if n.endswith("_1")], R, t, "3777 TT Motor")

# 130 DC motor (711): axis along x at z = 7.5, shaft tip at x = -22.75, terminals toward +x.
m = parts("711 DC Toy Motor")
R = [[0, 0, -1], [1, 0, 0], [0, -1, 0]]; t = np.array([7.25, 0, 7.5])
write(os.path.join(out, "motor-130.obj"), [(v, f, c) for n, v, f, c in m], R, t, "711 DC Toy Motor")

# Micro servo (1143, MG90S class): length along x, output shaft near -x, base on z = 0.
s = parts("1143 Micro Servo High Torque Metal Gear")
R = [[0, 1, 0], [-1, 0, 0], [0, 0, 1]]; t = np.array([-5.3, 0, 18.8])
write(os.path.join(out, "servo-micro.obj"), [(v, f, c) for n, v, f, c in s if not n.startswith("spline")], R, t, "1143 Micro Servo - High Torque Metal Gear")
write(os.path.join(out, "servo-micro-spline.obj"), [(v, f, c) for n, v, f, c in s if n.startswith("spline")], R, t, "1143 Micro Servo - High Torque Metal Gear")
