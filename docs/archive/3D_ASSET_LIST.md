# 📦 3D ASSET REQUIREMENT LIST (Optimized for Simulation)

**Engineering Rule:** do NOT model every screw. In simulation, loose screws are "polygons wasted". We only model what _moves_ or _collides_.

## 1. Static Chassis (The "Body")

**File Name:** `chassis_main.fbx`

- **Composition:** Combine the acrylic plates, brass standoffs, and the battery holder into **ONE** single mesh.
- **Why?** The physics engine treats this as one rigid object.
- **Material:** Semi-transparent acrylic (Shader: Standard, Albedo Alpha: 0.8).
- **Poly Count:** Low (< 5000 tris).
- **Colliders:** Use a "Mesh Collider (Convex)" in Unity. Don't worry about screw holes.

## 2. Moving Parts (Must be separate objects)

**File Name:** `wheel_left.fbx` & `wheel_right.fbx`

- **Dimensions:** measure the real diameter (approx 65mm).
- **Pivot Point:** MUST be in the exact center of the wheel.

**File Name:** `caster_ball.fbx`

- **Type:** A simple Sphere.
- **Physics:** Zero friction material.

## 3. The Gripper Mechanism (Complex)

**File Name:** `arm_base.fbx`

- The part attached to the chassis.

**File Name:** `arm_shoulder.fbx` (The lift arm)

- **Pivot:** At the servo horn hole.

**File Name:** `gripper_finger_right.fbx` (Driver)
**File Name:** `gripper_finger_left.fbx` (Passive)

- **Pivot:** At the screw hole where it rotates.

## 4. Electronic Boards (Visual Only)

**File Name:** `arduino_shield_combo.fbx`

- Combine the Arduino Uno and L293D Shield into one block.
- **Important:** Texture it with a photo of the real board. Don't model the capacitors unless you want to show off.

---

# ⚠️ "GOD TIER" OPTIMIZATION TRICK

Instead of modeling screws:

1.  **Texture Baking:** Paint the screws onto the texture of the chassis. It looks 100% real but costs 0 performance.
2.  **Mass Overrides:** We don't need the metal density of a screw. We just weigh the real robot (e.g., 600g) and type `0.6` into the Unity Rigidbody.

**Conclusion:** You only need to model roughly **7 parts**.

1. Chassis
2. Wheel L
3. Wheel R
4. Arm Segment 1
5. Arm Segment 2
6. Claw L
7. Claw R

Everything else is decoration!
