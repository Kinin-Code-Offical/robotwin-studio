# Liquids

Water, oil, honey and other liquids are solved as particles in the world. Each particle carries an equal share of the liquid's mass. The particles rest at the real density and flow with the real viscosity and surface tension. The liquid pushes on solid parts and the parts push back: a glass carries the weight of its water, and a part dropped into water feels buoyancy.

## The liquid itself: position-based fluids (PBF)

Incompressibility is one density constraint per particle (Macklin & Müller 2013). It is solved like every other XPBD constraint in the scene, such as the cables' wire.

- **Density:** an SPH sum over the neighbours, $`\rho_i = \sum_j m\,W(\lVert x_i - x_j\rVert, h)`$. $`W`$ is the cubic-spline kernel; $`h`$ is twice the particle spacing, which gives about 33 neighbours.
- **Particle mass:** $`m = \rho_0 / S_0`$, where $`S_0`$ is the kernel sum of a perfect cubic lattice. A resting liquid is therefore exactly at the real density.
- **Constraint:** $`C_i = \max(\rho_i/\rho_0 - 1,\ 0)`$. Pressure only pushes; the free surface stays free.
- **Correction:** $`\lambda_i = -C_i / (\sum_k \lvert\nabla_k C_i\rvert^2 + \varepsilon)`$ and $`\Delta x_i = \tfrac{m}{\rho_0}\sum_j (\lambda_i + \lambda_j)\nabla W_{ij}`$. This is iterated a few times per substep: 4 in the live view, 5–12 in a render.

## Walls: density maps

Every solid part and the ground contributes the density of the liquid a wall would hold back (Koschier & Bender 2017). The contribution is a function of the particle's distance $`d`$ to the wall, assuming the wall is locally flat:

```math
\rho_\text{wall}(d) = \frac{m}{s^3}\,\Phi(d), \qquad \Phi(d) = \int_{z>d} W\,\mathrm{d}V = \int_d^h A(z)\,\mathrm{d}z, \qquad A(z) = 2\pi\int_z^h W(r)\,r\,\mathrm{d}r
```

$`\Phi`$ and $`A`$ are tabulated once. This has three consequences:

- The wall carries the hydrostatic pressure of the liquid behind it.
- A particle is pushed back without ever touching the wall.
- The reaction to that push is the force on the wall's body.

Simple flat-faced bodies (a box, the ground) use this flat-wall formula. For bodies made of pieces or meshes (a glass), the kernel integral of the solid itself is precomputed on a grid in the body's frame: $`\Phi(x) = \int_\text{solid} W(\lVert x - y
Vert)\,\mathrm{d}y`$, each cell's solid share taken from the signed distance and convolved with the kernel. The inside corner of a glass, its thin wall and a part behind that wall then press as much as they really do. Of two flat faces facing the same way, the one behind adds no pressure: it lies in the nearer one's shadow.

The distance comes from each body's signed distance function. For bodies made of pieces or meshes, such as a glass built from 25 convex pieces, it is sampled once on a grid in the body's own frame. The cell is a third of the particle spacing. A query is then a trilinear interpolation, and the normal is the field's gradient.

**No tunnelling:** a particle may not cross a solid within a step. This is judged in the body's own frame, because the glass may be turning. The distance can change by at most the distance moved, so a particle whose clearances at both ends together exceed its move skips the exact ray test; most particles pass this cheap check.

## Viscosity and surface tension

- **Viscosity:** the liquid's real kinematic viscosity $`\nu = \mu/\rho`$, using the SPH Laplacian of Morris et al. (1997):

  ```math
  \frac{\mathrm{d}v_i}{\mathrm{d}t} = \sum_j \frac{2\nu\,m}{\rho_j}\,\frac{r_{ij}\cdot\nabla W_{ij}}{r_{ij}^2 + 0.01\,\ell^2}\,(v_i - v_j)
  ```

  It is taken implicitly, so honey ($`\nu \approx 7\cdot10^{-3}`$ m²/s) is stable at the same step. The wall is a no-slip boundary moving with its body. Its share is computed over the half-space beyond it as $`\int -W'(r)/r\,\mathrm{d}V = 2\pi\int_d^h W(z)\,\mathrm{d}z`$.
- **Surface tension:** applied as an attraction between particles (cohesion): $`F(r) = \varepsilon \sin(\pi r/h)`$. The strength $`\varepsilon`$ follows from the liquid's real $`\sigma`$ through the Kirkwood–Buff (Fowler) relation for the energy of a flat cut, where $`n`$ is the number of particles per m³:

  ```math
  \sigma = \frac{\pi n^2}{8}\int_0^h F(r)\,r^4\,\mathrm{d}r
  ```

  Inside the liquid the pulls cancel. At a surface, in a thin sheet and along a stream they hold the liquid together, sparse particles included.
