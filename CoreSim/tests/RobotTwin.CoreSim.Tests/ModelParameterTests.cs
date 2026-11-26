using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Modeling;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[param] Formulas, named parameters in the order they need each other, and the dimensions bound to them.</summary>
    public class ModelParameterTests
    {
        private readonly ITestOutputHelper _out;

        public ModelParameterTests(ITestOutputHelper output) => _out = output;

        private static double Eval(string text, Func<string, double?>? names = null)
        {
            Assert.True(Expression.TryEvaluate(text, names, out double v, out string? error), error);
            return v;
        }

        [Theory]
        [InlineData("2 + 3 * 4", 14)]
        [InlineData("(2 + 3) * 4", 20)]
        [InlineData("2 ^ 3 ^ 2", 512)]
        [InlineData("-2 ^ 2", -4)]
        [InlineData("2,5 * 2", 5)]
        [InlineData("=100 / 8", 12.5)]
        [InlineData("sin(30)", 0.5)]
        [InlineData("atan2(1; 1)", 45)]
        [InlineData("max(2, 5, 3)", 5)]
        [InlineData("max(2,5; 1)", 2.5)]
        [InlineData("round(2,345; 2)", 2.35)]
        [InlineData("hypot(3; 4)", 5)]
        [InlineData("2 * pi", 2 * Math.PI)]
        [InlineData("1.5e3", 1500)]
        [InlineData("10 % 4", 2)]
        public void FormulasWorkOutAsWritten(string text, double expected) => Assert.Equal(expected, Eval(text), 9);

        [Theory]
        [InlineData("2 +", "formül eksik")]
        [InlineData("(2 + 3", "')' eksik")]
        [InlineData("genişlik * 2", "'genişlik' tanımlı değil")]
        [InlineData("1 / 0", "sıfıra bölme")]
        [InlineData("sin(1; 2)", "sin 1 değer alır")]
        public void BrokenFormulasSayWhy(string text, string why)
        {
            Assert.False(Expression.TryEvaluate(text, null, out _, out string? error));
            Assert.Equal(why, error);
        }

        [Fact]
        public void NamesInAnyLanguageAndTheNamesAFormulaUses()
        {
            Assert.Equal(30, Eval("genişlik / 2", n => n == "genişlik" ? 60 : null), 9);
            Assert.Equal(new List<string> { "a", "boşluk" }, Expression.Names("max(a; boşluk) + a * 2"));
            Assert.True(Expression.IsName("delik_x2"));
            Assert.False(Expression.IsName("2a"));
            Assert.False(Expression.IsName("sin"));
            Assert.False(Expression.IsName("pi"));
        }

        private static ModelDocument Doc(params (string Name, string Formula)[] parameters) =>
            new ModelDocument { Parameters = parameters.Select(p => new ModelParameter { Name = p.Name, Expression = p.Formula }).ToList() };

        [Fact]
        public void ParametersWorkOutInTheOrderTheyNeedEachOther()
        {
            // Listed after what uses them: the order of the list does not matter.
            var doc = Doc(("delik_x", "genislik / 2 - kenar"), ("genislik", "60"), ("kenar", "genislik / 6"));
            Assert.Empty(ModelParameters.Evaluate(doc));
            Assert.Equal(20, doc.Parameters!.Single(p => p.Name == "delik_x").Value, 9);
            Assert.Equal(10, doc.Parameters!.Single(p => p.Name == "kenar").Value, 9);
        }

        [Fact]
        public void ALoopAnUnknownNameAndATwiceDefinedNameAreReported()
        {
            var doc = Doc(("a", "b + 1"), ("b", "a * 2"), ("c", "d"), ("e", "1"), ("e", "2"), ("f", "e + 1"));
            var problems = ModelParameters.Evaluate(doc);
            foreach (var p in problems) _out.WriteLine(p);
            Assert.Contains(problems, p => p.StartsWith("a: döngü: a → b → a", StringComparison.Ordinal));
            Assert.Contains(problems, p => p == "c: 'd' tanımlı değil");
            Assert.Contains(problems, p => p == "e: 'e' iki kez tanımlı");
            Assert.Equal(2, doc.Parameters!.Single(p => p.Name == "f").Value, 9);   // the first e counts
        }

        [Fact]
        public void APlatesDimensionsAndItsSketchFollowTheParameters()
        {
            // A plate extruded from a dimensioned sketch: its width, height and hole bound to parameters, its thickness too.
            var doc = Doc(("genislik", "40"), ("yukseklik", "genislik / 2"), ("kalinlik", "3"));
            var plate = ModelOps.AddPrimitive(doc, PrimitiveKind.Extrude);
            var shapes = new Sketch2D { Outer = SketchShape.Rect(40, 20) };
            shapes.Holes.Add(SketchShape.Circle(5, 10, 0));
            shapes.Constrained = ConstrainedSketch.From(shapes);
            plate.Primitive!.Sketch = shapes;
            var lengths = shapes.Constrained.Dimensions.Where(d => d.Kind == SketchConstraintKind.Length).ToList();
            lengths[0].Expression = "genislik";
            lengths[1].Expression = "yukseklik";
            shapes.Constrained.Dimensions.Single(d => d.Kind == SketchConstraintKind.HorizontalDistance).Expression = "genislik * 3 / 4";
            plate.Primitive.Expressions = new Dictionary<string, string> { ["h"] = "kalinlik" };
            Assert.Empty(ModelParameters.Apply(doc));

            doc.Parameters!.Single(p => p.Name == "genislik").Expression = "80";
            Assert.Empty(ModelParameters.Apply(doc));
            var outer = plate.Primitive.Sketch!.Outer.ToPolygon();
            double w = outer.Max(p => p.X) - outer.Min(p => p.X), h = outer.Max(p => p.Y) - outer.Min(p => p.Y);
            var hole = plate.Primitive.Sketch.Holes.Single().ToPolygon(64);
            _out.WriteLine($"plaka {w:0.###} × {h:0.###} mm, delik x {hole.Average(p => p.X):0.###}, kalınlık {plate.Primitive.Get("h")}");
            Assert.Equal(80, w, 6);
            Assert.Equal(40, h, 6);   // the height follows the width
            Assert.Equal(-20 + 60, hole.Average(p => p.X), 6);   // three quarters across from the fixed left corner
            Assert.Equal(3, plate.Primitive.Get("h"), 9);

            // A value the sketch cannot take leaves it as it was and says so.
            doc.Parameters!.Single(p => p.Name == "genislik").Expression = "-5";
            var problems = ModelParameters.Apply(doc);
            Assert.Contains(problems, p => p.Contains("eskiz bu değerlerle çelişiyor", StringComparison.Ordinal));
            Assert.Equal(80, plate.Primitive.Sketch!.Outer.ToPolygon().Max(p => p.X) - plate.Primitive.Sketch.Outer.ToPolygon().Min(p => p.X), 6);
            Assert.True(ModelParameters.IsUsed(doc, "kalinlik"));
            Assert.False(ModelParameters.IsUsed(doc, "yok"));
        }

        [Fact]
        public void ParametersTravelInTheModelFile()
        {
            var doc = Doc(("genislik", "40"), ("yukseklik", "genislik / 2"));
            var back = ModelDocument.FromJson(doc.ToJson());
            Assert.Equal(2, back.Parameters!.Count);
            Assert.Empty(ModelParameters.Evaluate(back));
            Assert.Equal(20, back.Parameters[1].Value, 9);
        }
    }
}
