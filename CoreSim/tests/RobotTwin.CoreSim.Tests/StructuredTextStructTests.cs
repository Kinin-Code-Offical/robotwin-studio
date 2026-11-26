using System.Linq;
using RobotTwin.CoreSim.Industrial;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[plc-struct] Structures, enumerations and VAR_IN_OUT in Structured Text, as IEC 61131-3 has them.</summary>
    public class StructuredTextStructTests
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

        private const string Types = @"
TYPE
    Mode : (Idle, Run := 10, Fault);
    Point : STRUCT
        X : REAL;
        Y : REAL := 2.5;
    END_STRUCT;
    Part : STRUCT
        Id : INT;
        At : Point;
        Marks : ARRAY[1..3] OF INT := [7, 8, 9];
        State : Mode;
    END_STRUCT;
END_TYPE
";

        [Fact]
        public void StructuresHoldFieldsNestedAndInArrays()
        {
            var p = Compile(Types + @"
VAR
    P : Part;
    Q : Part;
    Line : ARRAY[0..2] OF Point;
    Home : Point := (X := 1.0, Y := -1.0);
    I : INT;
    Sum : REAL;
END_VAR
P.Id := 42;
P.At.X := P.At.Y * 2.0;
P.Marks[2] := P.Marks[1] + P.Marks[3];
FOR I := 0 TO 2 DO
    Line[I].X := INT_TO_REAL(I) * 10.0;
    Line[I].Y := Line[I].X + 0.5;
END_FOR;
Line[1] := Home;          (* a whole structure into an element *)
Q := P;                   (* a whole structure, nested fields and arrays too *)
Q.Marks[1] := 0;
Sum := Line[0].X + Line[1].X + Line[2].X + Line[2].Y;
");
            p.Scan(10);
            Assert.Equal(42, p.Read("P.ID"));
            Assert.Equal(5.0, p.Read("P.AT.X"));
            Assert.Equal(16, p.Read("P.MARKS[2]"));
            Assert.Equal(42, p.Read("Q.ID"));
            Assert.Equal(5.0, p.Read("Q.AT.X"));
            Assert.Equal(7, p.Read("P.MARKS[1]"));   // Q's copy changed, not P
            Assert.Equal(0, p.Read("Q.MARKS[1]"));
            Assert.Equal(new[] { 0.0, 1.0, 20.0 }, Enumerable.Range(0, 3).Select(i => p.Read($"LINE[{i}].X")));
            Assert.Equal(-1.0, p.Read("LINE[1].Y"));
            Assert.Equal(0 + 1 + 20 + 20.5, p.Read("SUM"));

            // the panel and an HMI see every field by its name, and can write one
            var names = p.Watch.Select(w => w.Name).ToList();
            Assert.Contains("P.AT.Y", names);
            Assert.Contains("P.MARKS[3]", names);
            Assert.Contains("LINE[2].Y", names);
            Assert.DoesNotContain("P", names);
            Assert.True(p.Write("LINE[2].X", 3.25));
            Assert.Equal(3.25, p.Read("LINE[2].X"));

            p.Restart();   // back to the types' and the declarations' initial values
            Assert.Equal(2.5, p.Read("P.AT.Y"));
            Assert.Equal(-1.0, p.Read("HOME.Y"));
            Assert.Equal(8, p.Read("P.MARKS[2]"));
        }

        [Fact]
        public void EnumerationsNameAStationsStates()
        {
            var p = Compile(Types + @"
VAR
    State : Mode;
    Start AT %IX0.0 : BOOL;
    Busy AT %QX0.0 : BOOL;
    Ticks : INT;
END_VAR
CASE State OF
    Idle:
        IF Start THEN State := Run; END_IF;
    Mode#Run:
        Ticks := Ticks + 1;
        IF Ticks >= 3 THEN State := Mode#Fault; END_IF;
    Fault, 99:
        Ticks := 0;
END_CASE;
Busy := State = Run;
");
            p.Scan(10);
            Assert.Equal(0, p.Read("STATE"));   // Idle
            p.Inputs[0] = true;
            p.Scan(10);
            Assert.Equal(10, p.Read("STATE"));   // Run := 10
            Assert.True(p.Outputs[0]);
            for (int i = 0; i < 3; i++) p.Scan(10);
            Assert.Equal(11, p.Read("STATE"));   // Fault follows Run
            Assert.False(p.Outputs[0]);
        }

        [Fact]
        public void VarInOutHandsABlockAndAFunctionTheCallersOwnVariables()
        {
            var p = Compile(Types + @"
FUNCTION_BLOCK Mover
VAR_INPUT Dx : REAL; END_VAR
VAR_IN_OUT Target : Point; Count : INT; END_VAR
VAR_OUTPUT Moved : BOOL; END_VAR
Target.X := Target.X + Dx;
Count := Count + 1;
Moved := TRUE;
END_FUNCTION_BLOCK

FUNCTION Swap : BOOL
VAR_IN_OUT A, B : Point; END_VAR
VAR T : Point; END_VAR
T := A;
A := B;
B := T;
Swap := TRUE;
END_FUNCTION

FUNCTION Total : INT
VAR_IN_OUT Values : ARRAY[1..3] OF INT; END_VAR
VAR I : INT; END_VAR
Total := 0;
FOR I := 1 TO 3 DO Total := Total + Values[I]; Values[I] := 0; END_FOR;
END_FUNCTION

VAR
    M : Mover;
    Pts : ARRAY[1..2] OF Point;
    Moves : ARRAY[1..2] OF INT;
    A, B : Point;
    Done : BOOL;
    Box : Part;
    Got : INT;
    Store : ARRAY[1..3] OF INT := [4, 5, 6];
END_VAR
M(Dx := 1.5, Target := Pts[2], Count := Moves[2]);
M(Dx := 1.5, Target := Pts[2], Count := Moves[2]);
A.X := 1.0;
B.X := 2.0;
Done := Swap(A, B);
Got := Total(Store);
");
            p.Scan(10);
            Assert.Equal(3.0, p.Read("PTS[2].X"));   // moved twice through its in-out
            Assert.Equal(0.0, p.Read("PTS[1].X"));
            Assert.Equal(2, p.Read("MOVES[2]"));
            Assert.Equal(1, p.Read("M.MOVED"));
            Assert.Equal((2.0, 1.0), (p.Read("A.X"), p.Read("B.X")));   // swapped
            Assert.Equal(15, p.Read("GOT"));
            Assert.Equal(0, p.Read("STORE[2]"));   // the function cleared the caller's array
        }

        [Fact]
        public void MistakesAreSaidWhereTheyAre()
        {
            Assert.Contains("'Z' alanı yok", Error(Types + "VAR P : Point; R : REAL; END_VAR R := P.Z;"));
            Assert.Contains("bir değer değil", Error(Types + "VAR P : Point; R : REAL; END_VAR R := P;"));
            Assert.Contains("yapı dizisi", Error(Types + "VAR L : ARRAY[1..2] OF Point; R : REAL; END_VAR R := L.X;"));
            Assert.Contains("POINT yapısı", Error(Types + "VAR P : Point; Q : Part; END_VAR P := Q;"));
            Assert.Contains("VAR_IN_OUT yalnız", Error("VAR_IN_OUT X : INT; END_VAR"));
            Assert.Contains("her çağrıda verilmeli", Error(Types + "FUNCTION_BLOCK B VAR_IN_OUT P : Point; END_VAR END_FUNCTION_BLOCK VAR X : B; END_VAR X();"));
            Assert.Contains("POINT yapısı ister", Error(Types + "FUNCTION_BLOCK B VAR_IN_OUT P : Point; END_VAR END_FUNCTION_BLOCK VAR X : B; N : INT; END_VAR X(P := N);"));
            Assert.Contains("VAR_IN_OUT ile geçirin", Error(Types + "FUNCTION_BLOCK B VAR_INPUT P : Point; END_VAR END_FUNCTION_BLOCK VAR X : B; END_VAR"));
            Assert.Contains("dizi alanı olamaz", Error("TYPE T : STRUCT A : ARRAY[1..2] OF INT; END_STRUCT; END_TYPE VAR L : ARRAY[1..3] OF T; END_VAR"));
            var p = Compile(Types + "VAR L : ARRAY[1..2] OF Point; I : INT := 3; END_VAR L[I].X := 1.0;");
            Assert.False(p.Scan(10));
            Assert.Contains("L[3]", p.Fault);
        }
    }
}
