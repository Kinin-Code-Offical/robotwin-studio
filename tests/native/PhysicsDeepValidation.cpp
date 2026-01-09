// RobotWin Studio - Physics Engine Deep Validation Suite
// Extreme edge cases, numerical stability, and algorithm correctness
// Tests: collision response, constraint solving, numerical precision

#include <iostream>
#include <vector>
#include <cmath>
#include <chrono>
#include <string>
#include "../../NativeEngine/include/Physics/PhysicsWorld.h"
#include "../../NativeEngine/include/Physics/RigidBody.h"
#include "../../NativeEngine/include/Physics/PhysicsConfig.h"

// GLM includes might be needed for length/operations if not fully wrapped
#include "../../NativeEngine/include/Physics/MathTypes.h"

using namespace std;
using namespace NativeEngine::Physics;

// Platform-specific M_PI definition
#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

namespace NativeEngine::Physics::Validation
{

    struct TestResult
    {
        string name;
        bool passed;
        string details;
        double metric;
    };

    class PhysicsDeepValidator
    {
    private:
        vector<TestResult> results;
        float dt = 0.01f;

    public:
        // Test 1: Elastic collision energy conservation (1D)
        TestResult Test_ElasticCollision_EnergyConservation()
        {
            PhysicsWorld world;
            PhysicsConfig config;
            config.gravity = {0, 0, 0}; // Disable gravity to isolate collision
            config.restitution = 1.0f;  // Perfectly elastic
            config.dynamic_friction = 0.0f;
            config.static_friction = 0.0f;
            world.SetConfig(config);

            // Body 1: Moving right
            RigidBody body1;
            body1.id = 1;
            body1.mass = 1.0f;
            body1.SetMass(1.0f);
            body1.position = {-5.0f, 10.0f, 0.0f};
            body1.velocity = {10.0f, 0.0f, 0.0f};
            body1.restitution = 1.0f;
            body1.friction = 0.0f;
            body1.linear_damping = 0.0f;
            body1.drag_coefficient = 0.0f;
            body1.shape = ShapeType::Sphere;
            body1.radius = 0.5f;
            world.AddBody(body1);

            // Body 2: Stationary
            RigidBody body2;
            body2.id = 2;
            body2.mass = 1.0f;
            body2.SetMass(1.0f);
            body2.position = {0.0f, 10.0f, 0.0f};
            body2.velocity = {0.0f, 0.0f, 0.0f};
            body2.restitution = 1.0f;
            body2.friction = 0.0f;
            body2.linear_damping = 0.0f;
            body2.drag_coefficient = 0.0f;
            body2.shape = ShapeType::Sphere;
            body2.radius = 0.5f;
            world.AddBody(body2);

            // Initial Energy: 0.5 * m * v^2 = 0.5 * 1 * 100 = 50 J
            float initial_energy = 50.0f;

            // Run simulation until after collision
            for (int i = 0; i < 100; i++)
            {
                world.Step(dt);
            }

            world.GetBody(1, body1);
            world.GetBody(2, body2);

            // Final Energy
            float v1 = body1.velocity.Length();
            float v2 = body2.velocity.Length();
            float final_energy = 0.5f * body1.mass * v1 * v1 + 0.5f * body2.mass * v2 * v2;

            TestResult r;
            r.name = "Elastic Collision Energy Conservation";
            r.metric = std::abs(final_energy - initial_energy);
            r.passed = r.metric < 2.5f; // <5% error (allowing for integration drift)
            r.details = "Initial: " + to_string(initial_energy) + ", Final: " + to_string(final_energy);
            // results.push_back(r);
            return r;
        }

        // Test 2: Stacked boxes stability
        TestResult Test_StackedBoxes_NoToppling()
        {
            PhysicsWorld world;
            PhysicsConfig config;
            config.gravity = {0, -9.81f, 0};
            config.sleep_time = 0.1f; // Fast sleep
            world.SetConfig(config);

            // Ground Plane
            world.AddGroundPlane({0, 1, 0}, 0);

            // Stack 5 boxes
            int stack_height = 5;
            float box_size = 0.5f; // Half extents 0.5 -> Size 1.0

            for (int i = 0; i < stack_height; i++)
            {
                RigidBody box;
                box.id = i + 1;
                box.mass = 1.0f;
                box.SetMass(1.0f);
                box.position = {0, 1.0f + i * 1.0f + 0.01f, 0}; // Stack with slight gap
                box.shape = ShapeType::Box;
                box.half_extents = {box_size, box_size, box_size};
                box.friction = 0.5f;
                world.AddBody(box);
            }

            // Simulate for 3 seconds (300 steps)
            for (int i = 0; i < 300; i++)
            {
                world.Step(dt);
            }

            // Check top box position
            RigidBody top_box;
            world.GetBody(stack_height, top_box);

            float drift = std::sqrt(top_box.position.x * top_box.position.x + top_box.position.z * top_box.position.z);

            TestResult r;
            r.name = "Stacked Boxes Stability";
            r.metric = drift;
            r.passed = drift < 0.2f; // Should not drift much
            r.details = "Top box drift: " + to_string(drift) + "m";
            // results.push_back(r);
            return r;
        }

