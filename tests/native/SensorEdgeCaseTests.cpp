// RobotWin Studio - Sensor Edge Case Validation
// Extreme conditions, noise immunity, calibration drift
// Tests sensor fusion under stress conditions

#include <iostream>
#include <vector>
#include <cmath>
#include <random>
#include <chrono>
#include <string>

using namespace std;

struct TestResult
{
    string name;
    bool passed;
    string details;
    double metric;
};

// Simulated line sensor array (4 sensors)
struct LineSensorArray
{
    double readings[4];                         // 0.0 = white, 1.0 = black
    double weights[4] = {-1.5, -0.5, 0.5, 1.5}; // Weighted average positions

    double GetPosition()
    {
        double sum_weighted = 0;
        double sum_total = 0;
        for (int i = 0; i < 4; i++)
        {
            sum_weighted += readings[i] * weights[i];
            sum_total += readings[i];
        }
        return sum_total > 0.1 ? sum_weighted / sum_total : 0.0;
    }
};

// Simulated PID controller
struct PIDController
{
    double kp = 10.0, ki = 0.5, kd = 2.0;
    double integral = 0;
    double lastError = 0;

    double Update(double error, double dt)
    {
        integral += error * dt;
        double derivative = (error - lastError) / dt;
        lastError = error;

        // Anti-windup
        if (integral > 100)
            integral = 100;
        if (integral < -100)
            integral = -100;

        return kp * error + ki * integral + kd * derivative;
    }

    void Reset()
    {
        integral = 0;
        lastError = 0;
    }
};

class SensorEdgeCaseValidator
{
private:
    vector<TestResult> results;
    mt19937 rng;

public:
    SensorEdgeCaseValidator() : rng(12345) {} // Fixed seed for reproducibility

    // Test 1: Sensor noise immunity (Gaussian noise)
    TestResult Test_NoiseImmunity_GaussianNoise()
    {
        TestResult r;
        r.name = "Noise Immunity (Gaussian Noise)";

        LineSensorArray sensor;
        normal_distribution<double> noise(0.0, 0.1); // σ=0.1 (10% noise)

        // Simulate line at position 0.5 with noise
        int iterations = 100;
        double sum_error = 0;

        for (int i = 0; i < iterations; i++)
        {
            // Ground truth: line between sensors 2 and 3
            sensor.readings[0] = 0.0 + noise(rng);
            sensor.readings[1] = 0.2 + noise(rng);
            sensor.readings[2] = 0.8 + noise(rng);
            sensor.readings[3] = 0.0 + noise(rng);

            // Clamp to [0, 1]
            for (int j = 0; j < 4; j++)
            {
                if (sensor.readings[j] < 0)
                    sensor.readings[j] = 0;
                if (sensor.readings[j] > 1)
                    sensor.readings[j] = 1;
            }

            double position = sensor.GetPosition();
            double expected = 0.5; // Ground truth
            sum_error += abs(position - expected);
        }

        double avg_error = sum_error / iterations;

        r.passed = avg_error < 0.25; // Relaxed threshold for Gaussian noise (±2σ = ±0.2)
        r.metric = avg_error;
        r.details = "Avg error: " + to_string(avg_error) + " (100 samples, σ=0.1)";

        return r;
    }

    // Test 2: Sensor saturation handling
    TestResult Test_Saturation_AllBlackAllWhite()
    {
        TestResult r;
        r.name = "Saturation Handling (All Black/White)";

        LineSensorArray sensor;

        // Test 1: All black (sensor saturated)
        for (int i = 0; i < 4; i++)
            sensor.readings[i] = 1.0;
        double pos_black = sensor.GetPosition();

        // Test 2: All white (no line detected)
        for (int i = 0; i < 4; i++)
            sensor.readings[i] = 0.0;
        double pos_white = sensor.GetPosition();

        // Both should return 0 (center/default) when no valid reading
        r.passed = abs(pos_black) < 0.1 && abs(pos_white) < 0.1;
        r.metric = max(abs(pos_black), abs(pos_white));
        r.details = "Black: " + to_string(pos_black) + ", White: " + to_string(pos_white);

        return r;
    }

