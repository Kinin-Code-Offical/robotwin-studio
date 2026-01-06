cmake_minimum_required(VERSION 3.16)
project(RoboTwinFirmwareHost LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

get_filename_component(REPO_ROOT "${CMAKE_CURRENT_LIST_DIR}/.." ABSOLUTE)
set(OUT_DIR "${REPO_ROOT}/builds/firmware")

set(SOURCES
    "${REPO_ROOT}/FirmwareEngine/main.cpp"
    "${REPO_ROOT}/FirmwareEngine/PipeManager.cpp"
    "${REPO_ROOT}/FirmwareEngine/VirtualMcu.cpp"
    "${REPO_ROOT}/FirmwareEngine/BoardProfile.cpp"
    "${REPO_ROOT}/FirmwareEngine/Rpi/RpiBackend.cpp"
    "${REPO_ROOT}/FirmwareEngine/Rpi/RpiShm.cpp"
    "${REPO_ROOT}/FirmwareEngine/Rpi/QemuProcess.cpp"
    "${REPO_ROOT}/NativeEngine/src/MCU/ATmega328P_ISA.c"
)

add_executable(RoboTwinFirmwareHost ${SOURCES})
target_include_directories(RoboTwinFirmwareHost PRIVATE
    "${REPO_ROOT}/FirmwareEngine"
    "${REPO_ROOT}/FirmwareEngine/Rpi"
    "${REPO_ROOT}/NativeEngine/include"
)

set_target_properties(RoboTwinFirmwareHost PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${OUT_DIR}"
)

if (MINGW)
    target_link_options(RoboTwinFirmwareHost PRIVATE -static-libgcc -static-libstdc++)
endif()

# Minimal lockstep regression test: ensure every Step produces OutputState.
add_executable(RoboTwinFirmwareHostLockstepSanity
    "${REPO_ROOT}/FirmwareEngine/LockstepSanityTest.cpp"
)
target_include_directories(RoboTwinFirmwareHostLockstepSanity PRIVATE
    "${REPO_ROOT}/FirmwareEngine"
)
set_target_properties(RoboTwinFirmwareHostLockstepSanity PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${OUT_DIR}"
)
