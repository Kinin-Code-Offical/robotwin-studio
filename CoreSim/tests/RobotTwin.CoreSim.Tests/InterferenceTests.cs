using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[interference] SolidWorks' Interference Detection on a world: overlap volume, where, touching and joined parts.</summary>
    public class InterferenceTests
    {
        private static WorldObject Box(WorldDocument w, string name, double x, double y = 0, double z = 0.01, double yaw = 0) =>
            new WorldObject { Id = w.NextId("O"), Name = name, Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.02, SizeY = 0.02, SizeZ = 0.02, MassKg = 0.1, Pose = new WorldPose { X = x, Y = y, Z = z, Yaw = yaw } };

        [Fact]
        public void OverlappingBoxesReportTheirOverlapVolume()
        {
            var w = new WorldDocument();
            var a = Box(w, "A", 0);
            w.Objects.Add(a);
            var b = Box(w, "B", 0.015);   // 5 mm into A along x: 5 × 20 × 20 = 2000 mm³
            w.Objects.Add(b);
            var c = Box(w, "C", 0.1);     // far away
            w.Objects.Add(c);
            var found = Interference.Find(w, new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default), new InterferenceOptions { ResolutionMm = 0.25 });
            var pair = Assert.Single(found);
            Assert.Equal(new[] { a.Id, b.Id }.OrderBy(s => s), new[] { pair.A, pair.B }.OrderBy(s => s));
            Assert.InRange(pair.VolumeMm3, 2000 * 0.9, 2000 * 1.1);
            Assert.InRange(pair.Center.X, 0.0065, 0.0085);   // A spans x −10…10 mm, B 5…25 mm: the overlap 5…10 mm, its middle at 7.5 mm

            // turned 45°: still overlapping, a different volume
            b.Pose.Yaw = 45;
            Assert.Single(Interference.Find(w, new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default)));
        }

        [Fact]
        public void TouchingCountsOnlyWhenAskedAndJoinedPairsAreLeftOutByDefault()
        {
            var w = new WorldDocument();
            var a = Box(w, "A", 0);
            var b = Box(w, "B", 0.02);   // faces touch
            w.Objects.Add(a);
            w.Objects.Add(b);
            Assert.Empty(Interference.Find(w, new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default)));
            var touching = Interference.Find(w, new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default), new InterferenceOptions { TouchingCounts = true });
            Assert.Single(touching);

            b.Pose.X = 0.01;   // overlapping now, but joined
            w.Joints.Add(new WorldJoint { ObjectA = b.Id, ObjectB = a.Id });
            Assert.Empty(Interference.Find(w, new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default)));
            var all = Interference.Find(w, new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default), new InterferenceOptions { IncludeJoined = true });
            Assert.True(Assert.Single(all).Joined);
        }

        [Fact]
        public void PartsWeldedToOneBodyAreJoinedButNotThroughAMovingPort()
        {
            // [assembly-check] a ball caster and its post, each fixed to the plate: one solid in the physics, not a clash
            var w = new WorldDocument();
            WorldObject Add(WorldObject o)
            {
                w.Objects.Add(o);   // each in the world before the next takes an id
                return o;
            }

            var plate = Add(Box(w, "Plate", 0.2));
            var ball = Add(Box(w, "Ball", 0));
            var post = Add(Box(w, "Post", 0.01));   // 10 mm into the ball
            w.Joints.Add(new WorldJoint { ObjectA = ball.Id, ObjectB = plate.Id });
            w.Joints.Add(new WorldJoint { ObjectA = post.Id, ObjectB = plate.Id });
            var geo = new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default);
            Assert.Empty(Interference.Find(w, geo));
            Assert.True(Assert.Single(Interference.Find(w, geo, new InterferenceOptions { IncludeJoined = true })).Joined);

            // fixed to a moving port (a wheel on a shaft) the part turns against the rest: its overlap is reported
            w.Joints[0].PortB = "shaft";
            Assert.Single(Interference.Find(w, geo));
        }
    }
}
