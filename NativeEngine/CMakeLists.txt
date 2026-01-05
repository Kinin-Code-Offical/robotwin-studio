cmake_minimum_required(VERSION 3.20)
project(NativeEngine LANGUAGES C CXX)

enable_testing()

add_test(NAME nativeengine_smoketest COMMAND ${CMAKE_COMMAND} -E echo "NativeEngine CTest placeholder")

set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 20)

set(NATIVE_OUTPUT_DIR "${CMAKE_SOURCE_DIR}/../builds/native")
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${NATIVE_OUTPUT_DIR}")
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${NATIVE_OUTPUT_DIR}")
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY "${NATIVE_OUTPUT_DIR}")

foreach(OUTPUT_CONFIG DEBUG RELEASE RELWITHDEBINFO MINSIZEREL)
    set(CMAKE_RUNTIME_OUTPUT_DIRECTORY_${OUTPUT_CONFIG} "${NATIVE_OUTPUT_DIR}")
    set(CMAKE_LIBRARY_OUTPUT_DIRECTORY_${OUTPUT_CONFIG} "${NATIVE_OUTPUT_DIR}")
    set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY_${OUTPUT_CONFIG} "${NATIVE_OUTPUT_DIR}")
endforeach()

add_library(NativeEngineCore OBJECT
    src/NativeEngine_Core.cpp
    src/MCU/ATmega328P_ISA.c
    src/Circuit/NodalSolver.cpp
    src/Circuit/BvmFormat.cpp
    src/Circuit/CircuitContext.cpp
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
