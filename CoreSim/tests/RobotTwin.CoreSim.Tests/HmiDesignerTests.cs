using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[hmi-designer] The operator panel laid out on a screen, saved with the world; its alarms come, go and are acknowledged.</summary>
    public class HmiDesignerTests
    {
        [Fact]
        public void APanelMadeBeforeTheDesignerIsLaidOutInRowsOnItsScreen()
        {
            var panel = new HmiPanelSettings();
            var placed = new HmiWidget { Kind = HmiWidget.Trend, X = 20, Y = 20, Width = 360, Height = 200 };
            var widgets = new List<HmiWidget> { placed };
            for (int i = 0; i < 9; i++) widgets.Add(new HmiWidget { Kind = i % 2 == 0 ? HmiWidget.Lamp : HmiWidget.Bar, Variable = "V" + i });
            HmiLayout.Place(widgets, panel);

            Assert.Equal(20, placed.X);   // what was placed stays
            Assert.All(widgets, w => Assert.True(w.Width > 0 && w.Height > 0));
            Assert.All(widgets.Skip(1), w => Assert.True(w.Y >= placed.Y + placed.Height, "below what was placed"));
            Assert.All(widgets, w => Assert.True(w.X + w.Width <= panel.Width, "inside the screen's width"));
            for (int i = 0; i < widgets.Count; i++)
                for (int j = i + 1; j < widgets.Count; j++)
                {
                    var a = widgets[i];
                    var b = widgets[j];
                    bool apart = a.X + a.Width <= b.X || b.X + b.Width <= a.X || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y;
                    Assert.True(apart, $"{i} and {j} overlap");
                }

            Assert.Equal(16, HmiLayout.Snap(13, 8));
            Assert.Equal(13.3, HmiLayout.Snap(13.3, 0));
        }

        [Fact]
        public void TheLayoutRolesAlarmsAndScreenAreSavedWithTheWorld()
        {
            var motor = new HmiWidget { Kind = HmiWidget.Motor, Plc = "O1", Label = "Konveyör", X = 40, Y = 64, Width = 220, Height = 150 };
            motor.SetRole(HmiWidget.RoleStart, "Start");
            motor.SetRole(HmiWidget.RoleRunning, "Motor");
            var trend = new HmiWidget { Kind = HmiWidget.Trend, Plc = "O1", Variable = "Seviye", Seconds = 60 };
            trend.SetRole("2", "Debi");
            var w = new WorldDocument
            {
                Hmi = new List<HmiWidget> { motor, trend },
                HmiPanel = new HmiPanelSettings { Width = 1024, Height = 600, Grid = 4 },
                HmiAlarms = new List<HmiAlarm> { new HmiAlarm { Plc = "O1", Variable = "Seviye", Level = HmiAlarm.HighHigh, Condition = ">=", Threshold = 95, Text = "Tank taşıyor" } },
            };
            var again = WorldDocument.FromJson(w.ToJson());
            var m = again.Hmi![0];
            Assert.Equal((40.0, 64.0, 220.0, 150.0), (m.X, m.Y, m.Width, m.Height));
            Assert.Equal("Start", m.Role(HmiWidget.RoleStart));
            Assert.Equal("Motor", m.Role(HmiWidget.RoleRunning));
            Assert.Equal(string.Empty, m.Role(HmiWidget.RoleFault));
            Assert.Equal(new[] { "Seviye", "Debi" }, again.Hmi[1].Series());
            Assert.Equal(60, again.Hmi[1].Seconds);
            Assert.Equal((1024.0, 600.0, 4.0), (again.HmiPanel!.Width, again.HmiPanel.Height, again.HmiPanel.Grid));
            var alarm = Assert.Single(again.HmiAlarms!);
            Assert.Equal(("Seviye", HmiAlarm.HighHigh, 95.0, "Tank taşıyor"), (alarm.Variable, alarm.Level, alarm.Threshold, alarm.Text));

            motor.SetRole(HmiWidget.RoleStart, " ");
            motor.SetRole(HmiWidget.RoleRunning, "");
            Assert.Null(motor.Roles);   // nothing left: nothing saved
            Assert.Null(WorldDocument.FromJson(new WorldDocument().ToJson()).HmiAlarms);
        }

        [Fact]
        public void AlarmsComeGoAndStayUntilAcknowledged()
        {
            var high = new HmiAlarm { Plc = "P", Variable = "Seviye", Level = HmiAlarm.High, Condition = ">=", Threshold = 80, Text = "Seviye yüksek" };
            var hh = new HmiAlarm { Plc = "P", Variable = "Seviye", Level = HmiAlarm.HighHigh, Condition = ">", Threshold = 95 };
            var stop = new HmiAlarm { Plc = "P", Variable = "Acil", Level = HmiAlarm.Message, Condition = "=", Threshold = 1, Text = "Acil stop" };
            var alarms = new[] { high, hh, stop };
            var values = new Dictionary<string, double> { ["Seviye"] = 50, ["Acil"] = 0 };
            var monitor = new HmiAlarmMonitor();
            double Read(string plc, string v) => values.TryGetValue(v, out var x) ? x : double.NaN;

            monitor.Update(alarms, Read, 0);
            Assert.Empty(monitor.Shown);

            values["Seviye"] = 97;
            monitor.Update(alarms, Read, 1);
            Assert.Equal(2, monitor.ActiveCount);
            Assert.Equal(new[] { hh, high }, monitor.Shown.Select(e => e.Alarm));   // the more severe first
            monitor.Update(alarms, Read, 1.5);
            Assert.Equal(2, monitor.History.Count);   // still the same two occurrences

            values["Seviye"] = 60;
            monitor.Update(alarms, Read, 2);
            Assert.Equal(0, monitor.ActiveCount);
            Assert.Equal(2, monitor.Shown.Count());   // gone, but not seen yet
            monitor.Acknowledge(monitor.Shown.First(), 3);
            Assert.Single(monitor.Shown);
            monitor.AcknowledgeAll(4);
            Assert.Empty(monitor.Shown);
            Assert.Equal(2, monitor.History.Count);

            values["Acil"] = 1;
            monitor.Update(alarms, Read, 5);
            var e = Assert.Single(monitor.Shown);
            Assert.Equal("Acil stop", e.Alarm.Describe());
            Assert.True(e.Active);
            monitor.Acknowledge(e, 6);
            Assert.Single(monitor.Shown);   // acknowledged but still active: stays in the list
            Assert.False(new HmiAlarm { Condition = ">=", Threshold = 1 }.Holds(double.NaN));   // an unreadable variable raises nothing
            monitor.Reset();
            Assert.Empty(monitor.History);
        }
    }
}
