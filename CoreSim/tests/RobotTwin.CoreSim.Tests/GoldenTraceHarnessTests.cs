using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RobotTwin.CoreSim.Host;
using RobotTwin.CoreSim.IPC;
using RobotTwin.CoreSim.Specs;
using Xunit;

namespace RobotTwin.CoreSim.Tests;

public class GoldenTraceHarnessTests
{
    [Fact]
    public void GoldenTraceFixtureReplaysDeterministically()
    {
        var trace = GoldenTrace.LoadFixture("golden_trace_v1.json");
        var firmware = new DeterministicFirmwareClient();
        var circuit = new CircuitSpec { Id = "golden-circuit", Mode = SimulationMode.Fast };
        var host = new SimHost(circuit, firmware, trace.DtSeconds);

        foreach (var step in trace.Steps)
        {
            var request = new FirmwareStepRequest
            {
                StepSequence = step.StepSequence,
                PinStates = step.Inputs ?? Array.Empty<int>()
            };

            var result = host.StepOnce(request);
            Assert.Equal(step.StepSequence, result.StepSequence);
            AssertPinsPrefix(step.ExpectedPins, result.PinStates);
        }
    }

    [Fact]
    public void GoldenTraceRecorderCanReplayAndDiff()
    {
        var inputs = new List<int[]>
        {
            new[] { 0, 1, 0, 1, 1, 0, 0, 1 },
            new[] { 1, 1, 1, 0, 0, 0, 1, 0 },
            new[] { 0, 0, 0, 0, 1, 1, 0, 0 },
            new[] { 1, 0, 1, 0, 1, 0, 1, 0 }
        };

        var baseline = RecordTrace(inputs, 0.02, new DeterministicFirmwareClient());
        var replay = ReplayTrace(baseline, new DeterministicFirmwareClient());
        var diffs = DiffTraces(baseline, replay);
        Assert.Empty(diffs);

        var skewed = ReplayTrace(baseline, new SkewedFirmwareClient());
        var skewedDiffs = DiffTraces(baseline, skewed);
        Assert.NotEmpty(skewedDiffs);
    }

