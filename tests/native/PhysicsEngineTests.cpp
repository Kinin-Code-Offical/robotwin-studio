// Physics Engine Comprehensive Test Suite
// Tests for collision detection, forces, constraints, integration

#include "Physics/PhysicsWorld.h"
#include "Physics/RigidBody.h"
#include <cassert>
#include <cmath>
#include <iostream>
#include <vector>
#include <chrono>

namespace NativeEngine::Physics::Tests
{

    constexpr float kEpsilon = 1e-4f;

    bool NearEqual(float a, float b, float epsilon = kEpsilon)
    {
        return std::fabs(a - b) < epsilon;
    }

    bool Vec3NearEqual(const Vec3 &a, const Vec3 &b, float epsilon = kEpsilon)
    {
        return NearEqual(a.x, b.x, epsilon) &&
               NearEqual(a.y, b.y, epsilon) &&
               NearEqual(a.z, b.z, epsilon);
    }

    // Test 1: Basic Body Creation and Retrieval
    bool Test_BodyCreationAndRetrieval()
    {
        PhysicsWorld world;

        RigidBody body{};
        body.mass = 5.0f;
        body.position = {1.0f, 2.0f, 3.0f};
        body.radius = 0.5f;

        uint32_t id = world.AddBody(body);
        assert(id > 0);

        RigidBody retrieved{};
        bool found = world.GetBody(id, retrieved);
        assert(found);
        assert(NearEqual(retrieved.mass, 5.0f));
        assert(Vec3NearEqual(retrieved.position, {1.0f, 2.0f, 3.0f}));

        std::cout << "[PASS] Test_BodyCreationAndRetrieval\n";
        return true;
    }

    // Test 2: Force Application
    bool Test_ForceApplication()
    {
        PhysicsWorld world;

        RigidBody body{};
        body.mass = 2.0f;
        body.position = {0.0f, 0.0f, 0.0f};
        body.velocity = {0.0f, 0.0f, 0.0f};

        uint32_t id = world.AddBody(body);

        // Apply upward force
        world.ApplyForce(id, {0.0f, 10.0f, 0.0f});

        // Get body and check force accumulator
        RigidBody updated{};
        world.GetBody(id, updated);
        assert(NearEqual(updated.force_accum.y, 10.0f));

        std::cout << "[PASS] Test_ForceApplication\n";
        return true;
    }

    // Test 3: Gravity Integration
    bool Test_GravityIntegration()
    {
        PhysicsWorld world;
        PhysicsConfig config{};
        config.gravity = {0.0f, -9.81f, 0.0f};
        config.base_dt = 0.016f;
        world.SetConfig(config);

        RigidBody body{};
        body.mass = 1.0f;
        body.position = {0.0f, 10.0f, 0.0f};
        body.velocity = {0.0f, 0.0f, 0.0f};
        body.is_static = false;

        uint32_t id = world.AddBody(body);

        // Step simulation
        world.Step(0.016f);

        RigidBody updated{};
        world.GetBody(id, updated);

        // Body should have moved down due to gravity
        assert(updated.position.y < 10.0f);
        assert(updated.velocity.y < 0.0f);

        std::cout << "[PASS] Test_GravityIntegration\n";
        return true;
    }

    // Test 4: Collision Detection - Sphere-Sphere
    bool Test_SphereSphereCollision()
    {
        PhysicsWorld world;

        // Two spheres that overlap
        RigidBody sphere1{};
        sphere1.mass = 1.0f;
        sphere1.position = {0.0f, 0.0f, 0.0f};
        sphere1.radius = 0.5f;
        sphere1.shape = ShapeType::Sphere;

        RigidBody sphere2{};
        sphere2.mass = 1.0f;
        sphere2.position = {0.8f, 0.0f, 0.0f}; // Overlapping
        sphere2.radius = 0.5f;
        sphere2.shape = ShapeType::Sphere;

        uint32_t id1 = world.AddBody(sphere1);
        uint32_t id2 = world.AddBody(sphere2);

        // Step to generate contacts
        world.Step(0.016f);

        // After collision resolution, spheres should separate
        RigidBody updated1{}, updated2{};
        world.GetBody(id1, updated1);
        world.GetBody(id2, updated2);

        float distance = (updated2.position - updated1.position).Length();
        assert(distance >= 0.99f); // Should be approximately 1.0 (sum of radii)

        std::cout << "[PASS] Test_SphereSphereCollision\n";
        return true;
    }

