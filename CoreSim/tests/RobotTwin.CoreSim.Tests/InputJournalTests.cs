using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sandbox.Radio;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[replay] Live inputs recorded with their simulation times and fed back at exactly those times.</summary>
    public class InputJournalTests
    {
        [Fact]
        public void ControllerReportsAreStoredOnlyWhenTheyChange()
        {
            var j = new InputJournal();
            var s = new GamepadState { Throttle = 0 };
            Assert.True(j.AddPad(0.00, "O1", s));
            Assert.False(j.AddPad(0.02, "O1", s));   // same report: nothing new
            s.Throttle = 1023;
            Assert.True(j.AddPad(0.04, "O1", s));
            s.Throttle = 500;   // the journal kept its own copy
            Assert.Equal(1023, j.Events[1].Pad!.Throttle);
            j.Note(0.03, InputEvent.Serial, string.Empty, text: "ping\n");
            Assert.Equal(new[] { 0.00, 0.03, 0.04 }, j.Events.Select(e => e.Time).ToArray());   // kept in time order
            Assert.Contains("2 kumanda raporu", j.Summary());

            var back = InputJournal.FromJson(j.ToJson());
            Assert.Equal(3, back.Count);
            Assert.Equal(1023, back.Events[2].Pad!.Throttle);
            Assert.Equal("ping\n", back.Events[1].Text);
        }

        [Fact]
        public void TheReplayAppliesEachInputAtItsRecordedTime()
        {
            var w = WorldExamples.Ps5Plane(PartLibrary.Default);
            var padId = w.Objects.First(o => o.PartId == WirelessParts.GamepadId).Id;
            var journal = new InputJournal();
            journal.AddPad(0.10, padId, new GamepadState { AxisRX = 300, Throttle = 800 });
            journal.Note(0.37, InputEvent.GamepadPower, padId, 0);
            using var r = new WorldRunner(w, new RunnerOptions { Firmware = false });
            var pad = (GamepadNode)r.Radio!.Find(padId)!;
            var replay = new InputReplay(journal);
            replay.Start(r);
            replay.Step(r, 0.05);
            Assert.Equal(0, pad.Input.Throttle);   // not yet
            replay.Step(r, 0.25);                   // 0.05 → 0.30: the report at 0.10 lands inside the step
            Assert.Equal(800, pad.Input.Throttle);
            Assert.Equal(300, pad.Input.AxisRX);
            Assert.True(pad.PoweredOn);
            replay.Step(r, 0.05);                   // → 0.35
            Assert.True(pad.PoweredOn);
            replay.Step(r, 0.05);                   // → 0.40: switched off at 0.37
            Assert.False(pad.PoweredOn);
            Assert.Equal(2, replay.Applied);
            Assert.Empty(replay.Skipped);
            Assert.Equal(0.40, r.Time, 6);          // the step was split, not lengthened
        }
    }
}