    private static void AssertPinsPrefix(int[] expected, int[] actual)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        int count = Math.Min(expected.Length, actual.Length);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(expected[i], actual[i]);
        }
    }

    private static GoldenTrace RecordTrace(IReadOnlyList<int[]> inputs, double dtSeconds, IFirmwareClient firmware)
    {
        var trace = new GoldenTrace
        {
            Version = 1,
            DtSeconds = dtSeconds,
            Metadata = new Dictionary<string, string>
            {
                ["source"] = "deterministic_stub",
                ["recorded_utc"] = DateTime.UtcNow.ToString("O")
            }
        };

        var circuit = new CircuitSpec { Id = "golden-circuit", Mode = SimulationMode.Fast };
        var host = new SimHost(circuit, firmware, dtSeconds);

        for (int i = 0; i < inputs.Count; i++)
        {
            var request = new FirmwareStepRequest
            {
                StepSequence = (ulong)(i + 1),
                PinStates = inputs[i]
            };
            var result = host.StepOnce(request);
            trace.Steps.Add(new GoldenTraceStep
            {
                StepSequence = request.StepSequence,
                Inputs = request.PinStates ?? Array.Empty<int>(),
                ExpectedPins = (int[])result.PinStates.Clone()
            });
        }

        return trace;
    }

    private static GoldenTrace ReplayTrace(GoldenTrace baseline, IFirmwareClient firmware)
    {
        var trace = new GoldenTrace
        {
            Version = baseline.Version,
            DtSeconds = baseline.DtSeconds,
            Metadata = new Dictionary<string, string>(baseline.Metadata ?? new Dictionary<string, string>())
        };

        var circuit = new CircuitSpec { Id = "golden-circuit", Mode = SimulationMode.Fast };
        var host = new SimHost(circuit, firmware, baseline.DtSeconds);

        foreach (var step in baseline.Steps)
        {
            var request = new FirmwareStepRequest
            {
                StepSequence = step.StepSequence,
                PinStates = step.Inputs ?? Array.Empty<int>()
            };
            var result = host.StepOnce(request);
            trace.Steps.Add(new GoldenTraceStep
            {
                StepSequence = step.StepSequence,
                Inputs = request.PinStates ?? Array.Empty<int>(),
                ExpectedPins = (int[])result.PinStates.Clone()
            });
        }

        return trace;
    }

    private static List<string> DiffTraces(GoldenTrace baseline, GoldenTrace candidate)
    {
        var diffs = new List<string>();
        int stepCount = Math.Min(baseline.Steps.Count, candidate.Steps.Count);

        for (int stepIndex = 0; stepIndex < stepCount; stepIndex++)
        {
            var expected = baseline.Steps[stepIndex].ExpectedPins ?? Array.Empty<int>();
            var actual = candidate.Steps[stepIndex].ExpectedPins ?? Array.Empty<int>();
            int pinCount = Math.Min(expected.Length, actual.Length);
            for (int pin = 0; pin < pinCount; pin++)
            {
                if (expected[pin] != actual[pin])
                {
                    diffs.Add($"step={baseline.Steps[stepIndex].StepSequence} pin={pin} expected={expected[pin]} actual={actual[pin]}");
                }
            }
        }

        if (baseline.Steps.Count != candidate.Steps.Count)
        {
            diffs.Add($"step_count expected={baseline.Steps.Count} actual={candidate.Steps.Count}");
        }

        return diffs;
    }

    private sealed class DeterministicFirmwareClient : IFirmwareClient
    {
        public Task ConnectAsync() => Task.CompletedTask;
        public void Disconnect() { }

        public FirmwareStepResult Step(FirmwareStepRequest request)
        {
            var result = new FirmwareStepResult
            {
                StepSequence = request.StepSequence
            };

            Array.Fill(result.PinStates, -1);
            if (request.PinStates != null)
            {
                int count = Math.Min(request.PinStates.Length, 8);
                for (int i = 0; i < count; i++)
                {
                    result.PinStates[i] = request.PinStates[i] > 0 ? 1 : 0;
                }
            }

            result.PinStates[8] = (request.StepSequence % 2 == 0) ? 1 : 0;
            return result;
        }
    }

    private sealed class SkewedFirmwareClient : IFirmwareClient
    {
        public Task ConnectAsync() => Task.CompletedTask;
        public void Disconnect() { }

        public FirmwareStepResult Step(FirmwareStepRequest request)
        {
            var result = new FirmwareStepResult
            {
                StepSequence = request.StepSequence
            };

            Array.Fill(result.PinStates, -1);
            if (request.PinStates != null)
            {
                int count = Math.Min(request.PinStates.Length, 8);
                for (int i = 0; i < count; i++)
                {
                    result.PinStates[i] = request.PinStates[i] > 0 ? 1 : 0;
                }
            }

            if (request.StepSequence == 2)
            {
                result.PinStates[0] = result.PinStates[0] == 1 ? 0 : 1;
            }

            result.PinStates[8] = (request.StepSequence % 2 == 0) ? 1 : 0;
            return result;
        }
    }

    private sealed class GoldenTrace
    {
        public int Version { get; set; }
        public double DtSeconds { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
        public List<GoldenTraceStep> Steps { get; set; } = new();

        public static GoldenTrace LoadFixture(string name)
        {
            string baseDir = AppContext.BaseDirectory;
            string path = Path.Combine(baseDir, "Fixtures", name);
            if (!File.Exists(path))
            {
                path = Path.Combine(baseDir, name);
            }
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Fixture not found: {name}", path);
            }

            string json = File.ReadAllText(path);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var trace = JsonSerializer.Deserialize<GoldenTrace>(json, options);
            if (trace == null || trace.Steps == null || trace.Steps.Count == 0)
            {
                throw new InvalidOperationException("Golden trace fixture is empty.");
            }
            return trace;
        }
    }

    private sealed class GoldenTraceStep
    {
        public ulong StepSequence { get; set; }
        public int[] Inputs { get; set; } = Array.Empty<int>();
        public int[] ExpectedPins { get; set; } = Array.Empty<int>();
    }
}
