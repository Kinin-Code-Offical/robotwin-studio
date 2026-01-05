# NativeEngine: High-Fidelity Physics & Environment

**NativeEngine** is the computational heart of RobotWin Studio. It is a bespoke, multi-threaded C++20 physics solver built specifically for robotics simulation. Unlike game physics engines (PhysX/Havok) which prioritize stability and performance over accuracy, NativeEngine prioritizes **correctness, determinism, and physical realism**.

## Core Solvers

### 1. Rigid Body Dynamics (RBD)
- **Integrator:** Symplectic Euler / RK4 (configurable per body).
- **Collision:** Continuous Collision Detection (CCD) for high-speed rotors and projectiles.
- **Friction Model:** Coulomb friction with Stribeck effect for realistic motor startup behavior.
- **Constraints:** 6-DOF mechanical linkages (hinges, sliders, ball joints) with realistic compliance and breakage thresholds.

### 2. Aerodynamics & Fluid Dynamics
- **Blade Element Theory (BET):** Simulates lift/drag for each section of a propeller blade, accounting for twist, chord, and airfoil shape.
- **Drag Equation:** Calculates drag on all bodies based on cross-sectional area and drag coefficient ( = 0.5 \rho v^2 C_d A$).
- **Wind Simulation:** Global wind vector field with gusting and turbulence models.

### 3. Thermal Solver (Thermodynamics)
- **Heat Generation:** Calculates Joule heating (=I^2R$) for all electrical components (motors, ESCs, batteries, PCBs).
- **Heat Transfer:**
  - **Conduction:** Heat flow between physically touching bodies based on thermal conductivity and contact area.
  - **Convection:** Heat loss to the air, dependent on airflow velocity (propeller wash cools motors!).
  - **Radiation:** Black-body radiation (significant for high-temp components).
- **Throttling:** Simulates CPU/MCU thermal throttling and battery voltage sag under load.

### 4. Sensor Simulation
- **IMU (Accel/Gyro/Mag):**
  - Models bias instability, random walk, and temperature dependency.
  - Simulates "g-sensitivity" in gyroscopes.
- **Lidar / Depth Cameras:**
  - GPU-accelerated raycasting via shared memory.
  - Simulates material reflectivity and ambient light interference.
- **GPS:**
  - Simulates satellite constellation visibility and multipath errors based on environment geometry.

## Architecture & Performance

### Data-Oriented Design (DOD)
NativeEngine uses an Entity Component System (ECS) architecture in C++ to maximize cache locality and SIMD usage.
- **SoA Layout:** Structure-of-Arrays for hot data (positions, velocities).
- **Job System:** Multithreaded task graph for parallelizing independent solver islands.

### Determinism
- **Fixed-Point Math:** Optional fixed-point mode for cross-platform determinism (x86 vs ARM).
- **IEEE 754 Compliance:** Strict floating-point control when using floats.
- **Regression Testing:** Automated regression suite ensures that state(t) + inputs -> state(t+1) is bit-identical across builds.

## C# Interop (P/Invoke)

NativeEngine exposes a C-ABI for CoreSim to drive the simulation.

`cpp
// Example API
extern "C" {
    void Physics_Step(float dt, ControlFrame* inputs, PhysicsFrame* outputs);
    void Physics_SetWind(Vector3 velocity);
    void Physics_CreateBody(BodyDef* def);
    float Thermal_GetTemperature(int bodyId);
}
`

## Configuration

Physics parameters are defined in physics_config.json:
- gravity: Global gravity vector.
- ir_density: kg/m^3 (varies with altitude).
- solver_iterations: Velocity/Position iterations.
- 	hermal_ambient: Ambient temperature (Celsius).