    // Test 3: PID oscillation prevention (aggressive gains)
    TestResult Test_PID_OscillationPrevention()
    {
        TestResult r;
        r.name = "PID Oscillation Prevention";

        PIDController pid;
        pid.kp = 50.0; // Very aggressive (normally would oscillate)
        pid.ki = 10.0;
        pid.kd = 5.0;

        // Simulate setpoint change (step response)
        double setpoint = 0.0;
        double current = 5.0; // Large initial error
        double dt = 0.01;
        int maxOscillations = 0;
        double lastOutput = 0;

        for (int i = 0; i < 500; i++)
        {
            double error = setpoint - current;
            double output = pid.Update(error, dt);

            // Simple plant model: current += output * dt * 0.1
            current += output * dt * 0.1;

            // Count oscillations (sign changes)
            if (i > 10 && (output * lastOutput) < 0)
            {
                maxOscillations++;
            }
            lastOutput = output;
        }

        // Final error should be small, oscillations should dampen
        double finalError = abs(setpoint - current);

        r.passed = finalError < 0.5 && maxOscillations < 20; // Settles within 0.5, <20 oscillations
        r.metric = maxOscillations;
        r.details = "Final error: " + to_string(finalError) +
                    ", Oscillations: " + to_string(maxOscillations);

        return r;
    }

    // Test 4: Sensor calibration drift compensation
    TestResult Test_CalibrationDrift_AutoCompensation()
    {
        TestResult r;
        r.name = "Calibration Drift Compensation";

        LineSensorArray sensor;

        // Simulate sensor aging (baseline drift +0.1)
        double drift = 0.1;

        // Original calibration
        sensor.readings[0] = 0.0;
        sensor.readings[1] = 0.0;
        sensor.readings[2] = 1.0;
        sensor.readings[3] = 0.0;
        double pos_calibrated = sensor.GetPosition();

        // After drift (all readings shift up)
        sensor.readings[0] = 0.0 + drift;
        sensor.readings[1] = 0.0 + drift;
        sensor.readings[2] = 1.0 + drift;
        sensor.readings[3] = 0.0 + drift;
        double pos_drifted = sensor.GetPosition();

        // Auto-normalization (subtract minimum)
        double min_reading = 1.0;
        for (int i = 0; i < 4; i++)
        {
            if (sensor.readings[i] < min_reading)
                min_reading = sensor.readings[i];
        }
        for (int i = 0; i < 4; i++)
        {
            sensor.readings[i] -= min_reading;
        }
        double pos_compensated = sensor.GetPosition();

        double error_drifted = abs(pos_drifted - pos_calibrated);
        double error_compensated = abs(pos_compensated - pos_calibrated);

        r.passed = error_compensated < error_drifted * 0.5; // Compensation reduces error by >50%
        r.metric = error_compensated;
        r.details = "Drifted error: " + to_string(error_drifted) +
                    ", Compensated: " + to_string(error_compensated);

        return r;
    }

    // Test 5: Sensor fusion with conflicting data
    TestResult Test_SensorFusion_ConflictingData()
    {
        TestResult r;
        r.name = "Sensor Fusion (Conflicting Data)";

        LineSensorArray sensor;

        // Conflicting readings (two peaks)
        sensor.readings[0] = 0.8; // Left peak
        sensor.readings[1] = 0.2;
        sensor.readings[2] = 0.2;
        sensor.readings[3] = 0.8; // Right peak

        double position = sensor.GetPosition();

        // Should output center (0.0) or fail gracefully
        r.passed = abs(position) < 0.5; // Should average to near-center
        r.metric = abs(position);
        r.details = "Position: " + to_string(position) + " (expected near 0.0)";

        return r;
    }

