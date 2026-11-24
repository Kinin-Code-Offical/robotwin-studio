using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Tests;

/// <summary>[perf] The performance indicator's solver table: costs per simulated second, shares, the run's speed.</summary>
[Collection("Lang")]
public class SolverCostsTests
{
    private sealed class Run : ISolverCosts
    {
        public double SimulatedSeconds { get; set; }
        public double Circuit, Liquid;

        public void Costs(List<(string Name, double Seconds)> into)
        {
            into.Add(("devre", Circuit));
            into.Add(("sıvılar", Liquid));
            into.Add(("radyo", 0));
        }

        public string? Detail => null;
    }

    [Fact]
    public void TwoReadingsGiveEachSolversCostPerSimulatedSecondAndTheRunsSpeed()
    {
        var costs = new SolverCosts();
        var run = new Run { SimulatedSeconds = 1, Circuit = 0.1, Liquid = 0.5 };
        // The first reading of a run has nothing to compare with.
        var (rows, speed) = costs.Read(run, 10);
        Assert.Empty(rows);
        Assert.Equal(0, speed);
        // Half a simulated second in one second of clock: the liquid took 0.6 s of it, the circuit 0.2 s.
        run.SimulatedSeconds = 1.5;
        run.Circuit = 0.3;
        run.Liquid = 1.1;
        (rows, speed) = costs.Read(run, 11);
        Assert.Equal(0.5, speed, 6);
        Assert.Equal(2, rows.Count);   // a solver that took nothing is left out
        Assert.Equal("sıvılar", rows[0].Name);
        Assert.Equal(1200, rows[0].MsPerSimSecond, 6);
        Assert.Equal(0.75, rows[0].Share, 6);
        Assert.Equal("devre", rows[1].Name);
        Assert.Equal(400, rows[1].MsPerSimSecond, 6);
        // Paused: no rows; a new run starts over.
        (rows, _) = costs.Read(run, 12);
        Assert.Empty(rows);
        (rows, _) = costs.Read(new Run { SimulatedSeconds = 0.1, Circuit = 1 }, 13);
        Assert.Empty(rows);
        (rows, _) = costs.Read(null, 14);
        Assert.Empty(rows);
    }

    [Fact]
    public void EverySolverNameAndLabelHasItsEnglish()
    {
        Lang.Load("en");
        try
        {
            foreach (var text in SolverCosts.Names.Concat(SolverCosts.Labels))
                Assert.True(Lang.T(text) != text, $"\"{text}\" has no English");
        }
        finally
        {
            Lang.Load("tr");
        }
    }
}
