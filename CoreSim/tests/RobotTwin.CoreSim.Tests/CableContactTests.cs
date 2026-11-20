using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    public class CableContactTests
    {
        private static (WorldDocument World, WorldCable Cable) MotorWorld()
        {
            var w = new WorldDocument();
            var bat = w.AddComponent(PartLibrary.Default, "bat-1s-lipo-300", new WorldPose());
            var motor = w.AddComponent(PartLibrary.Default, "motor-8520", new WorldPose { X = 0.1 });
            var c = w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = motor.Id, Pin = "A" }, 24);
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = motor.Id, Pin = "B" }, 24);
            c.Length = 0.20;
            return (w, c);
        }

        [Fact]
        public void PluggedJumperAddsTwoContactsToTheCopper()
        {
            var (w, c) = MotorWorld();
            var circuit = WorldCompiler.Compile(w, PartLibrary.Default);
            double copper = WorldCable.Resistance(24, 0.20);
            Assert.Equal(copper + 2 * WorldCable.PlugContactResistance, circuit.CableDevices[c.Id].Resistance, 9);
            Assert.InRange(copper, 0.015, 0.020);   // AWG 24: 84 mΩ/m
        }

        [Fact]
        public void SolderedEndsHaveNoPlugContact()
        {
            var (w, c) = MotorWorld();
            c.SolderedA = c.SolderedB = true;
            var circuit = WorldCompiler.Compile(w, PartLibrary.Default);
            Assert.Equal(WorldCable.Resistance(24, 0.20) + 2 * WorldCable.SolderJointResistance, circuit.CableDevices[c.Id].Resistance, 9);
        }

        [Fact]
        public void APulledPlugLoosensUntilItComesOut()
        {
            var c = new WorldCable();
            Assert.Equal(WorldCable.PlugContactResistance, c.EndContact(true, 0), 9);
            Assert.True(c.EndContact(true, 0.5) > 2 * WorldCable.PlugContactResistance);
            Assert.Equal(7 * WorldCable.PlugContactResistance, c.EndContact(true, 1), 9);
        }
    }
}
