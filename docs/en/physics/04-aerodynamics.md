# Aerodynamics

## Atmosphere

Air density, pressure and temperature come from the International Standard Atmosphere (ISA) at the body's height. In the troposphere:

```math
T = T_0 - L\,h, \qquad p = p_0\left(\frac{T}{T_0}\right)^{g/(R L)}, \qquad \rho = \frac{p}{R\,T}
```

$`T_0 = 288.15`$ K, $`L = 0.0065`$ K/m, $`p_0 = 101\,325`$ Pa, $`R = 287.05`$ J/kg·K.

## Drag, face by face

A body's resistance to the air has three parts, each computed where it really acts:

| Part | Where | Coefficient |
| --- | --- | --- |
| Stagnation pressure | The face meeting the flow (leading) | 70 % of the lone body's pressure drag |
| Base suction | The face behind (trailing) | 30 % |
| Skin friction | The sides the flow runs along | $`C_f = 0.004`$ (turbulent plate, $`Re \approx 10^5\text{–}10^6`$) |

```math
\mathbf{F} = -\tfrac{1}{2}\,\rho\,(C_D A)(\hat{\mathbf{u}})\,\lvert\mathbf{u}\rvert\,\mathbf{u}, \qquad \mathbf{u} = \mathbf{v}_\text{body} - \mathbf{v}_\text{wind}
```

A body on its own keeps its measured $`C_D`$ exactly (friction is part of it):

| Shape | $`C_D`$ | Source |
| --- | --- | --- |
| Sphere | 0.47 | subcritical $`Re`$ (10³–2·10⁵) |
| Box face | 1.05 | flat-faced block |
| Cylinder, side-on | 1.0 | cross-flow |
| Cylinder, along its axis | 0.9 | short cylinder |
| Model body (hull) | from its faces | see below |