    // Test 5: Torque and Angular Velocity
    bool Test_TorqueApplication()
    {
        PhysicsWorld world;

        RigidBody body{};
        body.mass = 1.0f;
        body.position = {0.0f, 0.0f, 0.0f};
        body.angular_velocity = {0.0f, 0.0f, 0.0f};

        uint32_t id = world.AddBody(body);

        // Apply torque around Y axis
        world.ApplyTorque(id, {0.0f, 5.0f, 0.0f});

        // Step simulation
        world.Step(0.016f);

        RigidBody updated{};
        world.GetBody(id, updated);

        // Should have angular velocity around Y
        assert(updated.angular_velocity.y > 0.0f);

        std::cout << "[PASS] Test_TorqueApplication\n";
        return true;
    }

    // Test 6: Distance Constraint
    bool Test_DistanceConstraint()
    {
        PhysicsWorld world;

        RigidBody body1{};
        body1.mass = 1.0f;
        body1.position = {0.0f, 0.0f, 0.0f};

        RigidBody body2{};
        body2.mass = 1.0f;
        body2.position = {2.0f, 0.0f, 0.0f};

        uint32_t id1 = world.AddBody(body1);
        uint32_t id2 = world.AddBody(body2);

        // Add distance constraint: rest length = 1.0
        world.AddDistanceConstraint(id1, id2, {0, 0, 0}, {0, 0, 0}, 1.0f, 100.0f, 5.0f, 100.0f, false);

        // Step multiple times
        for (int i = 0; i < 60; ++i)
        {
            world.Step(0.016f);
        }

        RigidBody updated1{}, updated2{};
        world.GetBody(id1, updated1);
        world.GetBody(id2, updated2);

        float distance = (updated2.position - updated1.position).Length();
        // Should converge to rest length
        assert(NearEqual(distance, 1.0f, 0.1f));

        std::cout << "[PASS] Test_DistanceConstraint\n";
        return true;
    }

    // Test 7: Static Body (should not move)
    bool Test_StaticBody()
    {
        PhysicsWorld world;
        PhysicsConfig config{};
        config.gravity = {0.0f, -9.81f, 0.0f};
        world.SetConfig(config);

        RigidBody body{};
        body.mass = 1.0f;
        body.position = {0.0f, 5.0f, 0.0f};
        body.velocity = {0.0f, 0.0f, 0.0f};
        body.is_static = true;

        uint32_t id = world.AddBody(body);

        // Step simulation
        for (int i = 0; i < 10; ++i)
        {
            world.Step(0.016f);
        }

        RigidBody updated{};
        world.GetBody(id, updated);

        // Position should not change
        assert(Vec3NearEqual(updated.position, {0.0f, 5.0f, 0.0f}));
        assert(Vec3NearEqual(updated.velocity, {0.0f, 0.0f, 0.0f}));

        std::cout << "[PASS] Test_StaticBody\n";
        return true;
    }

    // Test 8: Energy Conservation (no damping, no gravity)
    bool Test_EnergyConservation()
    {
        PhysicsWorld world;
        PhysicsConfig config{};
        config.gravity = {0.0f, 0.0f, 0.0f};
        config.base_dt = 0.01f;
        world.SetConfig(config);

        RigidBody body{};
        body.mass = 1.0f;
        body.position = {0.0f, 0.0f, 0.0f};
        body.velocity = {10.0f, 0.0f, 0.0f};
        body.linear_damping = 0.0f;
        body.angular_damping = 0.0f;
        body.is_static = false;

        uint32_t id = world.AddBody(body);

        float initialKE = 0.5f * body.mass * body.velocity.LengthSq();

        // Step multiple times
        for (int i = 0; i < 100; ++i)
        {
            world.Step(0.01f);
        }

        RigidBody updated{};
        world.GetBody(id, updated);

        float finalKE = 0.5f * updated.mass * updated.velocity.LengthSq();

        // Kinetic energy should be conserved (within tolerance)
        assert(NearEqual(initialKE, finalKE, 0.5f));

        std::cout << "[PASS] Test_EnergyConservation\n";
        return true;
    }