        // Test 3: Pendulum period accuracy (Constraint test)
        TestResult Test_Pendulum_PeriodAccuracy()
        {
            PhysicsWorld world;
            PhysicsConfig config;
            config.gravity = {0, -9.81f, 0};
            world.SetConfig(config);

            // Anchor (Static)
            RigidBody anchor;
            anchor.id = 1;
            anchor.is_static = true;
            anchor.position = {0, 5, 0};
            world.AddBody(anchor);

            // Bob (Dynamic)
            RigidBody bob;
            bob.id = 2;
            bob.mass = 1.0f;
            bob.SetMass(1.0f);
            // Start position
            bob.position = {0.1f, 5.0f - std::sqrt(1.0f - 0.01f), 0.0f};
            world.AddBody(bob);

            // Add Constraint
            // Local anchor A: (0,0,0) (Center of anchor body)
            // Local anchor B: (0,0,0)
            // Rest length: 1.0
            world.AddDistanceConstraint(1, 2, {0, 0, 0}, {0, 0, 0}, 1.0f, 10000.0f, 0.0f, 1e9f, false);

            // Just verify it swings and stays constrained
            // Distance check
            float max_stretch = 0;

            for (int i = 0; i < 500; i++)
            { // 5 seconds
                world.Step(dt);
                world.GetBody(2, bob);
                float len = (bob.position - anchor.position).Length();
                float stretch = std::abs(len - 1.0f);
                if (stretch > max_stretch)
                    max_stretch = stretch;
            }

            TestResult r;
            r.name = "Pendulum Constraint Enforcement";
            r.metric = max_stretch;
            r.passed = max_stretch < 0.05f; // <5cm stretch
            r.details = "Max stretch/compression: " + to_string(max_stretch);
            // results.push_back(r);
            return r;
        }

        // Test 4: Friction
        TestResult Test_Friction_Sliding()
        {
            PhysicsWorld world;
            PhysicsConfig config;
            config.gravity = {0, -9.81f, 0};
            config.dynamic_friction = 1.0f;
            config.static_friction = 0.0f;
            world.SetConfig(config);

            world.AddGroundPlane({0, 1, 0}, 0);

            RigidBody box;
            box.id = 1;
            box.SetMass(1.0f);
            box.position = {0, 0.51f, 0};
            box.linear_damping = 0.0f;
            box.drag_coefficient = 0.0f;
            box.velocity = {10.0f, 0.0, 0.0f}; // V0 = 10
            box.friction = 0.5f;               // mu = 0.5
            world.AddBody(box);

            // d = v^2 / (2 * mu * g) = 100 / (2 * 0.5 * 9.81) = 100 / 9.81 = 10.19m

            for (int i = 0; i < 300; i++)
            { // 3s
                world.Step(dt);
            }

            world.GetBody(1, box);
            float dist = box.position.x;

            TestResult r;
            r.name = "Friction Sliding Distance";
            r.metric = std::abs(dist - 10.19f);
            r.passed = box.velocity.x < 0.1f && std::abs(dist - 10.19f) < 2.0f; // Approx check
            r.details = "Distance: " + to_string(dist) + " (Expected ~10.2m)";
            // results.push_back(r);
            return r;
        }

        void RunAll()
        {
            cout << "=== Physics Deep Validation Suite ===" << endl;
            int passed = 0;

            auto run = [&](TestResult t)
            {
                cout << "[" << (t.passed ? "PASS" : "FAIL") << "] " << t.name << endl;
                cout << "       " << t.details << endl;
                if (t.passed)
                    passed++;
                results.push_back(t);
            };

            run(Test_ElasticCollision_EnergyConservation());
            run(Test_StackedBoxes_NoToppling());
            run(Test_Pendulum_PeriodAccuracy());
            run(Test_Friction_Sliding());

            cout << endl
                 << "Passed: " << passed << "/" << 4 << endl;
            if (passed != 4)
                exit(1);
        }
    };

} // namespace

int main()
{
    std::cerr << "Starting PhysicsDeepValidation..." << std::endl;
    NativeEngine::Physics::Validation::PhysicsDeepValidator validator;
    validator.RunAll();
    return 0;
}
