cmake_minimum_required(VERSION 3.20)
project(NativeEngine LANGUAGES C CXX)

set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 20)

add_library(NativeEngineCore OBJECT
    src/NativeEngine_Core.cpp
    src/MCU/ATmega328P_ISA.c
    src/Core/NodalSolver.cpp
    src/Core/BvmFormat.cpp
    src/Core/CircuitContext.cpp
    src/Physics/PhysicsWorld.cpp
)

add_library(NativeEngine SHARED
    $<TARGET_OBJECTS:NativeEngineCore>
)

add_executable(NativeEngineStandalone
    src/main.cpp
    $<TARGET_OBJECTS:NativeEngineCore>
)

target_include_directories(NativeEngineCore PRIVATE
    include
)

target_include_directories(NativeEngine PRIVATE
    include
)

target_include_directories(NativeEngineStandalone PRIVATE
    include
)

target_compile_options(NativeEngineCore PRIVATE
    $<$<C_COMPILER_ID:MSVC>:/O2 /GL>
    $<$<CXX_COMPILER_ID:MSVC>:/O2 /GL>
    $<$<NOT:$<C_COMPILER_ID:MSVC>>:-O3 -flto>
    $<$<NOT:$<CXX_COMPILER_ID:MSVC>>:-O3 -flto>
)

target_link_options(NativeEngine PRIVATE
    $<$<C_COMPILER_ID:MSVC>:/LTCG>
    $<$<CXX_COMPILER_ID:MSVC>:/LTCG>
    $<$<NOT:$<C_COMPILER_ID:MSVC>>:-flto>
    $<$<NOT:$<CXX_COMPILER_ID:MSVC>>:-flto>
)

target_link_options(NativeEngineStandalone PRIVATE
    $<$<C_COMPILER_ID:MSVC>:/LTCG>
    $<$<CXX_COMPILER_ID:MSVC>:/LTCG>
    $<$<NOT:$<C_COMPILER_ID:MSVC>>:-flto>
    $<$<NOT:$<CXX_COMPILER_ID:MSVC>>:-flto>
)
