using System.Linq;
using RobotTwin.CoreSim.Industrial;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[plc] The Structured Text interpreter: types, timers, counters, a station's state machine, errors as a PLC gives them.</summary>
    public class StructuredTextTests
    {
        private static StProgram Compile(string source)
        {
            var p = StProgram.Compile(source, out var errors);
            Assert.True(p != null, string.Join("\n", errors));
            return p!;
        }

        [Fact]
        public void ATimerDelaysAnOutputAndAnEdgeCountsPresses()
        {
            var p = Compile(@"
PROGRAM Lamba
VAR
    Dugme AT %IX0.0 : BOOL;
    Lamba AT %QX0.0 : BOOL;
    Gecikme : TON;
    Kenar : R_TRIG;
    Sayac : CTU;
    Adet : INT;
END_VAR
Gecikme(IN := Dugme, PT := T#500ms);
Lamba := Gecikme.Q;          (* lights half a second after the button *)
Kenar(CLK := Dugme);
Sayac(CU := Kenar.Q, R := FALSE, PV := 3, CV => Adet);
END_PROGRAM");
            // The timer counts from the scan that first sees the button: 50 scans of 10 ms later it has run 490 ms.
            p.Inputs[0] = true;
            for (int i = 0; i < 50; i++) p.Scan(10);
            Assert.False(p.Outputs[0]);
            p.Scan(10);
            Assert.True(p.Outputs[0]);    // 500 ms
            p.Inputs[0] = false;
            p.Scan(10);
            Assert.False(p.Outputs[0]);
            for (int press = 0; press < 2; press++)
            {
                p.Inputs[0] = true;
                p.Scan(10);
                p.Inputs[0] = false;
                p.Scan(10);
            }

            Assert.Equal(3, p.Read("ADET"));   // three rising edges
        }

        [Fact]
        public void ACaseStateMachineRunsAStationsSteps()
        {
            // A pusher station: wait for a part, push it out, wait for the cylinder at the front, retract, count.
            var p = Compile(@"
VAR
    Parca AT %IX0.0 : BOOL;
    Ileride AT %IX0.1 : BOOL;
    Geride AT %IX0.2 : BOOL;
    Itici AT %QX0.0 : BOOL;
    Adim : INT := 0;
    Toplam AT %MW0 : INT;
    Bekle : TON;
END_VAR
CASE Adim OF
    0: IF Parca AND Geride THEN Adim := 10; END_IF
    10: Itici := TRUE;
        IF Ileride THEN Adim := 20; END_IF
    20: Itici := FALSE;
        Bekle(IN := Geride, PT := T#100ms);
        IF Bekle.Q THEN
            Bekle(IN := FALSE);
            Toplam := Toplam + 1;
            Adim := 0;
        END_IF
ELSE
    Adim := 0;
END_CASE");
            p.Inputs[2] = true;   // retracted
            p.Scan(10);
            Assert.Equal(0, p.Read("ADIM"));
            p.Inputs[0] = true;   // a part arrives
            p.Scan(10);
            Assert.Equal(10, p.Read("ADIM"));
            p.Scan(10);
            Assert.True(p.Outputs[0]);
            p.Inputs[2] = false;
            p.Inputs[1] = true;   // the cylinder is out
            p.Inputs[0] = false;
            p.Scan(10);
            Assert.Equal(20, p.Read("ADIM"));
            p.Scan(10);
            Assert.False(p.Outputs[0]);
            p.Inputs[1] = false;
            p.Inputs[2] = true;   // back
            for (int i = 0; i < 12; i++) p.Scan(10);
            Assert.Equal(0, p.Read("ADIM"));
            Assert.Equal(1, p.Memory[0]);   // the count is in %MW0
        }

        [Fact]
        public void IntegersWrapLoopsAndConversionsAsInAPlc()
        {
            var p = Compile(@"
VAR
    a : INT := 32767;
    b : UINT;
    r : REAL;
    i, s : DINT;
    t : TIME := T#1m2s500ms;
    x : WORD := 16#FF00;
    ok : BOOL;
END_VAR
a := a + 1;                       // INT wraps to -32768
b := 0; b := b - 1;               // UINT wraps to 65535
r := INT_TO_REAL(7) / 2.0;        // 3.5
s := 0;
FOR i := 1 TO 10 BY 3 DO s := s + i; END_FOR;   // 1 + 4 + 7 + 10
ok := (x AND 16#0F00) = 16#0F00 AND NOT FALSE;
i := 0;
WHILE TRUE DO i := i + 1; IF i >= 5 THEN EXIT; END_IF END_WHILE;
");
            Assert.True(p.Scan(10), p.Fault);
            Assert.Equal(-32768, p.Read("A"));
            Assert.Equal(65535, p.Read("B"));
            Assert.Equal(3.5, p.Read("R"), 9);
            Assert.Equal(22, p.Read("S"));
            Assert.Equal(62500, p.Read("T"));
            Assert.Equal(1, p.Read("OK"));
            Assert.Equal(5, p.Read("I"));
        }

        [Theory]
        [InlineData("VAR x : INT; END_VAR\ny := 1;", 2, "tanımlı değil")]
        [InlineData("VAR x : BOOL; END_VAR\nx := 3 + 4;", 2, "koşul")]
        [InlineData("VAR x : FOO; END_VAR", 1, "türü yok")]
        [InlineData("VAR t : TON; END_VAR\nt(IN := TRUE, XX := T#1s);", 2, "girişi yok")]
        [InlineData("VAR x : INT; END_VAR\nIF x THEN x := 1; END_IF", 2, "koşul")]
        [InlineData("VAR q AT %QX9.0 : BOOL; END_VAR", 1, "adresi tanınmadı")]
        [InlineData("VAR x : INT; END_VAR\n\n\nx := 1", 4, "';'")]
        public void ErrorsSayWhereAndWhy(string source, int line, string why)
        {
            Assert.Null(StProgram.Compile(source, out var errors));
            var e = Assert.Single(errors);
            Assert.Equal(line, e.Line);
            Assert.Contains(why, e.Message);
        }

        [Fact]
        public void ADivisionByZeroStopsThePlcUntilRestarted()
        {
            var p = Compile("VAR a, b : INT; q AT %QX0.0 : BOOL; END_VAR\nq := TRUE;\na := 10 / b;");
            Assert.False(p.Scan(10));
            Assert.Contains("sıfıra bölme", p.Fault);
            Assert.False(p.Scan(10));
            p.Restart();
            Assert.Null(p.Fault);
            Assert.False(p.Outputs[0]);
        }
    }
}
