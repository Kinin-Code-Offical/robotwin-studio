using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Industrial;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[plc-vars] A program's VAR blocks as a variable table and back, losing nothing the table cannot hold.</summary>
    public class StDeclarationsTests
    {
        [Fact]
        public void TheExampleProgramsVariablesReadAsRows()
        {
            var rows = StDeclarations.Read(CellParts.PlcExample);
            Assert.Equal(new[] { "Dugme", "Parca", "Ileride", "Geride", "Lamba", "Flas", "Itici", "T1", "Adim", "Sayi" }, rows.Select(r => r.Name));
            var dugme = rows[0];
            Assert.Equal(("VAR", "%IX0.0", "BOOL", ""), (dugme.Section, dugme.Location, dugme.Type, dugme.Initial));
            var adim = rows.Single(r => r.Name == "Adim");
            Assert.Equal(("INT", "0", ""), (adim.Type, adim.Initial, adim.Location));
            Assert.Equal("%MW0", rows.Single(r => r.Name == "Sayi").Location);
            Assert.Equal("TON", rows.Single(r => r.Name == "T1").Type);

            // written back unchanged, the program is the same program
            string again = StDeclarations.Write(CellParts.PlcExample, rows);
            Assert.NotNull(StProgram.Compile(again, out var errors));
            Assert.Empty(errors);
            Assert.Equal(rows.Select(r => r.ToLine()), StDeclarations.Read(again).Select(r => r.ToLine()));
            Assert.Contains("Lamba := Dugme;", again);   // the code is untouched
        }

        [Fact]
        public void EditsAddsAndRemovalsGoBackIntoTheBlocks()
        {
            const string text = "(* başlık *)\nVAR\n    A, B : BOOL; (* iki ad: tablo tutamaz *)\n    Hiz AT %MW1 : INT := 10; (* ayar *)\n    Lamba AT %QX0.0 : BOOL;\nEND_VAR\nVAR\n    Eski : INT;\nEND_VAR\n\nLamba := A AND B;\n";
            var rows = StDeclarations.Read(text);
            Assert.Equal(new[] { "Hiz", "Lamba", "Eski" }, rows.Select(r => r.Name));
            Assert.Equal("ayar", rows[0].Comment);

            rows[0].Type = "DINT";
            rows[0].Initial = "25";
            rows.RemoveAll(r => r.Name == "Eski");   // the second block is left empty: it goes
            rows.Add(new StVariable { Section = "VAR", Name = "Sayac", Type = "INT" });
            rows.Add(new StVariable { Section = "VAR_GLOBAL", Name = "Mod", Type = "INT", Initial = "1" });
            string written = StDeclarations.Write(text, rows);

            Assert.Contains("A, B : BOOL;", written);   // the line the table could not hold stays
            Assert.Contains("Hiz AT %MW1 : DINT := 25; (* ayar *)", written);
            Assert.Contains("Sayac : INT;", written);
            Assert.DoesNotContain("Eski", written);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(written, @"^VAR$", System.Text.RegularExpressions.RegexOptions.Multiline));
            Assert.Contains("VAR_GLOBAL\n    Mod : INT := 1;\nEND_VAR", written);
            Assert.Contains("Lamba := A AND B;", written);
            Assert.StartsWith("(* başlık *)", written);
            var back = StDeclarations.Read(written);
            Assert.Equal(new[] { "Hiz", "Lamba", "Sayac", "Mod" }, back.Select(r => r.Name));
            Assert.NotNull(StProgram.Compile(written, out var errors));
            Assert.Empty(errors);

            Assert.True(StDeclarations.IsName("Seviye_2"));
            Assert.False(StDeclarations.IsName("2Seviye"));
            Assert.False(StDeclarations.IsName("a b"));
        }
    }
}