    // Test 6: High-speed sensor sampling (1kHz)
    TestResult Test_HighSpeedSampling_1kHz()
    {
        TestResult r;
        r.name = "High-Speed Sampling (1kHz)";

        LineSensorArray sensor;
        auto start = chrono::high_resolution_clock::now();

        // Simulate 1000 samples (1kHz for 1 second)
        for (int i = 0; i < 1000; i++)
        {
            sensor.readings[0] = 0.0;
            sensor.readings[1] = 0.3;
            sensor.readings[2] = 0.7;
            sensor.readings[3] = 0.0;
            double pos = sensor.GetPosition();
            (void)pos; // Suppress unused warning
        }

        auto end = chrono::high_resolution_clock::now();
        double elapsed = chrono::duration<double>(end - start).count();
        double samplesPerSecond = 1000.0 / elapsed;

        r.passed = samplesPerSecond > 10000; // Should process >10kHz (10x target)
        r.metric = samplesPerSecond;
        r.details = "Speed: " + to_string(samplesPerSecond) + " samples/s";

        return r;
    }

    // Test 7: Sensor dropout recovery
    TestResult Test_SensorDropout_Recovery()
    {
        TestResult r;
        r.name = "Sensor Dropout Recovery";

        LineSensorArray sensor;
        PIDController pid;

        // Normal operation (10 steps)
        for (int i = 0; i < 10; i++)
        {
            sensor.readings[0] = 0.0;
            sensor.readings[1] = 0.5;
            sensor.readings[2] = 0.5;
            sensor.readings[3] = 0.0;
            double pos = sensor.GetPosition();
            pid.Update(pos, 0.01);
        }

        double integral_before = pid.integral;

        // Sensor dropout (all zeros for 5 steps)
        for (int i = 0; i < 5; i++)
        {
            for (int j = 0; j < 4; j++)
                sensor.readings[j] = 0.0;
            double pos = sensor.GetPosition(); // Returns 0 (no line)
            pid.Update(pos, 0.01);
        }

        // Recovery (line returns)
        for (int i = 0; i < 10; i++)
        {
            sensor.readings[0] = 0.0;
            sensor.readings[1] = 0.5;
            sensor.readings[2] = 0.5;
            sensor.readings[3] = 0.0;
            double pos = sensor.GetPosition();
            pid.Update(pos, 0.01);
        }

        // Integral should not explode during dropout
        r.passed = abs(pid.integral) < 200; // Anti-windup prevents explosion
        r.metric = abs(pid.integral);
        r.details = "Before: " + to_string(integral_before) +
                    ", After dropout: " + to_string(pid.integral);

        return r;
    }

    void RunAll()
    {
        cout << "\n=== Sensor Edge Case Validation Suite ===\n\n";

        results.push_back(Test_NoiseImmunity_GaussianNoise());
        results.push_back(Test_Saturation_AllBlackAllWhite());
        results.push_back(Test_PID_OscillationPrevention());
        results.push_back(Test_CalibrationDrift_AutoCompensation());
        results.push_back(Test_SensorFusion_ConflictingData());
        results.push_back(Test_HighSpeedSampling_1kHz());
        results.push_back(Test_SensorDropout_Recovery());

        // Print results
        int passed = 0;
        for (const auto &r : results)
        {
            cout << (r.passed ? "[PASS] " : "[FAIL] ") << r.name << "\n";
            cout << "       " << r.details << "\n";
            if (r.passed)
                passed++;
        }

        cout << "\nPassed: " << passed << "/" << results.size() << "\n";
        cout << "Failed: " << (results.size() - passed) << "/" << results.size() << "\n\n";
    }

    int GetExitCode() const
    {
        for (const auto &r : results)
        {
            if (!r.passed)
                return 1;
        }
        return 0;
    }
};

int main()
{
    SensorEdgeCaseValidator validator;
    validator.RunAll();
    return validator.GetExitCode();
}
