# Heat

Every part is one or more thermal nodes. Electrical losses, friction and hot gas heat the nodes; heat flows by conduction to the parts they are screwed to and by convection and radiation to the air. The temperature feeds back into the circuit and the mechanics.

## Node equation

```math
C_i\,\frac{dT_i}{dt} = P_i \;-\; \sum_j G_{ij}\,(T_i - T_j) \;-\; h_c A_i\,(T_i - T_\infty) \;-\; \varepsilon\,\sigma A_i\,\left(T_i^4 - T_\infty^4\right)
```

| Symbol | Meaning |
| --- | --- |
| $`C_i = m_i\,c_p`$ | Heat capacity (J/K): mass × the material's specific heat |
| $`P_i`$ | Power heating the node (W): $`I^2R`$, switching, friction, gas |
| $`G_{ij}`$ | Conductance between joined parts (W/K) |
| $`h_c`$ | Convection coefficient (W/m²K) |
| $`\varepsilon`$ | The material's emissivity (PLA 0.92, aluminium 0.1…) |

The network advances in 50 ms steps. Radiation is linearised: $`h_r = \varepsilon\sigma\,(T^2 + T_\infty^2)(T + T_\infty)`$.

## Convection with the real air speed

```math
h_c = \max\left(6,\; 10.45 - v + 10\sqrt{v}\right)\ \text{W/m²K}
```

Here $`v`$ is the real air speed over the part: the body's speed relative to the air (wind included), the slipstream of a propeller above it and its own spinning surface (an outrunner's bell). The slipstream, from disc theory with induced velocity $`v_i`$, at a distance $`d`$ from the disc:

```math
v_\text{slip} = v_i\left(1 + \frac{d}{\sqrt{d^2 + r^2}}\right)
```

## Motors: the real heat path

A motor is not one node; heat goes through the parts it really passes:

```mermaid
flowchart LR
    W["Winding<br/>copper loss I²R(T)"] -- "G = 0.12 W/K" --> K["Motor can<br/>brush, bearing friction"]
    K -- "G = 0.02 W/K" --> D["Gearbox (TT)<br/>gear friction"]
    D -- "screws" --> S["The chassis it is on"]
    K --> H["Air"]
    D --> H
```

The winding temperature changes copper's resistance (+0.393 % per °C): a warm motor draws less current at the same voltage and gives less torque. A yellow TT motor's screws go into its plastic gearbox, so little heat reaches the chassis: the winding heats, not the chassis.

The magnets' flux drops with temperature too: ferrite magnets by −0.2 % per °C, NdFeB by −0.12 %. A warm motor's back-EMF and torque constant shrink; it spins faster unloaded and gives less torque per amp. A ferrite motor whose housing has warmed from 25 °C to 92 °C spins 13.6 % faster unloaded (`MotorThermalTests`).

Outside the world (the Circuit page, a vehicle assembly) the motor runs its own two-node model. The winding is light: 10 % of the heat capacity, and 25 % of the thermal resistance lies from winding to housing. The housing is heavy and also takes the friction and gear losses, passing all the heat to the air. In a stalled motor the winding heats within seconds, the housing within minutes. In steady state the winding sits where the one-node model put it. In the world the thermal network above does this, and the magnets follow its housing (can) node.

## Battery

A battery heats by its internal resistance and polarization loss: $`P = I^2R + \lvert I\,V_p \rvert`$. Lithium cells above 130 °C go into thermal runaway (they turn into an open circuit and an event is written).

## How materials respond

| State | Criterion | Effect |
| --- | --- | --- |
| Softening | above the glass transition (PLA 60 °C, ABS 105 °C) | Young's modulus drops, joints loosen |
| Melting | at the melting point | The joint lets go, an event is written |
| Thermal stress | $`\sigma = -E\,\alpha\,\Delta T`$ (in the FEM analysis) | Added to the stress map |

To see it: **Görünüm → Isı kamerası (dünya)** (View → Thermal camera). With the mouse over a part, the temperature at that point and the part's nodes are shown.

## Validity range

The model's accuracy has been measured over the range below; the test behind each figure is in the [Validation](../guide/12-validation.md) chapter.

| Subject | Model | Measured | Outside the model |
| --- | --- | --- | --- |
| Live heat network | Lumped capacity: a part is one or a few nodes, one temperature per node; 50 ms step, backward Euler (unconditionally stable) | In a gear motor the winding's heat equals its temperature rise plus what goes into the air, within 10⁻⁶ J | The temperature field inside a single-node part (hot spots: the thermal analysis); thermal events shorter than 50 ms (a part's own fast thermal model follows those, such as a burning resistor) |
| Thermal analysis | Finite-volume grid inside the part | 1-D transient conduction within 0.0174 K of the Fourier series (for an 80 K step); a heated plate's equilibrium temperature within 0.005 % of the energy balance | Detail smaller than a grid cell |
| Convection | $`h_c = \max(6,\ 10.45 - v + 10\sqrt{v})`$, with the real air speed | — | Convection that depends on a surface's orientation and shape (vertical plate, inner channel), boundary-layer detail |
| Radiation | Grey body, to the surroundings, linearised | — | Parts radiating to each other (view factors), sunlight |
| Material | Glass transition and melting thresholds, thermal stress (FEM) | — | Creep, thermal fatigue, combustion chemistry |
