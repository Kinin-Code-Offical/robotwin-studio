using System;
using System.Linq;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[perf] The world's snapshot lookup answers as the document's own (slower) queries do.</summary>
    public class WorldLookupTests
    {
        [Fact]
        public void TheLookupFindsAndJudgesActivityAsTheDocumentDoes()
        {
            var rng = new Random(5);
            for (int trial = 0; trial < 30; trial++)
            {
                var w = new WorldDocument();
                var objects = Enumerable.Range(0, 40).Select(i => w.AddBody("box", 0.02, 0.02, 0.02, new WorldPose { X = 0.05 * i }, "PLA")).ToList();
                // mounted in chains and trees, some loops, a few joints naming nothing
                for (int k = 0; k < 45; k++)
                {
                    var a = objects[rng.Next(objects.Count)];
                    string? b = rng.Next(8) == 0 ? null : objects[rng.Next(objects.Count)].Id;
                    w.Joints.Add(new WorldJoint { Id = "J" + k, ObjectA = rng.Next(2) == 0 ? a.Id.ToLowerInvariant() : a.Id, ObjectB = b! });
                }

                foreach (var o in objects)
                    if (rng.Next(5) == 0) WorldBuilding.SetActive(o, false);
                var lookup = new WorldLookup(w);
                foreach (var o in objects)
                {
                    Assert.Same(w.Find(o.Id), lookup.Find(o.Id));
                    Assert.Same(w.Find(o.Id.ToLowerInvariant()), lookup.Find(o.Id.ToLowerInvariant()));
                    Assert.Equal(WorldBuilding.ActiveInHierarchy(w, o), lookup.ActiveInHierarchy(o));
                }

                Assert.Null(lookup.Find("no such"));
                Assert.Null(lookup.Find(null));
            }
        }
    }
}