A body from the modeller or a file gets its drag from its own faces, not from the box around it. A face meeting the flow at angle $`\theta`$ carries $`C_p \approx 0.8\,\sin^2\theta`$ on its projected area (Hoerner's subsonic forebody estimate): a flat face 0.8, a 20° cone ≈ 0.09, a slender ogive nose almost nothing. Trailing faces see a base suction of $`0.15\,\sin^2\theta`$; a tapering tail recovers it, a blunt base does not.

### Joined parts shield each other

Where a joined part covers a face of another, that face sees neither stagnation pressure nor base suction. Coverage is measured on every axis face of every body with lines through the real collision shapes (12 × 12 samples):

```mermaid
flowchart LR
    N["Nose cone<br/>front: open<br/>base: covered by the tube"] --- B["Body tube<br/>front: covered by the nose<br/>base: open<br/>sides: friction"]
    B --- F["Fins<br/>wing forces"]
```

So a rocket's drag is not "the sum of its parts flying alone" but the nose's front + the tail's base + friction along it. Coverage is sampled over each body's own extent, so a slender tube (Ø41 × 500 mm) is measured correctly too. Validation: nose + tube axial $`C_D`$ was 1.37 as separate parts and is 0.64 as an assembly (real blunt-based rockets: 0.5–0.7); the solid rocket with an ogive nose and three square-edged fins has 0.62 (OpenRocket-class estimates 0.5–0.7) and keeps it through the flight; the water rocket's apogee went from 26 m to 39.6 m (published flights: 30–50 m).

### Oblique flow

Each axis sees the dynamic pressure of its own velocity component (Allen and Perkins' cross-flow principle). The force need not be exactly against the flow; a tilted flat face also pushes sideways:

```math
F_i = -\tfrac{1}{2}\,\rho\,(C_D A)_i\,\lvert u_i\rvert\,u_i \quad (\text{box, each axis on its own}), \qquad
\mathbf{F}_\perp = -\tfrac{1}{2}\,\rho\,(C_D A)_\text{side}\,\lvert\mathbf{u}_\perp\rvert\,\mathbf{u}_\perp \quad (\text{a cylinder's round section})
```

So the cross-flow drag a slender body adds at a small angle of attack ($`\alpha`$) grows as $`\sin^2\alpha`$, not as $`\sin\alpha`$: a 2° wobble barely changes a rocket's drag. A sphere shows the same area every way and sees the whole speed. For model bodies, whether a section is round or flat is found by measuring the shadow at 45°.

## Wings: strip theory + lifting line

A wing is cut into spanwise strips. Each strip sees its own local air velocity ($`\mathbf{V} + \boldsymbol{\omega}\times\mathbf{r}`$), so roll damping and asymmetric stall follow from the geometry.

```math
\alpha_e = \alpha - \alpha_i, \qquad \alpha_i = \frac{C_L}{\pi\,e\,AR}, \qquad C_{D,i} = \frac{C_L^2}{\pi\,e\,AR}
```

This gives a finite wing's lift slope $`a = a_0 / (1 + a_0/(\pi e AR))`$. Section coefficients come from the airfoil's polar over ±180° (Clark Y, NACA 0012, NACA 2412, flat plate), stall and post-stall included. Control surfaces shift the angle by $`\tau\,\eta(\delta)\,\delta`$ and return their hinge moment to the servo. A tail sees the downwash of the wing ahead: $`\varepsilon = 2C_{L,w}/(\pi AR_w)`$.

## Propellers

With the advance ratio $`J = V/(nD)`$ (fitted to the trends of the UIUC database, Brandt & Selig 2011):

```math
T = C_T\,\rho\,n^2 D^4, \qquad Q = \frac{C_P}{2\pi}\,\rho\,n^2 D^5, \qquad
C_T = C_{T0}\left(1 - \frac{J}{J_0}\right), \qquad C_P = C_{P0}\left(1 - \left(\frac{J}{1.2\,J_0}\right)^2\right)
```

$`J_0 \approx 1.05\,P/D`$ (pitch / diameter). The propeller's torque loads its motor; the thrust acts on the body.


Every propeller in a world uses this curve: thrust falls with the speed of the air entering the disc along its axis, reaches zero as the aircraft approaches the prop's pitch speed, and beyond it the prop brakes (windmills). Example: an 8×4.5" prop at 8,200 rpm pushes ≈ 4.2 N on the ground and ≈ 1.3 N at 12 m/s (J ≈ 0.4). Earlier versions used only the static $`k_T\,\omega^2`$ for world propellers, so this page and the code disagreed; fixed.

**The propeller as a body.** A spinning propeller's forces are all in the model above. Standing still, the axial flow only meets the blades' own area: solidity $`\sigma \approx 0.1`$, a tenth of the disc ($`C_D \approx 1.1`$), fading as $`V^2/(V^2 + (0.75\,\omega R)^2)`$ while the blades speed up. Edgewise the flow sees the blade thickness across the diameter. A propeller does not drag like a solid disc (on an 8" prop that would be a phantom 1.1 N brake at 8 m/s).

## Limits and validation

- **Real-time flight** uses the strip + lifting-line model above at every physics step. Not modelled: the propeller slipstream on the wing and tail, wing–body interference, flexible wings. The tail sees only the wing's downwash.
- **3D flow (lattice-Boltzmann)** is solved only in the render, at a few instants of the recording (one per half second, at most six), and is not fed back into the flight. The solver is a D3Q19 lattice:
  - **Collision:** regularized BGK; the non-equilibrium part is rebuilt from its second moment. This stays stable down to a relaxation time τ → ½, so the flow is solved at the **real Reynolds number** (≈ 10⁵ on a 1 m model plane). Eddies smaller than a cell are represented by the Smagorinsky sub-grid model (LES, C = 0.17).
  - **Walls:** from each part's signed distance function, the true surface position on every lattice link is found and interpolated bounce-back (Bouzidi) is applied. Curved surfaces are seen with second-order accuracy instead of as a staircase. Parts thinner than a cell are represented one cell thick.
  - **Boundaries:** the inlet is a moving-wall velocity boundary (Ladd) that delivers the exact flow rate. The sides are open at ambient pressure, so air displaced by the parts can leave sideways. An absorbing layer along the edges keeps pressure waves and eddies from reflecting back off the boundary.
  - **Domain:** 1.5 assembly sizes ahead, 4 behind, 3.2 across. The force is read once successive averaging windows agree within 1 %.
  - **Trust:** each result's notes give the blockage ratio, the boundary-layer thickness and the y+ of the first cell, and a grid uncertainty (GCI, Roache) from solving the first instant on two grids. When the boundary layer is thinner than a cell, skin friction is under-resolved; the pressure drag of sharp-edged bluff bodies is still valid. The result does not replace a mesh study with an industrial CFD solver.
- **Validation tests** run with every release. All the numbers are on the [12. Validation](../guide/12-validation.md) page:
  - Poiseuille channel flow;
  - the Schäfer–Turek DFG cylinder benchmarks: 2D-1 (Re 20) drag within 1 %, 2D-2 (Re 100) Strouhal number within 0.1 %;
  - sphere drag at Re 20 and Re 100, 8–11 % above the standard drag curve (Schiller–Naumann), a finite-domain effect;
  - in FEM, thermal expansion and the inertia-relief balance of a freely accelerating bar.
  Flow at model-aircraft scale has not been validated independently (wind tunnel or flight data) yet.
- **Parts inside a closed body:** parts inside a hollow body (a shell) do not see the air; the drag is on the shell's outer faces.

## Wind and turbulence

Random gusts filtered to first order with the Dryden model are added to the mean wind. Wind changes the drag, the angle the wings see and [convective cooling](03-heat.md).
