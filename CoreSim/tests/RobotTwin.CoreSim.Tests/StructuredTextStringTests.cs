using System.Linq;
using RobotTwin.CoreSim.Industrial;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[plc-string] [plc-array] STRING and arrays of more dimensions in Structured Text, as IEC 61131-3 has them.</summary>
    public class StructuredTextStringTests
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

        [Fact]
        public void StringFunctionsWorkAsIecDefinesThem()
        {
            var p = Compile(@"
VAR
    S : STRING := 'Hello, PLC';
    Short : STRING[5];
    L, F, F0, N : INT;
    A, B, C, D, E, G, H, Q : STRING;
    R : REAL;
    T : TIME;
    Ok, Less : BOOL;
END_VAR
L := LEN(S);
A := LEFT(S, 5);
B := RIGHT(S, 3);
C := MID(IN := S, L := 2, P := 8);
D := CONCAT(A, ' ', 'world', '$21');
E := INSERT(S, '!!', 5);
G := DELETE(S, 2, 6);
H := REPLACE(S, 'XY', 3, 1);
F := FIND(S, 'PLC');
F0 := FIND(S, 'xyz');
Short := S;                       (* cut to its five characters *)
Q := CONCAT(INT_TO_STRING(-42), '|', REAL_TO_STRING(1.5), '|', BOOL_TO_STRING(TRUE), '|', TIME_TO_STRING(T#1m2s500ms));
N := STRING_TO_INT('  123 ') + STRING_TO_INT('16#FF') + STRING_TO_INT('abc');
R := STRING_TO_REAL('2.25');
T := STRING_TO_TIME('T#1s500ms');
Ok := S = 'Hello, PLC' AND A <> B;
Less := 'abc' < 'abd';
");
            Assert.True(p.Scan(10), p.Fault);
            Assert.Equal(10, p.Read("L"));
            Assert.Equal("Hello", p.ReadText("A"));
            Assert.Equal("PLC", p.ReadText("B"));
            Assert.Equal("PL", p.ReadText("C"));
            Assert.Equal("Hello world!", p.ReadText("D"));
            Assert.Equal("Hello!!, PLC", p.ReadText("E"));
            Assert.Equal("HelloPLC", p.ReadText("G"));
            Assert.Equal("XYlo, PLC", p.ReadText("H"));
            Assert.Equal(8, p.Read("F"));
            Assert.Equal(0, p.Read("F0"));
            Assert.Equal("Hello", p.ReadText("SHORT"));
            Assert.Equal("-42|1.5|TRUE|T#1m2s500ms", p.ReadText("Q"));
            Assert.Equal(123 + 255 + 0, p.Read("N"));
            Assert.Equal(2.25, p.Read("R"));
            Assert.Equal(1500, p.Read("T"));
            Assert.Equal(1, p.Read("OK"));
            Assert.Equal(1, p.Read("LESS"));

            // the panel shows a STRING in quotes; an HMI or a test writes one; a cold restart brings the initial text back
            Assert.Contains(p.Watch, w => w.Name == "S" && w.Value == "'Hello, PLC'");
            Assert.True(p.WriteText("SHORT", "abcdefgh"));
            Assert.Equal("abcde", p.ReadText("SHORT"));
            Assert.False(p.Write("S", 1));
            p.Restart();
            Assert.Equal("Hello, PLC", p.ReadText("S"));
            Assert.Equal(string.Empty, p.ReadText("A"));
        }

        [Fact]
        public void StringsGoThroughArraysStructuresBlocksAndFunctions()
        {
            var p = Compile(@"
TYPE
    Order : STRUCT
        Code : STRING[8] := 'NONE';
        Qty : INT;
    END_STRUCT;
END_TYPE

FUNCTION_BLOCK Labeler
VAR_INPUT Prefix : STRING; N : INT; END_VAR
VAR_OUTPUT Label : STRING; END_VAR
Label := CONCAT(Prefix, '-', INT_TO_STRING(N));
END_FUNCTION_BLOCK

FUNCTION Shout : STRING[20]
VAR_INPUT S : STRING; END_VAR
Shout := CONCAT(S, '!');
END_FUNCTION

FUNCTION Clear : BOOL
VAR_IN_OUT O : Order; END_VAR
O.Code := '';
Clear := TRUE;
END_FUNCTION

VAR
    Names : ARRAY[1..4] OF STRING := ['a', 'bb', 2('ccc')];
    Orders : ARRAY[1..2] OF Order;
    O : Order := (Code := 'X1', Qty := 4);
    Lb : Labeler;
    Out, Loud, Joined : STRING;
    I : INT;
    Done : BOOL;
END_VAR
Joined := '';
FOR I := 1 TO 4 DO
    Joined := CONCAT(Joined, Names[I]);
END_FOR;
Orders[2].Code := 'LONGERCODE';
Orders[1] := O;
Lb(Prefix := 'BOX', N := 7, Label => Out);
Loud := Shout(Lb.Label);
Done := Clear(Orders[1]);
");
            Assert.True(p.Scan(10), p.Fault);
            Assert.Equal("abbcccccc", p.ReadText("JOINED"));
            Assert.Equal("ccc", p.ReadText("NAMES[3]"));
            Assert.Equal("LONGERCO", p.ReadText("ORDERS[2].CODE"));   // STRING[8]
            Assert.Equal(string.Empty, p.ReadText("ORDERS[1].CODE"));   // cleared through VAR_IN_OUT
            Assert.Equal(4, p.Read("ORDERS[1].QTY"));
            Assert.Equal("X1", p.ReadText("O.CODE"));
            Assert.Equal("BOX-7", p.ReadText("OUT"));
            Assert.Equal("BOX-7", p.ReadText("LB.LABEL"));
            Assert.Equal("BOX-7!", p.ReadText("LOUD"));
            Assert.Contains(p.Tags, t => t.Name == "ORDERS[2].CODE" && t.Type == StType.String && t.Text == "LONGERCO");
        }

        [Fact]
        public void LiteralsReadTheDollarEscapes()
        {
            var p = Compile(@"VAR S, W : STRING; L : INT; END_VAR
S := 'it$'s $$5$N$41';
W := STRING#""say $""hi$"""";
L := LEN(S);");
            Assert.True(p.Scan(10));
            Assert.Equal("it's $5\nA", p.ReadText("S"));
            Assert.Equal("say \"hi\"", p.ReadText("W"));
            Assert.Equal(9, p.Read("L"));
            Assert.Contains(p.Watch, w => w.Name == "S" && w.Value == "'it$'s $$5$NA'");
        }

        [Fact]
        public void ArraysOfMoreDimensionsHoldAGrid()
        {
            var p = Compile(@"
TYPE Cell : STRUCT Full : BOOL; Id : INT; END_STRUCT; END_TYPE
VAR
    M : ARRAY[1..3, 0..3] OF INT := [1, 2, 3, 4, 4(0), 5];
    Grid : ARRAY[0..1, 0..2] OF Cell;
    Names : ARRAY[1..2, 1..2] OF STRING := ['a', 'b', 'c', 'd'];
    Timers : ARRAY[1..2, 1..2] OF TON;
    I, J, Sum, Count : INT;
END_VAR
FOR I := 1 TO 3 DO
    FOR J := 0 TO 3 DO
        Sum := Sum + M[I, J] * I;
    END_FOR;
END_FOR;
M[2, 3] := 99;
Grid[1, 2].Full := TRUE;
Grid[1, 2].Id := 12;
FOR I := 0 TO 1 DO FOR J := 0 TO 2 DO
    IF Grid[I, J].Full THEN Count := Count + 1; END_IF;
END_FOR; END_FOR;
Timers[2, 1](IN := TRUE, PT := T#20ms);
");
            Assert.True(p.Scan(10), p.Fault);
            Assert.Equal(1 + 2 + 3 + 4, p.Read("M[1,0]") + p.Read("M[1,1]") + p.Read("M[1,2]") + p.Read("M[1,3]"));
            Assert.Equal(5, p.Read("M[3,0]"));   // the 13th value: third row, first column
            Assert.Equal(10 + 5 * 3, p.Read("SUM"));
            Assert.Equal(99, p.Read("M[2,3]"));
            Assert.Equal(1, p.Read("COUNT"));
            Assert.Equal(12, p.Read("GRID[1,2].ID"));
            Assert.Equal("c", p.ReadText("NAMES[2,1]"));
            var names = p.Watch.Select(w => w.Name).ToList();
            Assert.Contains("M[3,3]", names);
            Assert.Contains("GRID[0,1].FULL", names);
            Assert.True(p.Write("M[3,3]", 7));
            Assert.Equal(7, p.Read("M[3,3]"));
            p.Scan(15);
            Assert.True(p.Scan(10));

            var bad = Compile("VAR M : ARRAY[1..2, 1..2] OF INT; I : INT := 3; END_VAR M[1, I] := 1;");
            Assert.False(bad.Scan(10));
            Assert.Contains("M[1,3]: 2. boyut 1..2", bad.Fault);
        }

        [Fact]
        public void MistakesAreSaidWhereTheyAre()
        {
            Assert.Contains("metin verilemez", Error("VAR N : INT; END_VAR N := 'abc';"));
            Assert.Contains("bir metin verin", Error("VAR S : STRING; N : INT; END_VAR S := N;"));
            Assert.Contains("CONCAT", Error("VAR S : STRING; END_VAR S := 'a' + 'b';"));
            Assert.Contains("yalnız bir metinle", Error("VAR S : STRING; B : BOOL; END_VAR B := S = 1;"));
            Assert.Contains("bir metin olmalı", Error("VAR N : INT; END_VAR N := LEN(5);"));
            Assert.Contains("tamsayı", Error("VAR S : STRING; END_VAR S := LEFT(S, 'x');"));
            Assert.Contains("'Q' girişi yok", Error("VAR S : STRING; END_VAR S := MID(IN := S, Q := 1, P := 2);"));
            Assert.Contains("kapanmamış", Error("VAR S : STRING; END_VAR S := 'abc;"));
            Assert.Contains("AT ile", Error("VAR S AT %MW0 : STRING; END_VAR"));
            Assert.Contains("1..65535", Error("VAR S : STRING[0]; END_VAR"));
            Assert.Contains("2 boyutlu", Error("VAR M : ARRAY[1..2, 1..2] OF INT; N : INT; END_VAR N := M[1];"));
            Assert.Contains("tek boyutlu", Error("VAR A : ARRAY[1..2] OF INT; N : INT; END_VAR N := A[1, 2];"));
            Assert.Contains("dizi alanı olamaz", Error("TYPE T : STRUCT A : ARRAY[1..2] OF INT; END_STRUCT; END_TYPE VAR L : ARRAY[1..3] OF T; END_VAR"));
        }
    }
}