    // Test 9: Raycast
    bool Test_Raycast()
    {
        PhysicsWorld world;

        RigidBody body{};
        body.mass = 1.0f;
        body.position = {0.0f, 0.0f, 10.0f};
        body.radius = 1.0f;
        body.shape = ShapeType::Sphere;

        world.AddBody(body);

        PhysicsWorld::RaycastHit hit;
        bool hitDetected = world.Raycast({0, 0, 0}, {0, 0, 1}, 20.0f, hit);

        assert(hitDetected);
        assert(hit.body_id > 0);
        assert(hit.distance < 10.0f && hit.distance > 8.0f);

        std::cout << "[PASS] Test_Raycast\n";
        return true;
    }

    // Test 10: Box-Box Collision
    bool Test_BoxBoxCollision()
    {
        PhysicsWorld world;

        RigidBody box1{};
        box1.mass = 1.0f;
        box1.position = {0.0f, 0.0f, 0.0f};
        box1.shape = ShapeType::Box;
        box1.half_extents = {0.5f, 0.5f, 0.5f};

        RigidBody box2{};
        box2.mass = 1.0f;
        box2.position = {0.8f, 0.0f, 0.0f}; // Overlapping
        box2.shape = ShapeType::Box;
        box2.half_extents = {0.5f, 0.5f, 0.5f};

        uint32_t id1 = world.AddBody(box1);
        uint32_t id2 = world.AddBody(box2);

        // Step to generate and resolve contacts
        for (int i = 0; i < 10; ++i)
        {
            world.Step(0.016f);
        }

        RigidBody updated1{}, updated2{};
        world.GetBody(id1, updated1);
        world.GetBody(id2, updated2);

        float distance = (updated2.position - updated1.position).Length();
        // Boxes should separate
        assert(distance > 0.8f);

        std::cout << "[PASS] Test_BoxBoxCollision\n";
        return true;
    }

    // Test 11: Determinism Test
    bool Test_Determinism()
    {
        std::vector<Vec3> positions1, positions2;

        // Run 1
        {
            PhysicsWorld world;
            PhysicsConfig config{};
            config.noise_seed = 12345;
            config.gravity = {0.0f, -9.81f, 0.0f};
            world.SetConfig(config);

            RigidBody body{};
            body.mass = 1.0f;
            body.position = {0.0f, 10.0f, 0.0f};
            uint32_t id = world.AddBody(body);

            for (int i = 0; i < 60; ++i)
            {
                world.Step(0.016f);
                RigidBody updated{};
                world.GetBody(id, updated);
                positions1.push_back(updated.position);
            }
        }

        // Run 2 (same seed)
        {
            PhysicsWorld world;
            PhysicsConfig config{};
            config.noise_seed = 12345;
            config.gravity = {0.0f, -9.81f, 0.0f};
            world.SetConfig(config);

            RigidBody body{};
            body.mass = 1.0f;
            body.position = {0.0f, 10.0f, 0.0f};
            uint32_t id = world.AddBody(body);

            for (int i = 0; i < 60; ++i)
            {
                world.Step(0.016f);
                RigidBody updated{};
                world.GetBody(id, updated);
                positions2.push_back(updated.position);
            }
        }

        // Compare results
        assert(positions1.size() == positions2.size());
        for (size_t i = 0; i < positions1.size(); ++i)
        {
            assert(Vec3NearEqual(positions1[i], positions2[i], 1e-6f));
        }

        std::cout << "[PASS] Test_Determinism\n";
        return true;
    }

