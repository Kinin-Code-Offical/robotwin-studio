using System.Collections.Generic;
using RobotTwin.CoreSim.Industrial;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[plc] Ladder diagrams: the classic circuits run as a PLC runs them.</summary>
    public class LadderTests
    {
        private static LdCell C(int row, int col, LdKind kind, string operand = "", string param = "", bool down = false) =>
            new LdCell { Row = row, Col = col, Kind = kind, Operand = operand, Param = param, LinkDown = down };

        private static StProgram Compile(LdProgram ld)
        {
            var p = ld.Compile(out var errors);
            Assert.True(p != null, string.Join("\n", errors) + "\n" + ld.ToStructuredText(out _));
            return p!;
        }

        /// <summary>
        /// The start/stop latch every electrician knows: Start, with Motor's own contact in parallel, in series with Stop's
        /// normally closed contact, drives Motor.
        /// <code>
        /// |--[Start]--+--[/Stop]--(Motor)--|
        /// |--[Motor]--+
        /// </code>
        /// </summary>
        [Fact]
        public void TheSealInLatchHoldsAfterStartAndDropsOnStop()
        {
            var ld = new LdProgram
            {
                Declarations = "VAR\n Start AT %IX0.0 : BOOL;\n Stop AT %IX0.1 : BOOL;\n Motor AT %QX0.0 : BOOL;\nEND_VAR",
                Rungs = new List<LdRung>
                {
                    new LdRung
                    {
                        Comment = "Motor tutma devresi",
                        Cells = { C(0, 0, LdKind.Contact, "Start", down: true), C(1, 0, LdKind.Contact, "Motor"), C(0, 1, LdKind.ContactNot, "Stop"), C(0, 2, LdKind.Coil, "Motor") }
                    }
                }
            };
            var p = Compile(ld);
            p.Scan(10);
            Assert.False(p.Outputs[0]);
            p.Inputs[0] = true;   // press Start
            p.Scan(10);
            Assert.True(p.Outputs[0]);
            p.Inputs[0] = false;   // let go: the motor's own contact holds it
            p.Scan(10);
            Assert.True(p.Outputs[0]);
            p.Inputs[1] = true;   // Stop
            p.Scan(10);
            Assert.False(p.Outputs[0]);
            p.Inputs[1] = false;
            p.Scan(10);
            Assert.False(p.Outputs[0]);   // stays off until Start again
        }

        [Fact]
        public void ATimerACounterAndAnEdgeWorkInRungs()
        {
            var ld = new LdProgram
            {
                Declarations = "VAR\n Go AT %IX0.0 : BOOL;\n Pulse AT %IX0.1 : BOOL;\n Clear AT %IX0.2 : BOOL;\n Late AT %QX0.0 : BOOL;\n Full AT %QX0.1 : BOOL;\n Level : INT := 70;\n High AT %QX0.2 : BOOL;\n Once AT %QX0.3 : BOOL;\nEND_VAR",
                Rungs = new List<LdRung>
                {
                    new LdRung { Cells = { C(0, 0, LdKind.Contact, "Go"), C(0, 1, LdKind.Ton, "T1", "T#200ms"), C(0, 2, LdKind.Coil, "Late") } },
                    new LdRung { Cells = { C(0, 0, LdKind.Contact, "Pulse"), new LdCell { Row = 0, Col = 1, Kind = LdKind.Ctu, Operand = "C1", Param = "3", Reset = "Clear" }, C(0, 2, LdKind.Coil, "Full") } },
                    new LdRung { Cells = { C(0, 0, LdKind.Compare, "Level", ">= 50"), C(0, 1, LdKind.Coil, "High") } },
                    new LdRung { Cells = { C(0, 0, LdKind.ContactRise, "Go"), C(0, 1, LdKind.Set, "Once") } }
                }
            };
            var p = Compile(ld);
            p.Inputs[0] = true;
            p.Scan(10);
            Assert.True(p.Outputs[3]);   // set on Go's rising edge, and stays
            for (int i = 0; i < 19; i++) p.Scan(10);   // 200 ms after the scan that saw Go
            Assert.False(p.Outputs[0]);
            p.Scan(10);
            Assert.True(p.Outputs[0]);   // 200 ms after Go
            for (int i = 0; i < 3; i++)
            {
                p.Inputs[1] = true;
                p.Scan(10);
                p.Inputs[1] = false;
                p.Scan(10);
            }

            Assert.True(p.Outputs[1]);   // three pulses
            p.Inputs[2] = true;
            p.Scan(10);
            Assert.False(p.Outputs[1]);   // cleared
            Assert.True(p.Outputs[2]);    // 70 >= 50
            Assert.True(p.Outputs[3]);
        }

        [Fact]
        public void ARungWithoutACoilOrAnOperandSaysWhich()
        {
            var ld = new LdProgram
            {
                Rungs = new List<LdRung>
                {
                    new LdRung { Cells = { C(0, 0, LdKind.Contact, "") , C(0, 1, LdKind.Coil, "X") } },
                    new LdRung { Cells = { C(0, 0, LdKind.Contact, "A") } }
                }
            };
            Assert.Null(ld.Compile(out var errors));
            Assert.Contains(errors, e => e.Line == 1 && e.Message.Contains("değişken"));
            Assert.Contains(errors, e => e.Line == 2 && e.Message.Contains("bobin yok"));
        }

        [Fact]
        public void ALadderSurvivesSavingAndLoading()
        {
            var ld = new LdProgram { Declarations = "VAR a : BOOL; END_VAR", Rungs = { new LdRung { Comment = "x", Cells = { C(0, 0, LdKind.Contact, "a", down: true), C(0, 1, LdKind.Coil, "a") } } } };
            var again = LdProgram.Load(ld.Save());
            Assert.Equal(ld.ToStructuredText(out _), again.ToStructuredText(out _));
            Assert.True(again.Rungs[0].Cells[0].LinkDown);
        }
    }
}