- **Wetting:** each wall pulls like a liquid-filled half-space, scaled by $`(1 + \cos\theta)/2`$. This gives Young–Dupré's work of adhesion $`W = \sigma(1 + \cos\theta)`$. The contact angle $`\theta`$ comes from the body's material:

  | Material | Glass | Steel | PLA | ABS | Acrylic | Wood | Rubber | PTFE |
  | --- | --- | --- | --- | --- | --- | --- | --- | --- |
  | Water contact angle θ | 25° | 70° | 75° | 80° | 70° | 60° | 100° | 110° |

  Oil and alcohol wet most surfaces better (≈20°). The wall carries the liquid's pull as a reaction.
- **Air drag:** particles exposed to the air (spray, drops, thin streams) are dragged by the world's air. Each one is treated as a sphere of its own volume, with the drag coefficient from Schiller–Naumann ($`C_D = 24/Re\,(1 + 0.15\,Re^{0.687})`$, 0.44 above Re 1000). The wind is included. The bulk of the liquid shields itself.

| Liquid (20 °C) | Density (kg/m³) | Viscosity (mPa·s) | Surface tension (mN/m) |
| --- | --- | --- | --- |
| Water | 998.2 | 1.002 | 72.8 |
| Sea water | 1024.8 | 1.08 | 73.5 |
| Ethanol | 789.3 | 1.20 | 22.3 |
| Milk | 1030 | 2.0 | 45 |
| Sunflower oil | 919 | 55.8 | 33.5 |
| Glycerol | 1261 | 1410 | 63.4 |
| Honey | 1420 | ≈10 000 | 50 |

## Time step

No particle moves more than 0.3 spacing in a substep (the CFL condition). As the liquid speeds up, the number of substeps rises on its own.

**The live view** is interactive:

- The liquid is solved every 1 ms (about 3000 particles, 4 iterations). A longer step let a resting column compress by 8 %; at 1 ms it stays under 1 %.
- Still water in a still glass sleeps.

**A render** computes everything in full, however long it takes:

- No liquid substep is longer than the accuracy's physics step (0.1 ms on the finest setting), and there is no sleeping.
- When the world starts, the liquid is laid out on a lattice that fills its whole volume (a whole number of layers in height), its mass is set to ρ·V, and the density constraint is projected without time passing: the water starts still, at its real mass and density.
- The pressure is iterated until the largest compression falls below a tolerance (0.2 % on the finest everyday setting, 0.05 % on Reference), not for a fixed number of iterations.
- It uses 8 000–120 000 particles depending on the accuracy setting (40 000 on Hassas).
- The surface is extracted on a finer grid.

## Look

The surface is built from the particles in four steps:

1. Particle centres are smoothed towards their neighbours (Yu & Turk 2013), which flattens a resting surface.
2. Each particle's kernel is stretched along the principal axes of its neighbours' covariance. These are Yu & Turk's anisotropic kernels:
   - flat in a thin sheet;
   - long along a thin stream;
   - round inside the liquid.

   A sheet spreading over a dry floor and a falling stream therefore read as continuous.
3. The field $`\phi = \sum_j W(\lvert G_j(x - \bar x_j)\rvert)\det G_j / S_0`$ is sampled on a grid.
4. The surface $`\phi = 1/2`$ is extracted with surface nets; normals come from the field's gradient.
5. Particles flying alone, with fewer than two neighbours, are drawn as drops. In the live view each drop is stretched along its velocity by the streak a real camera would record in a 1/60 s exposure, so a thin falling stream reads as a stream rather than separate beads.

WPF has no shaders, so the optics are computed per vertex:

- The Fresnel reflectance towards the camera uses Schlick's approximation; for water $`F_0 = ((n-1)/(n+1))^2 \approx 0.02`$.
- Looking straight in, the liquid is nearly clear and shows its own colour.
- At grazing angles it mirrors the sky.
- A sharp highlight carries the sun.

Glass and acrylic parts are drawn after the liquid, so water shows through a glass.

## Validation

The numbers are on the [12. Validation](../guide/12-validation.md) page:

- **Water at rest:** 6 cm of water presses on a side wall with its hydrostatic force within 2 %. The interior density is within 0.2 % of the real density, and the largest compression is under 1 % (0.01 % at a render's 0.1 ms step).
- **Start:** the water in a glass has exactly its ρV mass and is still when the world starts.
- **Free container:** a free container on the floor carries the weight of its water within 0.1 %.
- **Dam break:** the surge front runs 3–8 % ahead of the Martin & Moyce (1952) experiment. Numerical methods usually lead this experiment, because its gate was not removed at once.

## Limits

- **No film thinner than a particle:** a sheet thinner than one particle (the first water spreading over a dry floor) breaks into pieces. Particles are 3–4 mm in the live view, 1.7 mm in a Hassas render and 1.2 mm on Reference. The film running down the outside of a pouring glass does not appear for the same reason.
- **Surface tension at coarse resolution:** a drop a few particles in radius does not oscillate. Its particles lock into a regular arrangement that a small Laplace pressure cannot rearrange. Surface tension acts correctly at large scale (holding a stream and a sheet together, wetting), not in sub-millimetre dynamics.
- **No bubbles:** no air bubbles form in the liquid.