    // Test 12: Sleep State
    bool Test_SleepState()
    {
        PhysicsWorld world;
        PhysicsConfig config{};
        config.sleep_time = 0.5f;
        config.sleep_linear_threshold = 0.01f;
        config.sleep_angular_threshold = 0.01f;
        world.SetConfig(config);

        RigidBody body{};
        body.mass = 1.0f;
        body.position = {0.0f, 0.0f, 0.0f};
        body.velocity = {0.001f, 0.0f, 0.0f}; // Very slow
        body.linear_damping = 0.9f;

        uint32_t id = world.AddBody(body);

        // Step for more than sleep time
        for (int i = 0; i < 60; ++i)
        {
            world.Step(0.016f);
        }

        RigidBody updated{};
        world.GetBody(id, updated);

        // Body should eventually sleep
        assert(updated.is_sleeping);

        std::cout << "[PASS] Test_SleepState\n";
        return true;
    }

    // Performance Test: Many Bodies
    bool Test_Performance_ManyBodies()
    {
        PhysicsWorld world;

        const int bodyCount = 100;
        std::vector<uint32_t> ids;

        auto start = std::chrono::high_resolution_clock::now();

        // Create bodies
        for (int i = 0; i < bodyCount; ++i)
        {
            RigidBody body{};
            body.mass = 1.0f;
            body.position = {(float)(i % 10) * 2.0f, (float)(i / 10) * 2.0f, 0.0f};
            body.radius = 0.5f;
            ids.push_back(world.AddBody(body));
        }

        // Step simulation
        for (int i = 0; i < 60; ++i)
        {
            world.Step(0.016f);
        }

        auto end = std::chrono::high_resolution_clock::now();
        auto duration = std::chrono::duration_cast<std::chrono::milliseconds>(end - start);

        std::cout << "[PERF] Test_Performance_ManyBodies: " << bodyCount
                  << " bodies, 60 steps = " << duration.count() << "ms\n";

        // Should complete in reasonable time (< 1000ms)
        assert(duration.count() < 1000);

        std::cout << "[PASS] Test_Performance_ManyBodies\n";
        return true;
    }

    void RunAllTests()
    {
        std::cout << "=== Physics Engine Test Suite ===\n\n";

        int passed = 0;
        int total = 0;

        auto runTest = [&](bool (*testFunc)(), const char *name)
        {
            total++;
            try
            {
                if (testFunc())
                {
                    passed++;
                }
                else
                {
                    std::cout << "[FAIL] " << name << "\n";
                }
            }
            catch (const std::exception &e)
            {
                std::cout << "[ERROR] " << name << ": " << e.what() << "\n";
            }
        };

        runTest(Test_BodyCreationAndRetrieval, "BodyCreationAndRetrieval");
        runTest(Test_ForceApplication, "ForceApplication");
        runTest(Test_GravityIntegration, "GravityIntegration");
        runTest(Test_SphereSphereCollision, "SphereSphereCollision");
        runTest(Test_TorqueApplication, "TorqueApplication");
        runTest(Test_DistanceConstraint, "DistanceConstraint");
        runTest(Test_StaticBody, "StaticBody");
        runTest(Test_EnergyConservation, "EnergyConservation");
        runTest(Test_Raycast, "Raycast");
        runTest(Test_BoxBoxCollision, "BoxBoxCollision");
        runTest(Test_Determinism, "Determinism");
        runTest(Test_SleepState, "SleepState");
        runTest(Test_Performance_ManyBodies, "Performance_ManyBodies");

        std::cout << "\n=== Test Results ===\n";
        std::cout << "Passed: " << passed << "/" << total << "\n";
        std::cout << "Failed: " << (total - passed) << "/" << total << "\n";

        if (passed == total)
        {
            std::cout << "\n✓ ALL TESTS PASSED\n";
        }
        else
        {
            std::cout << "\n✗ SOME TESTS FAILED\n";
        }
    }

} // namespace NativeEngine::Physics::Tests

// Main entry point for standalone test exe
#ifndef UNITY_BUILD
int main()
{
    NativeEngine::Physics::Tests::RunAllTests();
    return 0;
}
#endif
