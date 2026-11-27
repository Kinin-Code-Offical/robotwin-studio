using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RobotTwin.CoreSim.Design;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [integrity] The shared part library is read while parts join it (a project's own parts, the SDK's): readers never
    /// see a collection being changed, and the parts keep the order they came in.
    /// </summary>
    public class PartLibraryConcurrencyTests
    {
        [Fact]
        public void ReadersNeverMeetAChangingLibrary()
        {
            var lib = new PartLibrary();
            StandardParts.Register(lib);
            var template = lib.Get("resistor");
            int before = lib.Parts.Count();
            var firstIds = lib.Parts.Take(5).Select(p => p.Id).ToList();
            Exception? failure = null;
            using var stop = new CancellationTokenSource();
            var reader = Task.Run(() =>
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        _ = lib.Parts.Count(p => p.Pins.Count > 0);
                        _ = lib.Presets.Select(p => p.Category).Distinct().Count();
                        _ = lib.Categories.Count();
                        _ = lib.Find("resistor");
                        _ = lib.FindPreset("res-1000");
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            for (int i = 0; i < 300; i++)
            {
                var part = new PartDefinition { Id = $"test-part-{i}", Name = template.Name, Category = template.Category, Symbol = template.Symbol };
                lib.Add(part);
                lib.AddPreset(new PartPreset($"test-preset-{i}", part.Name, part.Id, "Test", string.Empty, new Dictionary<string, string>()));
                if (i % 3 == 0) lib.RemovePreset($"test-preset-{i}");
            }

            stop.Cancel();
            reader.Wait();
            Assert.Null(failure);
            Assert.Equal(before + 300, lib.Parts.Count());
            Assert.Equal(firstIds, lib.Parts.Take(5).Select(p => p.Id).ToList());   // the order stays
            Assert.Equal("test-part-299", lib.Parts.Last().Id);
            Assert.Null(lib.FindPreset("test-preset-3"));
            Assert.NotNull(lib.FindPreset("test-preset-4"));
        }
    }
}
