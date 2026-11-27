# 14. Learning path: five levels, hands-on tasks

This path teaches RobotWin Studio from end to end: the circuit first, then the board's code, then mechanics and the PCB, then robots, and industrial cells last. Each level starts from a ready example inside the app and ends with tasks you do on your own. Every task has an **expected result** you can check what you see against; the values come from the simulation's own solvers and are close to what you would measure on a bench.

The examples are the **Circuit and robot examples** cards on the start page (a click on a card first shows what it teaches and the tools it needs); world templates are in the **Worlds** list. Do not be afraid of breaking an example: the original stays unchanged unless you save over it.

If this is your first time, start with the **Your first robot** guide on the start page (5–10 minutes); this path carries on from it.

Each level can be walked step by step inside the app too: **Help → Learning path**, "learning path" in the command palette, or **Start the guide** on the info screen of the level's first example. The card in the corner opens the example and says what to do; it moves on by itself on the steps it can see (the example open, the simulation running, the page shown), and on those it cannot (reading a value, an edit of your own) you go on with **Done, next**. The last card is the level's own task and leads to the next level.

## Level 1: Electronics basics

**Start:** the *LED and Button* example.

1. Start the simulation with **F5** and press the button. The LED lights. Click the LED and look at the live values at the top of the inspector.
   - Expected: with a 9 V battery, 470 Ω and a red LED the current is about 15 mA; about 2 V across the LED.
2. Delete the resistor and start again. The LED's current goes past its limit and the LED burns out; the reason shows on the part and in the **Events** tab below. Undo (**Ctrl+Z**).
3. Open the *RC Low-Pass Filter* example and connect the oscilloscope (the **Oscilloscope** tab below) to the output.
   - Expected: with 1 kΩ and 1 µF the time constant RC = 1 ms and the corner frequency 1/(2πRC) ≈ 159 Hz; on the 1 kHz square wave the output rises exponentially, to about 63 % of its final value after 1 ms.
4. Keep this waveform with **REF**, double the capacitor and run again: the two waveforms show one over the other and the status line gives the deviation.
5. Measure the gain and the regulator's dropout in the *Op-Amp Amplifier* and *7805 Power Supply* examples.

**Your task:** build a divider from 5 V to 3.3 V and measure with the multimeter how far the output drops under a 10 kΩ load.

## Level 2: Embedded code

**Start:** the *Arduino: Real Firmware* example (its firmware comes prebuilt).

1. Open the **Code** page. Change the `delay` value, build (**F7**) and run (**F5**): the LED blinks at the new rate.
2. Put the caret on a pin number in the code: the line below says what that pin is wired to on the board ("D13 → R1", for example), and the part is selected in the circuit too.
3. Set a breakpoint on a line (click its number), run, watch the values in the **Variables** and **Registers** tabs and step through.
4. When the sketch writes to `Serial`, the Arduino drives its TX pin (D1) bit by bit: probe D1 with the oscilloscope, choose UART in the **Decoder** and click a byte in the strip; the cursors go to its start and end.
5. In the *ESP32: PWM Dimming* example turn the potentiometer: the LED's brightness follows the PWM duty cycle; see the PWM on the oscilloscope.
   - Expected: 5 kHz PWM; the duty cycle linear in the potentiometer's position.

**Your task:** write a sketch that counts button presses and shows the count on the LCD; remove the button's bounce in software and see the bounce on the oscilloscope.

## Level 3: Mechanics and PCB

**Start:** the *Driving a Motor with a MOSFET* example.

1. On the **PCB** page place the board automatically, route it and run the **DRC**. Mark an error as an exception with the wrench button in the **Problems** panel, or fix it.
2. Select a part in the circuit: it is selected on the PCB too (cross-selection).
3. Look at the manufacturing readiness summary and export the Gerber and drill files.
4. On the **Part** page draw a motor holder (sketch, dimensions, extrusion) and look at its centre of mass and mass.
5. In the *Electronics box* world template check that the board fits the box and the cable holes.

**Your task:** design a 50 × 30 mm board, put M3 holes in its corners, pass the DRC clean and model its box on the Part page.

## Level 4: Robotics

**Start:** the *Robot: Obstacle Avoider (Arduino)* example (it opens assembled in the Montaj).

1. In the Montaj drive the robot with its own code using **▶ Run the world**. Select a motor: its cables, joints, centre of mass and, while running, its torque show on it (**Technical highlight**).
2. In the *Two-axis servo arm with Arduino* template open the inverse kinematics and send the arm to a target; give it one it cannot reach and read why (which axis' limit, or how far away).
3. Measure the period in the *Pendulum* template.
   - Expected: for a compound pendulum T = 2π√(I/mgh), about 1.11 s when released from 30°.
4. Compare the sensor readings on the different tapes in the *Line-following robot* template; look at the camera, LiDAR and IMU data in the *Sensor rover*.
5. Have the inverse kinematics work out the wheel speeds of the *Mecanum-wheel car* and drive it sideways.

**Your task:** change the obstacle distance and the turning time in the *Robot: Obstacle Avoider*'s code so that it gets round the course with the fewest collisions; sweep the two values with **Experiments** (chapter 13) and choose the best with its 95 % confidence interval. The *Robot: Line Follower*, by the way, is an analog circuit with no code: it does the same job with resistors.

## Level 5: Industry

**Start:** the *Factory cell (robot arm + conveyor)* template.

1. Run the cell; look at the station cycle time and the bottleneck.
2. Add a PLC with **Parts → Robot Cell → PLC**, wire sensors to its inputs and the conveyor to its outputs, and program it in Structured Text (ST) (chapter 7, PLC). Watch the live values in the ladder diagram and stop and start the cell from the HMI panel.
3. Change the gripper's timing in the *SCARA pick-and-place* template and count the parts it misses.
4. Watch the cell from outside over Modbus TCP or OPC UA (chapter 7).
5. Add a failure scenario (a stuck sensor, a valve delay) and see how the cell behaves.

**Your task:** add a second station to the conveyor, find the order that cuts the cycle time by 20 % and show the result with an experiment report.

## Next

- Add a part of your own to the library and write physics of your own as a plugin (developer guide: SDK).
- Export a project to KiCad, put an FMU model in a circuit, bring a URDF robot into a world.
- Report something missing: **Help → Report a problem…**
