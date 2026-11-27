using System;
using System.Linq;
using RobotTwin.CoreSim.Design;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [datasheet] Every part number the library compares with its datasheet passes: the badge in the library and this
    /// test read the same checks (Design/DatasheetChecks.cs), so what the app shows is what is tested.
    /// </summary>
    public class DatasheetCheckTests
    {
        private readonly ITestOutputHelper _out;

        public DatasheetCheckTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void EveryPartMatchesItsDatasheet()
        {
            foreach (string part in DatasheetChecks.Parts)
            {
                var results = DatasheetChecks.Results(part);
                Assert.NotEmpty(results);
                foreach (var r in results)
                {
                    var c = r.Check;
                    string Fmt(double v) => double.IsInfinity(v) ? "—" : (v / c.Scale).ToString("0.###");
                    _out.WriteLine($"{part,-8} {c.Quantity}: model {Fmt(r.Value)} {c.Unit} (datasheet typ. {(double.IsNaN(c.Typical) ? "—" : Fmt(c.Typical))}, {Fmt(c.Low)} … {Fmt(c.High)}) {(r.Pass ? "ok" : "OUT")}");
                }

                Assert.All(results, r => Assert.True(r.Pass, $"{part}: {r.Check.Quantity} = {r.Value}"));
            }
        }

        [Fact]
        public void APartWithoutChecksHasNone()
        {
            Assert.Empty(DatasheetChecks.For("res-10000"));
            Assert.Contains("lm7805", DatasheetChecks.Parts);
        }
    }
}
