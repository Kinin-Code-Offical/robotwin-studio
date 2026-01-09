cmake_minimum_required(VERSION 3.20)
project(NativeEngine LANGUAGES C CXX)

enable_testing()

add_test(NAME nativeengine_smoketest COMMAND ${CMAKE_COMMAND} -E echo "NativeEngine CTest placeholder")

set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 20)

# === PERFORMANCE OPTIMIZATIONS ===
if(MSVC)
    # Windows/MSVC: Maximize Speed, Intrinsic Functions, Favor Speed, Frame Pointer Omission, Whole Program Optimization, Fast Floating Point
    add_compile_options(/O2 /Oi /Ot /Oy /GL /fp:fast)
    # Enable AVX2 if supported
    add_compile_options(/arch:AVX2)
else()
    # Unix/GCC/Clang: O3, Native Architecture
    add_compile_options(-O3 -march=native -ffast-math -funroll-loops)
endif()

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
    $<$<C_COMPILER_ID:MSVC>:/O2 /GL /fp:fast /arch:AVX2>
    $<$<CXX_COMPILER_ID:MSVC>:/O2 /GL /fp:fast /arch:AVX2>
    $<$<NOT:$<C_COMPILER_ID:MSVC>>:-O3 -flto -march=native -ffast-math>
    $<$<NOT:$<CXX_COMPILER_ID:MSVC>>:-O3 -flto -march=native -ffast-math>
)

# Enable GLM SIMD optimizations
target_compile_definitions(NativeEngineCore PRIVATE
    GLM_FORCE_INTRINSICS
    GLM_FORCE_ALIGNED_GENTYPES
    GLM_FORCE_DEFAULT_ALIGNED_GENTYPES
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

# Internal Validation Tools
if (EXISTS "${CMAKE_SOURCE_DIR}/../tests/native/PhysicsDeepValidation.cpp")
    message(STATUS "Adding PhysicsDeepValidation target")
    add_executable(PhysicsDeepValidation 
        "${CMAKE_SOURCE_DIR}/../tests/native/PhysicsDeepValidation.cpp"
        $<TARGET_OBJECTS:NativeEngineCore>
    )
    target_include_directories(PhysicsDeepValidation PRIVATE include)
    target_compile_definitions(PhysicsDeepValidation PRIVATE _USE_MATH_DEFINES)
    
    target_compile_options(PhysicsDeepValidation PRIVATE
        $<$<C_COMPILER_ID:MSVC>:/EHsc>
    )
endif()

if (EXISTS "${CMAKE_SOURCE_DIR}/../tests/native/SensorInternalValidation.cpp")
    message(STATUS "Adding SensorInternalValidation target")
    add_executable(SensorInternalValidation 
        "${CMAKE_SOURCE_DIR}/../tests/native/SensorInternalValidation.cpp"
    )
    target_include_directories(SensorInternalValidation PRIVATE 
        include
        "${CMAKE_SOURCE_DIR}/../FirmwareEngine/include"
        "${CMAKE_SOURCE_DIR}/../FirmwareEngine/U1"
        "${CMAKE_SOURCE_DIR}/../tests/native"
    )
    target_compile_definitions(SensorInternalValidation PRIVATE _USE_MATH_DEFINES)
    target_compile_options(SensorInternalValidation PRIVATE
        $<$<C_COMPILER_ID:MSVC>:/EHsc>
    )
endif()

if (EXISTS "${CMAKE_SOURCE_DIR}/../tests/native/FirmwareIntegrationTest.cpp")
    message(STATUS "Adding FirmwareIntegrationTest target")
    add_executable(FirmwareIntegrationTest
        "${CMAKE_SOURCE_DIR}/../tests/native/FirmwareIntegrationTest.cpp"
    )
    target_include_directories(FirmwareIntegrationTest PRIVATE
        include
        "${CMAKE_SOURCE_DIR}/../FirmwareEngine/include"
        "${CMAKE_SOURCE_DIR}/../FirmwareEngine/U1"
        "${CMAKE_SOURCE_DIR}/../tests/native/mocks"
    )
    target_compile_definitions(FirmwareIntegrationTest PRIVATE _USE_MATH_DEFINES)
    target_compile_options(FirmwareIntegrationTest PRIVATE
        $<$<C_COMPILER_ID:MSVC>:/EHsc>
    )
endif()
