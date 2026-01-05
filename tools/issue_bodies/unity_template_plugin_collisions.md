Repro:

- Run: py tools/rt_tool.py build-standalone

Actual:

- Unity build fails with: Plugins colliding with each other.
- Collision list (from logs/unity/build.log):
  - Assets/Templates/arduino-led-starter/Code/U1/app.h
  - Assets/Templates/arduino-button-starter/Code/U1/app.h
  - Assets/Templates/arduino-led-starter/Code/U1/builds/bvm_build/sketch/app.h
  - Assets/Templates/arduino-button-starter/Code/U1/builds/bvm_build/sketch/app.h
  - Assets/Templates/arduino-led-starter/Code/U1/builds/bvm_build/sketch/U1.ino.cpp
  - Assets/Templates/arduino-button-starter/Code/U1/builds/bvm_build/sketch/U1.ino.cpp

Expected:

- Template source/build artifacts should not be imported as build plugins for Windows standalone.

Notes:

- Fix candidates: disable PluginImporter for these assets, or move template build outputs out of Assets/ so they are never considered plugins during player build.
