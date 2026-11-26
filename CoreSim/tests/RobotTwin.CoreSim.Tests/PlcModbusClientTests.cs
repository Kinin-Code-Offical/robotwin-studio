using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[mbclient] A PLC as a Modbus client on the plant network: its MB_READ / MB_WRITE blocks drive a remote I/O module.</summary>
    public class PlcModbusClientTests
    {
        private readonly ITestOutputHelper _out;

        public PlcModbusClientTests(ITestOutputHelper output) => _out = output;

        private const string Program =
            "VAR\n" +
            "    Yaz : MB_WRITE;\n" +
            "    Oku : MB_READ;\n" +
            "    Hatali : MB_READ;\n" +
            "    Yok : MB_READ;\n" +
            "    Analog : MB_WRITE;\n" +
            "    Adim : INT := 0;\n" +
            "    Geri AT %MW0 : INT;\n" +
            "    Kod AT %MW1 : INT;\n" +
            "    Sessiz AT %MW2 : INT;\n" +
            "    Bitti AT %MW3 : INT;\n" +
            "END_VAR\n" +
            "\n" +
            "CASE Adim OF\n" +
            "    0:  Yaz(REQ := TRUE, UNIT := 2, FC := 5, ADDR := 0, VALUE := 1);     (* DO1 on *)\n" +
            "        IF Yaz.DONE THEN Adim := 1; END_IF\n" +
            "    1:  Adim := 2;                                                       (* a scan for DO1 to reach DI3 *)\n" +
            "    2:  Oku(REQ := TRUE, UNIT := 2, FC := 2, ADDR := 2);                 (* DI3 back *)\n" +
            "        IF Oku.DONE THEN Geri := Oku.VALUE; Adim := 3; END_IF\n" +
            "    3:  Hatali(REQ := TRUE, UNIT := 2, FC := 3, ADDR := 100);           (* no such register *)\n" +
            "        IF Hatali.ERROR THEN Kod := Hatali.STATUS; Adim := 4; END_IF\n" +
            "    4:  Yok(REQ := TRUE, UNIT := 9, FC := 3, ADDR := 0);                (* nothing at unit 9 *)\n" +
            "        IF Yok.ERROR THEN Sessiz := Yok.STATUS; Adim := 5; END_IF\n" +
            "    5:  Analog(REQ := TRUE, UNIT := 2, FC := 6, ADDR := 1, VALUE := 2500); (* AO1 = 2.5 V *)\n" +
            "        IF Analog.DONE THEN Bitti := 1; Adim := 6; END_IF\n" +
            "END_CASE\n";

        [Fact]
        public void ThePlcDrivesARemoteModuleOverThePlantNetwork()
        {
            var lib = PartLibrary.Default;
            var world = new WorldDocument();
            var plc = world.AddComponent(lib, "plc-st-8di8do", new WorldPose { X = -0.3 });
            plc.Parameters["program"] = Program;
            var io = world.AddComponent(lib, "modbus-io-8di8do", new WorldPose { X = 0.3 });
            io.Parameters["unit"] = "2";
            io.Parameters["port"] = "0";
            world.Connect(new CableEnd { ObjectId = io.Id, Pin = "DO1" }, new CableEnd { ObjectId = io.Id, Pin = "DI3" });
            using var runner = new WorldRunner(world, new RunnerOptions { Firmware = false });
            var p = runner.Physics!.PlcOf(plc.Id)!;
            Assert.True(p.Errors.Count == 0, string.Join("; ", p.Errors));
            while (runner.Time < 0.6) runner.Step(0.01);

            var memory = p.Program!.Memory;
            var device = runner.Physics.ModbusDevices.Single(d => d.Plc == null);
            bool coil;
            ushort ao;
            lock (device.Image.Gate)
            {
                coil = device.Image.Coils[0];
                ao = device.Image.HoldingRegisters[1];
            }

            _out.WriteLine($"step {p.Program.Read("ADIM")}: DI3 back {memory[0]}, bad address → {memory[1]}, unit 9 → {memory[2]}, done {memory[3]}; coil 0 {coil}, AO1 {ao} mV ({WorldPhysics.AnalogOutputVolts(device, 0):0.000} V)");
            Assert.True(coil);
            Assert.Equal(1, memory[0]);    // DO1 came back on DI3 through the cable
            Assert.Equal(2, memory[1]);    // illegal data address
            Assert.Equal(11, memory[2]);   // no answer: gateway target failed to respond
            Assert.Equal(1, memory[3]);
            Assert.Equal(2500, ao);
            Assert.InRange(WorldPhysics.AnalogOutputVolts(device, 0), 2.495, 2.505);
        }
    }
}
