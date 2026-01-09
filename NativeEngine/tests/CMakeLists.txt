# Physics Engine Test Suite CMakeLists.txt

cmake_minimum_required(VERSION 3.20)

# Test executable
add_executable(PhysicsEngineTests
    PhysicsEngineTests.cpp
)

# Link against NativeEngine library
target_link_libraries(PhysicsEngineTests PRIVATE
    NativeEngine_Core
)

target_include_directories(PhysicsEngineTests PRIVATE
    ${CMAKE_SOURCE_DIR}/include
)

# Enable C++17
set_target_properties(PhysicsEngineTests PROPERTIES
    CXX_STANDARD 17
    CXX_STANDARD_REQUIRED ON
)

# Add test to CTest
enable_testing()
add_test(NAME PhysicsEngineTests COMMAND PhysicsEngineTests)

# Platform-specific settings
if(MSVC)
    target_compile_options(PhysicsEngineTests PRIVATE /W4)
else()
    target_compile_options(PhysicsEngineTests PRIVATE -Wall -Wextra -Wpedantic)
endif()
