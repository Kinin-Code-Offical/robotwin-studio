using System.Linq;
using RobotTwin.CoreSim.Industrial;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[plc-pou] The program's own function blocks and functions, and arrays, as IEC 61131-3 has them.</summary>
    public class StructuredTextPouTests
    {
        private static StProgram Compile(string source)
        {
            var p = StProgram.Compile(source, out var errors);
            Assert.True(p != null, string.Join("\n", errors));
            return p!;
        }

        private static string Error(string source)
        {
            var p = StProgram.Compile(source, out var errors);
            Assert.Null(p);
            return Assert.Single(errors).ToString();
        }

        private const string Debounce = @"
FUNCTION_BLOCK Debounce
VAR_INPUT
    IN : BOOL;
    PT : TIME := T#30ms;
END_VAR
VAR_OUTPUT
    Q : BOOL;
    Changes : INT;
END_VAR
VAR
    OnDelay : TON;
    OffDelay : TON;
    Last : BOOL;
END_VAR
OnDelay(IN := IN, PT := PT);
OffDelay(IN := NOT IN, PT := PT);
IF OnDelay.Q THEN Q := TRUE; END_IF;
IF OffDelay.Q THEN Q := FALSE; END_IF;
IF Q <> Last THEN Changes := Changes + 1; END_IF;
Last := Q;
END_FUNCTION_BLOCK
";

        [Fact]
        public void EachInstanceOfAFunctionBlockKeepsItsOwnState()
        {
            // Two buttons through their own debouncers: a 10 ms bounce never gets through, a held press does, each on its own.
            var p = Compile(Debounce + @"
PROGRAM Main
VAR
    B1 AT %IX0.0 : BOOL;
    B2 AT %IX0.1 : BOOL;
    L1 AT %QX0.0 : BOOL;
    L2 AT %QX0.1 : BOOL;
    D1, D2 : Debounce;
    Toggles : INT;
END_VAR
D1(IN := B1, Q => L1);
D2(IN := B2, PT := T#50ms);
L2 := D2.Q;
Toggles := D1.Changes;
END_PROGRAM");
            p.Inputs[0] = true;
            p.Scan(10);
            p.Inputs[0] = false;   // a 10 ms bounce
            for (int i = 0; i < 5; i++) p.Scan(10);
            Assert.False(p.Outputs[0]);
            p.Inputs[0] = true;
            p.Inputs[1] = true;
            for (int i = 0; i < 4; i++) p.Scan(10);   // held 30 ms after the scan that saw it: through the first debouncer
            Assert.True(p.Outputs[0]);
            Assert.False(p.Outputs[1]);   // the second waits for its own 50 ms
            for (int i = 0; i < 2; i++) p.Scan(10);
            Assert.True(p.Outputs[1]);
            Assert.Equal(1, p.Read("TOGGLES"));
            Assert.Equal(1, p.Read("D2.CHANGES"));

            p.Restart();   // a cold restart clears every instance
            p.Scan(10);
            Assert.Equal(0, p.Read("TOGGLES"));
        }

        [Fact]
        public void FunctionsTakeNamedOrPositionalArgumentsAndKeepNothing()
        {
            var p = Compile(@"
FUNCTION Scale : REAL
VAR_INPUT
    Raw : INT;
    Lo : REAL := 0.0;
    Hi : REAL := 10.0;
END_VAR
VAR
    Calls : INT;
END_VAR
Calls := Calls + 1;   (* a function's variables start afresh at every call *)
Scale := Lo + (Hi - Lo) * INT_TO_REAL(Raw) / 4095.0 + INT_TO_REAL(Calls - 1);
END_FUNCTION

FUNCTION InBand : BOOL
VAR_INPUT X, Lo, Hi : REAL; END_VAR
InBand := X >= Lo AND X <= Hi;
END_FUNCTION

VAR
    A, B, C : REAL;
    Ok : BOOL;
END_VAR
A := Scale(Raw := 4095);
B := Scale(2048, 4.0, 20.0);
C := Scale(Hi := 100.0, Raw := REAL_TO_INT(Scale(4095)));
Ok := InBand(B, 11.0, 13.0);
");
            Assert.Null(StProgram.Compile("FUNCTION F : INT VAR_INPUT X : INT; END_VAR F := X; END_FUNCTION VAR Y : INT; END_VAR Y := F(TRUE);", out _));
            p.Scan(10);
            Assert.Equal(10.0, p.Read("A"), 9);
            Assert.Equal(4 + 16 * 2048 / 4095.0, p.Read("B"), 9);
            Assert.Equal(100.0 * 10 / 4095.0, p.Read("C"), 9);   // the inner call's result, its own variables fresh
            Assert.Equal(1, p.Read("OK"));
        }

        [Fact]
        public void ArraysHoldValuesAndTimersAndStopTheScanOutsideTheirBounds()
        {
            var p = Compile(@"
VAR
    Weights : ARRAY[1..5] OF INT := [3, 1, 4, 1, 5];
    Delays : ARRAY[0..3] OF TON;
    Inputs AT %IX0.0 : BOOL;
    Done : ARRAY[0..3] OF BOOL;
    Sum, I, Bad : INT;
END_VAR
Sum := 0;
FOR I := 1 TO 5 DO
    Sum := Sum + Weights[I];
END_FOR;
Weights[1] := 13;
FOR I := 0 TO 3 DO
    Delays[I](IN := Inputs, PT := INT_TO_TIME((I + 1) * 100));
    Done[I] := Delays[I].Q;
END_FOR;
IF Bad > 0 THEN Weights[Bad + 5] := 0; END_IF;
");
            p.Scan(10);
            Assert.Equal(14, p.Read("SUM"));
            Assert.Equal(13, p.Read("WEIGHTS[1]"));
            p.Inputs[0] = true;
            for (int i = 0; i < 21; i++) p.Scan(10);   // 200 ms on
            Assert.Equal(new double[] { 1, 1, 0, 0 }, Enumerable.Range(0, 4).Select(i => p.Read($"DONE[{i}]")));
            Assert.Contains(p.Watch, w => w.Name == "WEIGHTS[5]" && w.Value == "5");
            Assert.True(p.Write("WEIGHTS[2]", 7));
            p.Scan(10);
            Assert.Equal(14 + 10 + 6, p.Read("SUM"));

            Assert.True(p.Write("BAD", 1));
            Assert.False(p.Scan(10));   // WEIGHTS[6]: the PLC stops
            Assert.Contains("WEIGHTS[6]", p.Fault);
        }

        [Fact]
        public void TheProgramsOwnBlocksNestAndUseArrays()
        {
            // A station that counts parts per lane with an array inside a block, and a block that holds two of those.
            var p = Compile(@"
PROGRAM Main
VAR
    Lane AT %MW0 : INT;
    Cell : Plant;
    Total : INT;
END_VAR
Cell(Lane := Lane, Pulse := TRUE);
Total := Cell.Total;
END_PROGRAM

FUNCTION_BLOCK Counter4
VAR_INPUT Lane : INT; Pulse : BOOL; END_VAR
VAR_OUTPUT Total : INT; END_VAR
VAR Counts : ARRAY[0..3] OF INT; Edge : R_TRIG; I : INT; END_VAR
Edge(CLK := Pulse);
IF Edge.Q AND Lane >= 0 AND Lane <= 3 THEN Counts[Lane] := Counts[Lane] + 1; END_IF;
Total := 0;
FOR I := 0 TO 3 DO Total := Total + Counts[I]; END_FOR;
END_FUNCTION_BLOCK

FUNCTION_BLOCK Plant
VAR_INPUT Lane : INT; Pulse : BOOL; END_VAR
VAR_OUTPUT Total : INT; END_VAR
VAR A, B : Counter4; END_VAR
A(Lane := Lane, Pulse := Pulse);
B(Lane := Lane - 4, Pulse := Pulse);
Total := A.Total + B.Total;
END_FUNCTION_BLOCK
");
            p.Memory[0] = 2;
            p.Scan(10);   // the rising edge on the first scan counts once
            p.Scan(10);
            Assert.Equal(1, p.Read("TOTAL"));
        }

        [Fact]
        public void TheGuidesExampleRuns()
        {
            // docs/kullanim/07-dunya.md, "Kendi blokları, fonksiyonlar ve diziler"
            var p = Compile(@"
FUNCTION_BLOCK Sicrama
VAR_INPUT IN : BOOL; PT : TIME := T#30ms; END_VAR
VAR_OUTPUT Q : BOOL; END_VAR
VAR Acik, Kapali : TON; END_VAR
Acik(IN := IN, PT := PT);
Kapali(IN := NOT IN, PT := PT);
IF Acik.Q THEN Q := TRUE; ELSIF Kapali.Q THEN Q := FALSE; END_IF
END_FUNCTION_BLOCK
VAR D1 : Sicrama; Dugme AT %IX0.0 : BOOL; Lamba AT %QX0.0 : BOOL; END_VAR
D1(IN := Dugme, Q => Lamba);
");
            p.Inputs[0] = true;
            for (int i = 0; i < 4; i++) p.Scan(10);
            Assert.True(p.Outputs[0]);
            p.Inputs[0] = false;
            for (int i = 0; i < 4; i++) p.Scan(10);
            Assert.False(p.Outputs[0]);
        }

        [Fact]
        public void MistakesAreSaidWhereTheyAre()
        {
            Assert.Contains("özyinelemeli", Error("FUNCTION F : INT VAR_INPUT X : INT; END_VAR F := F(X); END_FUNCTION VAR Y : INT; END_VAR Y := F(1);"));
            Assert.Contains("16 kattan derin", Error("FUNCTION_BLOCK Loop VAR L : Loop; END_VAR END_FUNCTION_BLOCK VAR X : Loop; END_VAR"));
            Assert.Contains("doğrudan adres", Error("FUNCTION_BLOCK B VAR_OUTPUT Q : BOOL; END_VAR Q := %IX0.0; END_FUNCTION_BLOCK VAR X : B; END_VAR X();"));
            Assert.Contains("'Z' girişi yok", Error(Debounce + "VAR D : Debounce; END_VAR D(Z := TRUE);"));
            Assert.Contains("END_FUNCTION_BLOCK eksik", Error("FUNCTION_BLOCK B VAR_INPUT A : BOOL; END_VAR"));
            Assert.Contains("Dizi indisi bir tamsayı", Error("VAR A : ARRAY[0..3] OF INT; R : REAL; END_VAR A[R] := 1;"));
            Assert.Contains("bir dizi", Error("VAR A : ARRAY[0..3] OF INT; X : INT; END_VAR X := A;"));
            Assert.Contains("standart bir adın", Error("FUNCTION_BLOCK TON VAR_INPUT A : BOOL; END_VAR END_FUNCTION_BLOCK"));
        }
    }
}
