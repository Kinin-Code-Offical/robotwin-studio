using System;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>Tests that measure the whole process (its memory): run alone, never beside another test.</summary>
    [CollectionDefinition("Alone", DisableParallelization = true)]
    public sealed class AloneCollection
    {
    }

    /// <summary>[perf] Long runs: what a world holds does not grow with the time it has run.</summary>
    [Collection("Alone")]
    public class LongRunTests
    {
        private readonly ITestOutputHelper _out;

        public LongRunTests(ITestOutputHelper output) => _out = output;

        private static long Heap()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return GC.GetTotalMemory(true);
        }

        [Fact]
        public void AMinuteOfAFactoryCellKeepsItsMemory()
        {
            // Robot, conveyor, sensors and pallet for a minute of world: after the first ten seconds the heap after a full
            // collection stays where it was, but for the boxes the conveyor has brought in since (a few kB).
            using var r = new WorldRunner(FactoryCell.Build(), new RunnerOptions { Firmware = false });
            r.Step(10);
            long at10 = Heap();
            for (int i = 0; i < 5; i++) r.Step(10);
            long at60 = Heap();
            _out.WriteLine($"heap after 10 s {at10 / 1024.0:0} kB, after 60 s {at60 / 1024.0:0} kB");
            Assert.True(at60 - at10 < 512 * 1024, $"the heap grew {(at60 - at10) / 1024.0:0} kB in 50 s of world");
        }
    }
}
