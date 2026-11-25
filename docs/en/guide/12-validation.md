# 12. Validation: how far from theory?

Every solver in RobotWin Studio is checked against problems with a known answer: analytic solutions, published benchmark problems and datasheet values. Each row below is an automated test. The tests run on every build (CI), and a build fails when a deviation exceeds its **tolerance**.

- **Theory / reference:** the analytic formula, the published benchmark value or the manufacturer's data.
- **Simulation:** what the test measured on its last run.
- **Deviation:** the difference between the simulation and the reference.
- **Tolerance:** the largest error the test accepts.

To run the tests yourself: `dotnet test RobotWinStudio.sln`. Each test prints what it measured.

## 1. Mechanics (rigid bodies, joints, welds)

| Measurement | Theory / reference | Simulation | Deviation | Tolerance |
| --- | --- | --- | --- | --- |
| Hinged physical pendulum period | 0.89769 s | 0.89767 s | −0.002 % | ±0.5 % |
| Pendulum template period (28°, large-amplitude corrected) | 1.1069 s | 1.1073 s | +0.04 % | ±1 % |
| Pendulum energy conservation | 0 | −0.80 % | −0.80 % | ±3 % |
| Bounce height e²·h | 0.2113 m | 0.2111 m | −0.09 % | ±5 % |
| Car acceleration under constant torque | 0.9714 m/s² | 0.9715 m/s² | +0.01 % | ±5 % |
| Sliding acceleration on a slope | 2.894 m/s² | 2.894 m/s² | ≈0 % | ±5 % |
| Droop of a P-controlled arm | 1.405° | 1.405° | ≈0 % | ±1 % |
| Weight carried by a weld (0.5 kg) | 4.905 N | 4.905 N | ≈0 % | ±2 % |
| Bending moment in a weld | 0.1962 N·m | 0.196 N·m | −0.1 % | ±5 % |
| Freely spinning welded group, angular momentum | conserved | −1.10 % | −1.10 % | ±1.5 % |
| Momentum between a wire and a block (action–reaction) | 0 | 3.1·10⁻⁵ kg·m/s | ≈3.6 % of the exchange | 5·10⁻⁵ |
| Head-on collision, total momentum (steel, 1 kg and 3 kg) | conserved | off by 2·10⁻¹⁶ kg·m/s | ≈0 | 4·10⁻⁹ |
| Separation / approach speed (Newton's law of restitution, e = 0.65) | 0.6500 | 0.6500 | ≈0 % | ±0.02 |
| Energy lost in the collision, (1 − e²)·½μv² | 1.9491 J | 1.9491 J | ≈0 % | ±2 % |
| Off-centre hit: momentum and angular momentum | conserved | 3·10⁻¹⁶ (‖L‖ 0.225) | ≈0 | 1 % |
| Free box about its middle axis (Dzhanibekov): it flips, ‖L‖ over 6 s | conserved | −0.14 %, direction 0.02° | −0.14 % | ±2 %, 1° |
| The same box's rotational energy, 6 s | conserved | −0.29 % | −0.29 % | ±3 % |
| Mechanical energy of a body thrown in vacuum, 2 s | conserved | at most 0.087 % | 0.087 % | ±0.2 % |
| Energy gained in bounces (passivity) | 0 | 5·10⁻¹¹ J | ≈0 | 1 % of the start |
| Terminal velocity of a sphere falling in air | 8.5568 m/s | 8.5567 m/s | −0.001 % | ±0.5 % |
| Wind force on a box | 0.31607 N | 0.31557 N | −0.16 % | ±2 % |

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

## 2. Stress analysis (FEM, quadratic T10 elements)

| Measurement | Theory | Simulation | Deviation | Tolerance |
| --- | --- | --- | --- | --- |
| Cantilever tip deflection (Timoshenko) | 0.20156 mm | 0.19993 mm | −0.81 % | −3 / +2 % |
| Cantilever root stress | 60.00 MPa | 60.06 MPa | +0.11 % | ±5 % |
| Cantilever σxx at L/4 | 45.00 MPa | 45.06 MPa | +0.13 % | ±5 % |
| Beam element tip deflection and root stress | 0.2000 mm / 60 MPa | same | 0 % | ±0.01 % / ±0.1 % |
| Plate with a hole, stress concentration Kt (Peterson, finite width) | 3.133 | 2.946 | −6.0 % | ±8 % |
| Freely accelerating bar (inertia relief), mid stress | 250.00 kPa | 250.00 kPa | ≈0 % | ±10 % |

## 3. Heat

| Measurement | Theory | Simulation | Deviation | Tolerance |
| --- | --- | --- | --- | --- |
| 1D transient conduction (Fourier series), largest error | — | 0.0174 K / 80 K | 0.02 % | 0.1 K |
| Convective energy balance of a heated plate | 18.797 K | 18.798 K | +0.005 % | ±2 % |

## 4. Aerodynamics and flight

| Measurement | Theory / reference | Simulation | Deviation | Tolerance |
| --- | --- | --- | --- | --- |
| Rectangular wing C_L (AR 6, 4°), lifting line | 0.318 | 0.319 | +0.3 % | ±7 % |
| Induced drag (6°) | 0.0121 | 0.0139 | +15 % | −20 / +40 % |
| Four-bar linkage, 45° horn → surface | 20.7° | 20.69° | −0.05 % | ±0.5° |
| Model rocket Cd (OpenRocket class) | 0.5–0.7 | 0.64 | in range | 0.4–0.8 |

## 5. Flow (CFD, lattice Boltzmann)

The flow solver runs at the real Reynolds number with a regularized collision and Smagorinsky LES. On curved walls it uses the true wall position (Bouzidi, a second-order boundary). The inlet is a velocity boundary that delivers the exact flow rate; the sides are open at ambient pressure.

| Measurement | Reference | Simulation | Deviation | Tolerance |
| --- | --- | --- | --- | --- |
| Channel flow (Poiseuille) u_max | 1.0837·10⁻³ | 1.0770·10⁻³ | −0.6 % | ±1 % |
| Cylinder, Schäfer–Turek 2D-1 (Re 20) Cd | 5.5795 | 5.624 | +0.8 % | ±2 % |
| Same, lift Cl | 0.0106 | 0.0100 | −6 % | 0.005–0.016 |
| Cylinder, Schäfer–Turek 2D-2 (Re 100) Strouhal | 0.300 | 0.2999 | −0.03 % | ±5 % |
| Same, peak Cd | 3.23 | 3.337 | +3.3 % | ±5 % |
| Same, peak Cl | 1.00 | 0.966 | −3.4 % | 0.8–1.1 |
| Sphere Re 100, Cd (Schiller–Naumann) | 1.092 | 1.176 | +7.7 % | −8 / +10 % |
| Sphere Re 20, Cd (Schiller–Naumann) | 2.610 | 2.900 | +11.1 % | −8 / +15 % |
| Cube face-on, Re 10⁵ (real Re, LES), Cd | ≈1.05 (Hoerner) | 0.883 | −15.9 % | −22 / +15 % |

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

| Measurement | Theory / reference | Simulation | Deviation | Tolerance |
| --- | --- | --- | --- | --- |
| 40 × 30 × 20 mm block falling for 0.2 s, force of the air on it (½ρv²·Cd·A, Cd 1.05) | 2.91 mN | 2.55 mN | −12 % | ±40 % |
| Part resting on the floor in a world without wind: air flow | none | none | — | no flow frame at all |

While the block falls, the cell is 3.3 mm and the flow is still developing (the body accelerates). The previous release blew a 2 m/s "test speed" at an assembly standing still; now the air comes only from the wind and from the parts' own motion.

## 6. Liquids (particle fluid)

| Measurement | Theory / reference | Simulation | Deviation | Tolerance |
| --- | --- | --- | --- | --- |
| Force of 6 cm of still water on a side wall (ρgD²L/2) | 1.0576 N | 1.0795 N | +2.1 % | ±5 % |
| Interior density of still water | 998.2 kg/m³ | 999.8 kg/m³ | +0.16 % | ±1 % |
| Largest compression | 0 | 1.02 % | — | 3 % |
| Weight of the water carried by a free container | 1.6922 N | 1.6922 N | 0.0 % | ±3 % |
| Mass of the water in the glass when the world starts (ρV) | 192.67 g | 192.67 g | 0 % | ±0.01 % |
| Water in the glass 0.2 s after the start: rms speed / compression | 0 / 0 | 21.9 mm/s / 1.04 % | — | < 30 mm/s / < 2 % |
| Glass of water template: water poured into the bowl (ESP32 + SG90) | — | 97 % | — | > 60 % |
| Dam break, front x/a (Martin & Moyce 1952), T = 0.84 … 2.98 | 1.22 … 3.67 | 1.25 … 3.82 | +2.8 … +7.8 % | ±12 % |
| Hassas render, glass of water: particles / mass / largest compression | — / 192.67 g / 0 | 41 760 / 192.7 g / 0.01 % | — | tolerance 0.2 % |

The water's speed just after the start comes from the glass rocking slightly on its unpowered servo in this test and from a few free-surface particles chattering (position-based fluids); at a render's 0.1 ms step the compression drops to 0.01 %.

Numerical methods usually run 5–10 % ahead of the dam-break experiment, whose gate was not removed at once. Details: [Liquids](../physics/07-liquids.md).

## 7. Gas and rockets

| Measurement | Theory / reference | Simulation | Deviation | Tolerance |
| --- | --- | --- | --- | --- |
| Air tank blow-down, 0.5 s (adiabatic) | 4.671 bar | 4.709 bar | +0.8 % | −2 / +3 % |
| Same tank, 2 s (between adiabatic and isothermal with wall heat transfer) | 1.563–2.121 bar | 1.818 bar | in range | must lie between |
| KNSB motor chamber pressure | 2.26 MPa (steady) | 2.58 MPa | +14 % (start-up transient) | −20 / +25 % |
| KNSB specific impulse | 110–140 s (literature) | 136 s | in range | 100–165 s |

## 8. Electrical, electromechanical, sensors

| Measurement | Theory / reference | Simulation | Deviation | Tolerance |
| --- | --- | --- | --- | --- |
| RC charge (1 kΩ, 1 µF, 5 V), largest difference from the exponential at the app's step | 0 | 5.21 mV | 0.10 % | 10 mV |
| RL current (10 Ω, 10 mH, 10 V), the same | 0 | 1.04 mA | 0.10 % | 5 mA |
| Lossless LC tank (1 mH, 1 µF), amplitude after 100 periods | 1 V | 0.9999 V | −0.01 % | ±1 % |
| The same tank's period at 64 steps a period | 198.692 µs | 198.851 µs | +0.080 % (the trapezoidal rule's (ωh)²/12) | ±0.1 % |
| How much the period error shrinks per halved step (T/32 → T/256) | 4 times (second order) | 3.89 / 4.00 / 4.00 | — | 3.6–4.4 |
| Motor no-load speed (6.28 V, datasheet) | ≈209 rpm | 210 rpm | +0.5 % | −12 / +2 % |
| A gear motor's energy over 3 s: what the supply gave = wiring + winding + friction + gears + work on the load + stored (flywheel, the winding's field) | 8.313 J | 8.313 J (0.434 + 3.477 + 1.041 + 0.939 + 2.191 + 0.231) | −0.001 % | ±0.5 % |
| Same motor: winding heat = temperature rise + heat to the air | 3.477 J | 3.462 + 0.015 J | ≈0 | 10⁻⁶ J |
| Motor current rise on a 10 % slope | 0.059 A | 0.055 A | −7 % | ±25 % |
| Current rise while pushing a box | 0.052 A | 0.054 A | +4 % | ±35 % |
| Encoder edge count | 1940 | 1940 | 0 % | ±10 % |
| GPS median horizontal error (datasheet CEP) | 2.5 m | 2.64 m | +6 % | 1.2–4.5 m |
| Wheel mesh radius (mean / worst) | — | 0.07 % / 1.47 % | — | worst ≤ 3 % |

## For your own model

These tables show how accurate the solvers are. How close your model is to reality also depends on your mesh, supports and part data:

- **FEM:** the *mesh convergence check* re-solves the worst instant on a finer mesh and reports the change. The *.inp export* writes the same case for CalculiX, ANSYS or Abaqus.
- **CFD:** look at the grid uncertainty (GCI) and y+ in the notes of the 3D analysis.
- **Part spread:** datasheet values are typical; clone servos and motors can differ by 30–40 % in current. The render's uncertainty band draws that range.
