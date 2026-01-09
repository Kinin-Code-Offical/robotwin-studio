cmake_minimum_required(VERSION 3.20)
project(NativeEngineTests)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Add NativeEngine source directory
set(NATIVE_ENGINE_DIR "${CMAKE_SOURCE_DIR}/../../NativeEngine")
include_directories(${NATIVE_ENGINE_DIR}/include)

# Physics Engine Tests
add_executable(PhysicsEngineTests
    PhysicsEngineTests.cpp
    ${NATIVE_ENGINE_DIR}/src/Physics/PhysicsWorld.cpp
)

# Sensor Edge Case Validation
add_executable(SensorEdgeCaseTests
    SensorEdgeCaseTests.cpp
)

# Enable testing
enable_testing()
add_test(NAME PhysicsEngineTests COMMAND PhysicsEngineTests)
add_test(NAME SensorEdgeCaseTests COMMAND SensorEdgeCaseTests)

# Platform-specific settings
if(WIN32)
    target_compile_definitions(PhysicsEngineTests PRIVATE _CRT_SECURE_NO_WARNINGS)
    target_compile_definitions(SensorEdgeCaseTests PRIVATE _CRT_SECURE_NO_WARNINGS)
endif()
