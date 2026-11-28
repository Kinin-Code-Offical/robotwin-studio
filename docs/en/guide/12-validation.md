# 12. Validation: how far from theory?

Every solver in RobotWin Studio is checked against problems with a known answer: analytic solutions, published benchmark problems and datasheet values. Each row below is an automated test. The tests run on every build (CI), and a build fails when a deviation exceeds its **tolerance**.

- **Theory / reference:** the analytic formula, the published benchmark value or the manufacturer's data.
- **Simulation:** what the test measured on its last run.
- **Absolute error:** the simulation minus the reference, in the measure's own unit (— when the reference is a range or given in words).
- **Relative error:** the same difference over the reference (or the test's own measure).
- **Tolerance:** the largest error the test accepts.
- **Test, source, initial conditions and range of validity:** at the end of each section. The numbers in a row (mass, angle, Reynolds number, circuit values) are the test's initial conditions.
- **Version:** the tables come from the test suite of the main branch after RobotWin Studio 1.7.0 (28 November 2026); when a number changes, the test has changed too.

To run the tests yourself: `dotnet test RobotWinStudio.sln`. Each test prints what it measured. In the app this chapter opens from the command palette (**Validation report**) and from the reliability section of a part's static analysis.

## 1. Mechanics (rigid bodies, joints, welds)

| Measurement | Theory / reference | Simulation | Absolute error | Relative error | Tolerance |
| --- | --- | --- | --- | --- | --- |
| Hinged physical pendulum period | 0.89769 s | 0.89767 s | −0.00002 s | −0.002 % | ±0.5 % |
| Pendulum template period (28°, large-amplitude corrected) | 1.1069 s | 1.1073 s | +0.0004 s | +0.04 % | ±1 % |
| Pendulum energy conservation | 0 | −0.80 % | −0.80 % | −0.80 % | ±3 % |
| Bounce height e²·h | 0.2113 m | 0.2111 m | −0.0002 m | −0.09 % | ±5 % |
| Car acceleration under constant torque | 0.9714 m/s² | 0.9715 m/s² | +0.0001 m/s² | +0.01 % | ±5 % |
| Sliding acceleration on a slope | 2.894 m/s² | 2.894 m/s² | 0 m/s² | ≈0 % | ±5 % |
| Droop of a P-controlled arm | 1.405° | 1.405° | 0° | ≈0 % | ±1 % |
| Weight carried by a weld (0.5 kg) | 4.905 N | 4.905 N | 0 N | ≈0 % | ±2 % |
| Bending moment in a weld | 0.1962 N·m | 0.196 N·m | −0.0002 N·m | −0.1 % | ±5 % |
| Freely spinning welded group, angular momentum | conserved | −1.10 % | — | −1.10 % | ±1.5 % |
| Momentum between a wire and a block (action–reaction) | 0 | 3.1·10⁻⁵ kg·m/s | +3.1·10⁻⁵ kg·m/s | ≈3.6 % of the exchange | 5·10⁻⁵ |
| Head-on collision, total momentum (steel, 1 kg and 3 kg) | conserved | off by 2·10⁻¹⁶ kg·m/s | — | ≈0 | 4·10⁻⁹ |
| Separation / approach speed (Newton's law of restitution, e = 0.65) | 0.6500 | 0.6500 | 0 | ≈0 % | ±0.02 |
| Energy lost in the collision, (1 − e²)·½μv² | 1.9491 J | 1.9491 J | 0 J | ≈0 % | ±2 % |
| Off-centre hit: momentum and angular momentum | conserved | 3·10⁻¹⁶ (‖L‖ 0.225) | — | ≈0 | 1 % |
| Free box about its middle axis (Dzhanibekov): it flips, ‖L‖ over 6 s | conserved | −0.14 %, direction 0.02° | — | −0.14 % | ±2 %, 1° |
| The same box's rotational energy, 6 s | conserved | −0.29 % | — | −0.29 % | ±3 % |
| Mechanical energy of a body thrown in vacuum, 2 s | conserved | at most 0.087 % | — | 0.087 % | ±0.2 % |
| Energy gained in bounces (passivity) | 0 | 5·10⁻¹¹ J | +5·10⁻¹¹ J | ≈0 | 1 % of the start |
| Terminal velocity of a sphere falling in air | 8.5568 m/s | 8.5567 m/s | −0.0001 m/s | −0.001 % | ±0.5 % |
| Wind force on a box | 0.31607 N | 0.31557 N | −0.00050 N | −0.16 % | ±2 % |

### Time-step convergence

The same motion, run again with the solver's step halved each time, must approach the exact answer, and be close enough at the step the app uses (0.5 ms, `PhysicsScene.MaxSubstep`). Figures from `TimeStepConvergenceTests`:

| Measurement | 4 ms | 2 ms | 1 ms | 0.5 ms (the app) | 0.25 ms |
| --- | --- | --- | --- | --- | --- |
| Thrown body, position error after 1 s | 19.6 mm | 9.8 mm | 4.9 mm | 2.45 mm | 1.23 mm |
| Hinged pendulum's period, relative error | 3.0·10⁻⁵ | 7.4·10⁻⁶ | 1.8·10⁻⁶ | 2.2·10⁻⁷ | 1.6·10⁻⁷ |
| Box dropped to rest, height of its centre (theory 30 mm) | 29.936 mm | 29.984 mm | 29.996 mm | 29.999 mm | 29.999 mm |
| Swinging chain of five links, largest joint separation in 10 s | 0.54 mm | — | — | 0.048 mm | — |

The thrown body's error halves exactly with each halving: semi-implicit (symplectic) Euler is first order, and the error is what a·T·h/2 gives. The pendulum's period converges at nearly second order; the 1.6·10⁻⁷ at 0.25 ms is the truncation of the analytic formula it is compared with (the θ⁴ term of the large-amplitude correction). A contact acts like a stiff, heavily damped spring: under its weight the box overlaps the ground by at most 64 µm (at the coarsest step, where the spring softens with the step), and neither sinks nor floats. For small fast bodies the solver shortens the step by itself so as not to miss a collision (continuous collision detection); the thrown body is 40 cm across to stay out of that.

### Long rest and stacks

Slow creep and drift only show over time: an impulse solver that loses a little at every step walks a box down a slope it should hold and slowly tilts a stack. Figures from `LongRestTests` (wood on wood, μs 0.50, μk 0.30):

| Measurement | Theory | 0.5 ms (the app) | 2 ms |
| --- | --- | --- | --- |
| Box on a 20° slope, distance moved in 30 s | 0 | 0.000 mm | 0.000 mm |
| On 25° (the limit is 26.6°) | 0 | 0.000 mm | 0.000 mm |
| Sliding acceleration on 30° | 2.356 m/s² | 2.357 m/s² | 2.361 m/s² |
| Top of a twenty-block stack, 20 s (ten blocks at 2 ms) | 0 | 0.029 mm | 0.009 mm |
| A light block under one 100 times heavier, pressed in by | 0 | 0.16 mm | — |

Every other block of the stack is 2 mm off centre, so no interface is symmetric. Whether a contact sticks or slides is decided from the motion at the start of the step: the velocity the step's own forces add (gravity along a slope, a push) is what static friction is there to cancel. The limits of taller stacks and coarser steps are in the validity range of the [Mechanics](../physics/02-mechanics.md) chapter.

**Test, source, conditions and validity:** `PhysicsSceneTests`, `ConservationTests`, `TimeStepConvergenceTests`, `LongRestTests` and the weld / cable / wind tests. The references are analytic: a physical pendulum's period (with the large-amplitude series), Newton's law of impact, the conservation of momentum, angular momentum and energy, Euler's equations (the intermediate axis), the terminal speed under quadratic drag. Conditions: gravity 9.81 m/s², the app's step of 0.5 ms (`PhysicsScene.MaxSubstep`); masses, angles, restitution and speeds in the rows. Validity: the range of validity in [Mechanics](../physics/02-mechanics.md).

## 2. Stress analysis (FEM, quadratic T10 elements)

| Measurement | Theory | Simulation | Absolute error | Relative error | Tolerance |
| --- | --- | --- | --- | --- | --- |
| Cantilever tip deflection (Timoshenko) | 0.20156 mm | 0.19993 mm | −0.00163 mm | −0.81 % | −3 / +2 % |
| Tensile bar (steel, 100 × 10 × 10 mm, 1 kN): stress across the middle, F/A | 10.000 MPa | 10.000 MPa | 0 MPa | ≈0 % | ±2 % |
| Same, the end's stretch F·L/(E·A) | 5.0000 µm | 4.9979 µm | −0.0021 µm | −0.04 % | −3 / +1 % |
| Cantilever root stress | 60.00 MPa | 60.06 MPa | +0.06 MPa | +0.11 % | ±5 % |
| Cantilever σxx at L/4 | 45.00 MPa | 45.06 MPa | +0.06 MPa | +0.13 % | ±5 % |
| Beam element tip deflection and root stress | 0.2000 mm / 60 MPa | same | — | 0 % | ±0.01 % / ±0.1 % |
| Plate with a hole, stress concentration Kt (Peterson, finite width) | 3.133 | 2.946 | −0.187 | −6.0 % | ±8 % |
| Freely accelerating bar (inertia relief), mid stress | 250.00 kPa | 250.00 kPa | 0 kPa | ≈0 % | ±10 % |
| The part editor's static analysis: cantilever tip deflection (fixture and force picked on faces, 2.5 mm elements; the analysis's reference solution) | 0.2015 mm | 0.1995 mm | −0.0020 mm | −1.0 % | ±5 % |
| The same study, cantilever under a uniform pressure (10 kPa, aluminium) | 21.74 µm | 21.84 µm | +0.10 µm | +0.5 % | −10 / +20 % |

**Test, source, conditions and validity:** `FemValidationTests`, `PartStudyTests`, `GridConvergenceTests`. References: beam theory (Euler–Bernoulli and Timoshenko), F/A and F·L/(E·A), Peterson's stress-concentration charts for a plate with a hole of finite width, the inertial balance of an accelerating bar. Conditions: material, size and load in the rows; quadratic T10 elements. Validity: linear elastic material and small displacements; beyond yield and buckling lie outside this table.

## 3. Heat

| Measurement | Theory | Simulation | Absolute error | Relative error | Tolerance |
| --- | --- | --- | --- | --- | --- |
| 1D transient conduction (Fourier series), largest error | — | 0.0174 K / 80 K | — | 0.02 % | 0.1 K |
| Convective energy balance of a heated plate | 18.797 K | 18.798 K | +0.001 K | +0.005 % | ±2 % |

**Test, source, conditions and validity:** `ThermalValidationTests`. References: the Fourier-series solution of 1D transient conduction, the energy balance of a heated plate. Validity: the range of validity in [Heat](../physics/03-heat.md).

## 4. Aerodynamics and flight

| Measurement | Theory / reference | Simulation | Absolute error | Relative error | Tolerance |
| --- | --- | --- | --- | --- | --- |
| Rectangular wing C_L (AR 6, 4°), lifting line | 0.318 | 0.319 | +0.001 | +0.3 % | ±7 % |
| Induced drag (6°) | 0.0121 | 0.0139 | +0.0018 | +15 % | −20 / +40 % |
| NACA 0012 section, Re 3.6·10⁵ (Sheldahl and Klimas 1981, SAND80-2114, Table 2): C_l 2° / 4° / 6° / 8° | 0.220 / 0.440 / 0.660 / 0.854 | 0.220 / 0.440 / 0.660 / 0.880 | 0 / 0 / 0 / +0.026 | 0 / 0 / 0 / +3 % | ±0.03 |
| Same, C_d 0° / 4° / 8° | 0.0079 / 0.0098 / 0.0153 | 0.0080 / 0.0095 / 0.0142 | +0.0001 / −0.0003 / −0.0011 | +1 / −3 / −7 % | ±15 % |
| Same table, C_l,max at Re 8·10⁴ / 1.6·10⁵ / 3.6·10⁵ / 7·10⁵ / 10⁶ / 2·10⁶ | 0.723 / 0.853 / 0.981 / 1.075 / 1.121 / 1.217 | 0.748 / 0.855 / 0.979 / 1.081 / 1.135 / 1.242 | +0.025 / +0.002 / −0.002 / +0.006 / +0.014 / +0.025 | +3.6 / +0.3 / −0.2 / +0.6 / +1.3 / +2.1 % | ±6 % |
| Same, stall angle | 7 / 9 / 10 / 11 / 12 / 13° | 6.9 / 7.8 / 8.9 / 9.9 / 10.4 / 11.3° | −0.1° / −1.2° / −1.1° / −1.1° / −1.6° / −1.7° | −0.1 … −1.7° | −2 / +1° |
| Deep stall 45° / 60° / 90° (Re 3.6·10⁵): C_l | 1.085 / 0.875 / 0.070 | 1.080 / 0.901 / 0.000 | −0.005 / +0.026 / −0.070 | −0.005 / +0.026 / −0.07 | ±0.1 |
| Same, C_d | 1.075 / 1.470 / 1.800 | 0.982 / 1.491 / 2.010 | −0.093 / +0.021 / +0.210 | −9 / +1 / +12 % | ±15 % |
| Same table, just past the stall (11–25°, Re 1.6·10⁵ / 3.6·10⁵ / 7·10⁵ / 10⁶ / 2·10⁶; 30 points): the lift falling onto the separated flow's | e.g. Re 3.6·10⁵: 12° 0.483, 14° 0.289, 20° 0.612 | 0.491 / 0.288 / 0.610 | mean \|ΔC_l\| 0.022, largest 0.073 | mean 0.04, largest 0.12 |
| Four-bar linkage, 45° horn → surface | 20.7° | 20.69° | −0.01° | −0.05 % | ±0.5° |
| Model rocket Cd (OpenRocket class) | 0.5–0.7 | 0.64 | — | in range | 0.4–0.8 |

Past the stall the lift falls onto a line almost independent of Re (the separated flow's, ≈ 0.054 per degree); the fall takes less than a degree at Re 1.6·10⁵ and spreads over a few degrees at 10⁶. The model takes both from the table (`LeadingEdgeStall`), so a model plane stalling at low speed loses most of its lift at once, as a real one does.

**Test, source, conditions and validity:** `AeroWingTests`, `AirfoilValidationTests` and the flight tests. References: Prandtl's lifting-line theory, the NACA 0012 tables of Sheldahl and Klimas 1981 (SAND80-2114). Conditions: aspect ratio, angle of attack and Reynolds number in the rows. Validity: Re 8·10⁴–2·10⁶ and 0–90° (what the table covers); the Limits and validation part of [Aerodynamics](../physics/04-aerodynamics.md).

## 5. Flow (CFD, lattice Boltzmann)

The flow solver runs at the real Reynolds number with a regularized collision and Smagorinsky LES. On curved walls it uses the true wall position (Bouzidi, a second-order boundary). The inlet is a velocity boundary that delivers the exact flow rate; the sides are open at ambient pressure.

| Measurement | Reference | Simulation | Absolute error | Relative error | Tolerance |
| --- | --- | --- | --- | --- | --- |
| Channel flow (Poiseuille) u_max | 1.0837·10⁻³ | 1.0770·10⁻³ | −0.0067·10⁻³ | −0.6 % | ±1 % |
| Cylinder, Schäfer–Turek 2D-1 (Re 20) Cd | 5.5795 | 5.624 | +0.0445 | +0.8 % | ±2 % |
| Same, lift Cl | 0.0106 | 0.0100 | −0.0006 | −6 % | 0.005–0.016 |
| Cylinder, Schäfer–Turek 2D-2 (Re 100) Strouhal | 0.300 | 0.2999 | −0.0001 | −0.03 % | ±5 % |
| Same, peak Cd | 3.23 | 3.337 | +0.107 | +3.3 % | ±5 % |
| Same, peak Cl | 1.00 | 0.966 | −0.034 | −3.4 % | 0.8–1.1 |
| Sphere Re 100, Cd (Schiller–Naumann) | 1.092 | 1.176 | +0.084 | +7.7 % | −8 / +10 % |
| Sphere Re 20, Cd (Schiller–Naumann) | 2.610 | 2.900 | +0.290 | +11.1 % | −8 / +15 % |
| Cube face-on, Re 10⁵ (real Re, LES), Cd | ≈1.05 (Hoerner) | 0.883 | — | −15.9 % | −22 / +15 % |

Most of the sphere's remaining deviation comes from the finite domain: at low Re a sphere disturbs the flow far out. Widening the domain from 4 to 6 diameters brings it from 11.1 % to 7.8 % at Re 20 and from 11.1 % to 7.7 % at Re 100. The standard drag curve (Schiller–Naumann) itself is within ±5 % of measurements.

**As the grid gets finer:** the solver converges at second order; halving the cell size cuts the deviation to about a quarter. Measurements on finer grids than the tests use:

| Measurement | Coarse grid | Fine grid |
| --- | --- | --- |
| DFG 2D-1 Cd (20 → 40 cells per diameter) | +0.80 % | +0.20 % |
| DFG 2D-2 Strouhal / peak Cd / peak Cl (20 → 40) | −0.03 % / +3.3 % / −3.4 % | +0.27 % / +1.8 % / −1.6 % |
| Cube Re 10⁵ Cd (12 → 16 → 20 cells per side) | −15.9 % / −12.7 % | −3.9 % |

**Before and after** (this release's flow improvements):

| Measurement | Before | Now |
| --- | --- | --- |
| Sphere Re 20, Cd deviation | +30 % | +11 % |
| Cylinder Re 100 | Strouhal +21 % in a confined domain | −0.03 % on the DFG benchmark |
| Flow rate delivered by a channel inlet | 93 % | 100 % |
| Flow at Re 10⁵ | lowered to Re 1500 on the grid | real Re (τ0 − ½ ≈ 2·10⁻⁵) |

**The 3D flow analysis in the app** prints how far to trust each result: the real Reynolds number, the cell size, the blockage ratio, whether the boundary layer is resolved (y+), whether the force settled, and the **grid uncertainty (GCI)** from solving the first instant again on a second grid. When the boundary layer is thinner than a cell, skin friction is under-resolved; the pressure drag of sharp-edged bluff bodies (a box, a car body) is still valid.

**The real air in a render (over time, moving walls):**

| Measurement | Theory / reference | Simulation | Absolute error | Relative error | Tolerance |
| --- | --- | --- | --- | --- | --- |
| 40 × 30 × 20 mm block falling for 0.2 s, force of the air on it (½ρv²·Cd·A, Cd 1.05) | 2.91 mN | 2.55 mN | −0.36 mN | −12 % | ±40 % |
| Part resting on the floor in a world without wind: air flow | none | none | — | — | no flow frame at all |

While the block falls, the cell is 3.3 mm and the flow is still developing (the body accelerates). The previous release blew a 2 m/s "test speed" at an assembly standing still; now the air comes only from the wind and from the parts' own motion.

**Test, source, conditions and validity:** `CfdValidationTests`. References: Poiseuille flow, the DFG benchmark of Schäfer and Turek (2D-1, 2D-2), the Schiller–Naumann drag curve, Hoerner's cube data. Conditions: the Reynolds number in the rows, 20 cells across the cylinder (40 in the grid table), an inlet speed that gives the exact flow rate. Validity: low Mach number (incompressible flow); in thin boundary layers the skin friction is under-resolved (the y+ note).

## 6. Liquids (particle fluid)

| Measurement | Theory / reference | Simulation | Absolute error | Relative error | Tolerance |
| --- | --- | --- | --- | --- | --- |
| Force of 6 cm of still water on a side wall (ρgD²L/2) | 1.0576 N | 1.0795 N | +0.0219 N | +2.1 % | ±5 % |
| Interior density of still water | 998.2 kg/m³ | 999.8 kg/m³ | +1.6 kg/m³ | +0.16 % | ±1 % |
| Largest compression | 0 | 1.02 % | +1.02 % | — | 3 % |
| Weight of the water carried by a free container | 1.6922 N | 1.6922 N | 0 N | 0.0 % | ±3 % |
| Mass of the water in the glass when the world starts (ρV) | 192.67 g | 192.67 g | 0 g | 0 % | ±0.01 % |
| Water in the glass 0.2 s after the start: rms speed / compression | 0 / 0 | 21.9 mm/s / 1.04 % | +21.9 mm/s / +1.04 % | — | < 30 mm/s / < 2 % |
| Glass of water template: water poured into the bowl (ESP32 + SG90) | — | 97 % | — | — | > 60 % |
| Dam break, front x/a (Martin & Moyce 1952), T = 0.84 … 2.98 | 1.22 … 3.67 | 1.25 … 3.82 | — | +2.8 … +7.8 % | ±12 % |
| Hassas render, glass of water: particles / mass / largest compression | — / 192.67 g / 0 | 41 760 / 192.7 g / 0.01 % | — | — | tolerance 0.2 % |

The water's speed just after the start comes from the glass rocking slightly on its unpowered servo in this test and from a few free-surface particles chattering (position-based fluids); at a render's 0.1 ms step the compression drops to 0.01 %.

Numerical methods usually run 5–10 % ahead of the dam-break experiment, whose gate was not removed at once. Details: [Liquids](../physics/07-liquids.md).

**Test, source, conditions and validity:** `LiquidTests`. References: hydrostatic pressure and buoyancy, the dam-break experiment of Martin and Moyce 1952. Conditions: water 998.2 kg/m³, sizes in the rows. Validity: the Limits part of [Liquids](../physics/07-liquids.md).

## 7. Gas and rockets

| Measurement | Theory / reference | Simulation | Absolute error | Relative error | Tolerance |
| --- | --- | --- | --- | --- | --- |
| Air tank blow-down, 0.5 s (adiabatic) | 4.671 bar | 4.709 bar | +0.038 bar | +0.8 % | −2 / +3 % |
| Same tank, 2 s (between adiabatic and isothermal with wall heat transfer) | 1.563–2.121 bar | 1.818 bar | — | in range | must lie between |
| KNSB motor chamber pressure | 2.26 MPa (steady) | 2.58 MPa | — | +14 % (start-up transient) | −20 / +25 % |
| KNSB specific impulse | 110–140 s (literature) | 136 s | — | in range | 100–165 s |

**Test, source, conditions and validity:** `GasPhysicsTests`, `RocketIgnitionTests`, `SolidRocketFlightTests`. References: the adiabatic and isothermal discharge of an ideal gas, published amateur-rocket data for KNSB propellant (a literature range). Conditions: tank volume, pressure and nozzle in the rows. Validity: the Validation part of [Gas and rockets](../physics/05-gas-and-rockets.md).

## 8. Electrical, electromechanical, sensors

| Measurement | Theory / reference | Simulation | Absolute error | Relative error | Tolerance |
| --- | --- | --- | --- | --- | --- |
| RC charge (1 kΩ, 1 µF, 5 V), largest difference from the exponential at the app's step | 0 | 5.21 mV | +5.21 mV | 0.10 % | 10 mV |
| RL current (10 Ω, 10 mH, 10 V), the same | 0 | 1.04 mA | +1.04 mA | 0.10 % | 5 mA |
| Lossless LC tank (1 mH, 1 µF), amplitude after 100 periods | 1 V | 0.9999 V | −0.0001 V | −0.01 % | ±1 % |
| The same tank's period at 64 steps a period | 198.692 µs | 198.851 µs | +0.159 µs | +0.080 % (the trapezoidal rule's (ωh)²/12) | ±0.1 % |
| How much the period error shrinks per halved step (T/32 → T/256) | 4 times (second order) | 3.89 / 4.00 / 4.00 | — | — | 3.6–4.4 |
| Motor no-load speed (6.28 V, datasheet) | ≈209 rpm | 210 rpm | — | +0.5 % | −12 / +2 % |
| A gear motor's energy over 3 s: what the supply gave = wiring + winding + friction + gears + work on the load + stored (flywheel, the winding's field) | 8.313 J | 8.313 J (0.434 + 3.477 + 1.041 + 0.939 + 2.191 + 0.231) | — | −0.001 % | ±0.5 % |
| Same motor: winding heat = temperature rise + heat to the air | 3.477 J | 3.462 + 0.015 J | — | ≈0 | 10⁻⁶ J |
| Motor current rise on a 10 % slope | 0.059 A | 0.055 A | −0.004 A | −7 % | ±25 % |
| Current rise while pushing a box | 0.052 A | 0.054 A | +0.002 A | +4 % | ±35 % |
| Encoder edge count | 1940 | 1940 | 0 | 0 % | ±10 % |
| DC motor's inrush current, peak (6 V, 2 Ω, 1 mH, k 0.01; the winding and rotor equations solved with a fine step) | 2.7906 A | 2.7914 A | +0.0008 A | +0.03 % | ±2 % |
| Same, time to the peak | 1.90 ms | 1.90 ms | 0 ms | ≈0 | ±0.2 ms |
| MOSFET turn-on, gate-charge circuit (R_g 10 Ω, C_gs 1.3 nF, C_gd 0.2 nF, 10 A clamped): gate to threshold R_g·C_iss·ln(V/(V − V_th)) | 5.350 ns | 5.350 ns | 0 ns | ≈0 % | ±3 % |
| Same, Miller plateau C_gd·ΔV_ds·R_g/(V − V_p) | 8.625 ns | 8.714 ns | +0.089 ns | +1.0 % | ±7 % |
| Same, gate charge to 10 V | 19.965 nC | 19.965 nC | 0 nC | ≈0 % | ±2 % |
| Stepper, pull-out torque of a sudden load (energy method: 0.7245·K·I) | 0.1206 N·m | 0.1211 N·m | +0.0005 N·m | +0.4 % | −6 / +8 % |
| Same, a load raised slowly (K·I ± detent) | 0.144–0.188 N·m | 0.1854 N·m | — | in range | range |
| LM7805 load regulation, 5 mA → 1.5 A (TI datasheet: typ. 10, max. 25 mV) | 10 mV | 11.96 mV | +1.96 mV | +20 % | max. 25 mV |
| LM7805 output impedance, no output capacitor (datasheet Figure 7, read off the graph): 100 Hz / 1 kHz / 10 kHz / 100 kHz | 8 / 9 / 28 / 270 mΩ | 8.0 / 8.7 / 32.7 / 305 mΩ | 0 mΩ / −0.3 mΩ / +4.7 mΩ / +35 mΩ | 0 / −4 / +17 / +13 % | ±40 % |
| LM7805, a 0.1 → 0.6 A step (1 µs) on 0.1 µF: the output's dip, against the model's equations solved with a fine step | 244.5 mV | 243.9 mV | −0.6 mV | −0.2 % | ±5 % |
| GPS median horizontal error (datasheet CEP) | 2.5 m | 2.64 m | +0.14 m | +6 % | 1.2–4.5 m |
| Wheel mesh radius (mean / worst) | — | 0.07 % / 1.47 % | — | — | worst ≤ 3 % |

**Test, source, conditions and validity:** `CircuitAccuracyTests`, `MotorStartValidationTests`, `MosfetSwitchingValidationTests`, `StepperTests`, `RegulatorLoadValidationTests`, `EnergyBalanceTests`, `EncoderMotorWorldTests` and the sensor tests. References: the analytic solutions of RC, RL and LC circuits, a fine-stepped solution of the model's own equations, datasheets (TI LM7805, SNOSBT0L), the energy method for the stepper. Conditions: circuit values in the rows. Validity: the range of validity in [Circuit](../physics/01-circuit.md).

## For your own model

These tables show how accurate the solvers are. How close your model is to reality also depends on your mesh, supports and part data:

- **FEM:** the *mesh convergence check* re-solves the worst instant on a finer mesh and reports the change. The *.inp export* writes the same case for CalculiX, ANSYS or Abaqus.
- **CFD:** look at the grid uncertainty (GCI) and y+ in the notes of the 3D analysis.
- **Part spread:** datasheet values are typical; clone servos and motors can differ by 30–40 % in current. The render's uncertainty band draws that range.
- **Time step:** the render's *time-step check* runs the first 2 seconds again at half the physics step and reports how much the parts' paths and every recorded quantity change. Semi-implicit Euler is first order, so that difference measures the run's own step error: under 2 % the result counts as independent of the step, up to 10 % as slightly sensitive; above 10 %, render at a higher accuracy. Channels that jitter, barely move or count things (the number of contacts) are not compared: they change from run to run whatever the step.

The render puts all of these together at the top of its log, under **Numerical uncertainty**: the time step, the stress meshes, the flow grid (GCI) and the part tolerances.
