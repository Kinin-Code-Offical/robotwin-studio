using System.Linq;
using System.Text.RegularExpressions;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Tests;

/// <summary>[lang] Language packs: exact texts, patterns with numbers and names, several lines, and the built-in English pack.</summary>
public class LangTests
{
    private const string Pack = """
    {
      "language": "English",
      "strings": { "Ayarlar": "Settings", "açık": "open", "Kaydedildi.": "Saved.", "Dünyayı çalıştır": "Run the world" },
      "patterns": {
        "{world.Objects.Count} parça · {cables} kablo": "{0} parts · {1} cables",
        "Kapı {state}": "Door {0}",
        "Servo açısı: {s.AngleDeg:0.0}°": "Servo angle: {0}°"
      }
    }
    """;

    [Fact]
    public void ExactTextsAndPatternsAreTranslated()
    {
        Lang.LoadJson("en", Pack);
        try
        {
            Assert.Equal("Settings", Lang.T("Ayarlar"));
            Assert.Equal("8 parts · 7 cables", Lang.T("8 parça · 7 kablo"));
            Assert.Equal("Servo angle: 92,5°", Lang.T("Servo açısı: 92,5°"));
            // A pattern's parts are translated too when the pack has them.
            Assert.Equal("Door open", Lang.T("Kapı açık"));
            // Several lines: line by line.
            Assert.Equal("Settings\nSaved.", Lang.T("Ayarlar\nKaydedildi."));
            // Unknown texts stay as they are.
            Assert.Equal("Bilinmeyen metin", Lang.T("Bilinmeyen metin"));
        }
        finally
        {
            Lang.Load("tr");
        }
    }

    [Fact]
    public void AWordlessPatternIsIgnoredAndNothingLoops()
    {
        // "{…}" alone and "{…}: {…}" would match every text (the first one forever, its part being the text itself).
        Lang.LoadJson("en", """{ "strings": { "Hazır": "Ready" }, "patterns": { "{x}": "{0}", "{a}: {b}": "{0}: {1}", "Hız {v}": "Speed {0} {" } }""");
        try
        {
            Assert.Equal(1, Lang.Size.Patterns);   // only "Hız {v}" has words of its own
            Assert.Equal("Durum: Hazır", Lang.T("Durum: Hazır"));
            Assert.Equal("Ready", Lang.T("Hazır"));
            Assert.Equal("Bir şey: başka", Lang.T("Bir şey: başka"));
            // A value with a stray brace leaves the text as it was.
            Assert.Equal("Hız 5", Lang.T("Hız 5"));
        }
        finally
        {
            Lang.Load("tr");
        }
    }

    [Fact]
    public void AMenusAccessKeyMovesToTheTranslationAndTheMostParticularPatternWins()
    {
        Lang.LoadJson("en", """{ "strings": { "Yardım": "Help" }, "patterns": { "{a} kΩ": "{0} kilo-ohm", "Direnç {a} kΩ": "Resistor {0} kΩ" } }""");
        try
        {
            Assert.Equal("_Help", Lang.T("_Yardım"));
            Assert.Equal("Resistor 22 kΩ", Lang.T("Direnç 22 kΩ"));
            Assert.Equal("4,7 kilo-ohm", Lang.T("4,7 kΩ"));
        }
        finally
        {
            Lang.Load("tr");
        }
    }

    [Fact]
    public void TurkishTranslatesNothing()
    {
        Lang.Load("tr");
        Assert.Equal("Ayarlar", Lang.T("Ayarlar"));
        Assert.False(Lang.Active);
    }

    [Fact]
    public void TheEnglishPackIsWholeAndItsPatternsHaveTheirParts()
    {
        Assert.Contains(Lang.Available(), l => l.Id == "en");
        Lang.Load("en");
        try
        {
            var (exact, patterns) = Lang.Size;
            Assert.True(exact > 1000, $"{exact} texts");
            Assert.True(patterns > 100, $"{patterns} patterns");
            Assert.Equal("Settings", Lang.T("Ayarlar"));
        }
        finally
        {
            Lang.Load("tr");
        }
    }
}
