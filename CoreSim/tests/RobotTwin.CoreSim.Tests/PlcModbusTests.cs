using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Industrial;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[plc][modbus] A SCADA writes a set point into the PLC's memory over Modbus and reads the program's result back.</summary>
    public class PlcModbusTests
    {
        [Fact]
        public async Task AScadaWritesASetPointAndReadsTheProgramsAnswer()
        {
            var w = new WorldDocument();
            var plc = w.AddComponent(PartLibrary.Default, "plc-st-8di8do", new WorldPose());
            plc.Parameters["modbus"] = "açık";
            plc.Parameters["modbus_port"] = "0";
            plc.Parameters["program"] = "VAR sp AT %MW1 : INT; out AT %MW2 : INT; q AT %QX0.3 : BOOL; END_VAR\nout := sp * 2;\nq := sp > 10;";
            using var r = new WorldRunner(w, new RunnerOptions { Firmware = false });
            var device = Assert.Single(r.Physics!.ModbusDevices);
            Assert.NotNull(device.Plc);
            using var host = new ModbusHost(r.Physics.ModbusDevices);
            Assert.Empty(host.Problems);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, host.Servers[0].Port);
            var stream = client.GetStream();
            ushort tid = 0;

            async Task<byte[]> Ask(params int[] pdu)
            {
                tid++;
                var frame = new byte[7 + pdu.Length];
                frame[0] = (byte)(tid >> 8);
                frame[1] = (byte)tid;
                frame[5] = (byte)(pdu.Length + 1);
                frame[6] = 1;
                for (int i = 0; i < pdu.Length; i++) frame[7 + i] = (byte)pdu[i];
                await stream.WriteAsync(frame, 0, frame.Length);
                var header = new byte[7];
                for (int got = 0; got < 7;) got += await stream.ReadAsync(header, got, 7 - got);
                var reply = new byte[(header[4] << 8 | header[5]) - 1];
                for (int got = 0; got < reply.Length;) got += await stream.ReadAsync(reply, got, reply.Length - got);
                return reply;
            }

            await Ask(6, 0, 1, 0, 21);   // %MW1 := 21
            r.Step(0.05);   // a few scans
            Assert.Equal(new byte[] { 3, 4, 0, 21, 0, 42 }, await Ask(3, 0, 1, 0, 2));   // %MW1, %MW2
            Assert.Equal(new byte[] { 1, 1, 0b1000 }, await Ask(1, 0, 0, 0, 8));   // %QX0.3 on
            Assert.Equal(21, r.Physics.PlcOf(plc.Id)!.Program!.Read("SP"));
        }
    }
}
