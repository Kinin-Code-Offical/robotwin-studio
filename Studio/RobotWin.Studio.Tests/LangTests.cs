using System.Linq;
using System.Text.RegularExpressions;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Tests;

/// <summary>[lang] Language packs: exact texts, patterns with numbers and names, several lines, and the built-in English pack.</summary>
[Collection("Lang")]   // the language in use is one for the whole process: tests that load packs run one after another
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
            Assert.Equal("Durum: Ready", Lang.T("Durum: Hazır"));   // not by the pattern: part by part
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
    public void ASketchChangesOnlyWholeLinesAndSearchFindsBothLanguages()
    {
        Lang.LoadJson("de", """{ "strings": { "// Bardak dik bekler": "// The glass waits upright", "Direnç": "Resistor" }, "patterns": { "{a} ms bekle": "wait {0} ms" } }""");
        try
        {
            // Code: whole lines only, never a pattern (a pattern could reach into code).
            string sketch = "// Bardak dik bekler\r\ndelay(500); // 500 ms bekle\r\nvoid loop() {}";
            Assert.Equal("// The glass waits upright\r\ndelay(500); // 500 ms bekle\r\nvoid loop() {}", Lang.Code(sketch));
            // Search: the text as shown and as written, lower-cased.
            string hay = Lang.Searchable("Direnç 220 Ω");
            Assert.Contains(Lang.Fold("DİRENÇ"), hay);
            Lang.LoadJson("en", """{ "strings": { "Direnç": "Resistor" } }""");
            hay = Lang.Searchable("Direnç");
            Assert.Contains(Lang.Fold("RESISTOR"), hay);
            Assert.Contains("direnç", hay);
            // English folds "I" to "i" (Turkish would make it "ı").
            Assert.Equal("import", Lang.Fold("IMPORT"));
        }
        finally
        {
            Lang.Load("tr");
        }
    }

    [Fact]
    public void ATextJoinedFromPartsIsTranslatedPartByPart()
    {
        Lang.LoadJson("en", """{ "strings": { "Bantlı Konveyör": "Belted Conveyor", "sabit": "fixed", "Durum": "State", "Hazır": "Ready" } }""");
        try
        {
            Assert.Equal("Belted Conveyor · fixed", Lang.T("Bantlı Konveyör · sabit"));
            Assert.Equal("State: Ready", Lang.T("Durum: Hazır"));
            Assert.Equal("Belted Conveyor → O3:V+", Lang.T("Bantlı Konveyör → O3:V+"));
            // Nothing known: the text as it was; a time is not a "label: value".
            Assert.Equal("Bilinmeyen · metin", Lang.T("Bilinmeyen · metin"));
            // Two holes side by side: the first is the number ("%100" is not "1" and "00").
            Lang.LoadJson("en", """{ "patterns": { "(sertlik %{a}{b})": "(stiffness {0}%{1})", ", camsı geçiş {t} °C": ", glass transition {0} °C" } }""");
            Assert.Equal("(stiffness 100%, glass transition 60 °C)", Lang.T("(sertlik %100, camsı geçiş 60 °C)"));
            Assert.Equal("12:30", Lang.T("12:30"));
            // A readout's columns: the gap follows the number's width, the pattern takes any gap.
            Lang.LoadJson("en", """{ "strings": { "Gövde borusu (kraft)": "Body tube (kraft)" }, "patterns": { "Sol        {n} rpm ileri": "Left        {0} rpm forward" } }""");
            Assert.Equal("Left        59 rpm forward", Lang.T("Sol         59 rpm ileri"));
            Assert.Equal("Left        1205 rpm forward", Lang.T("Sol 1205 rpm ileri"));
            // An object named in English is found by its Turkish name.
            Assert.Equal("Gövde borusu (kraft)", Lang.Source("Body tube (kraft)"));
            // A trailing aside in brackets: the name and the aside each.
            Lang.LoadJson("en", """{ "strings": { "Bantlı Konveyör": "Belted Conveyor", "sabit": "fixed" } }""");
            Assert.Equal("Belted Conveyor (6)", Lang.T("Bantlı Konveyör (6)"));
            Assert.Equal("Belted Conveyor  (fixed)", Lang.T("Bantlı Konveyör  (sabit)"));
        }
        finally
        {
            Lang.Load("tr");
        }
    }

    [Fact]
    public void PartsThePackHasWordForWordWinOverAShortPatternAndOptionalEndingsAndNumbersAreKept()
    {
        Lang.LoadJson("en", """
        {
          "strings": { "İç direnç (0 = varsayılan)": "Internal resistance (0 = default)", "Duvar": "Wall", "Durum": "State", "Hazır": "Ready" },
          "patterns": {
            "İç {a}": "Inner {0}",
            "İtki    : {t} N · çıkış {v} m/s{choked}": "Thrust  : {0} N · exit {1} m/s{2}"
          }
        }
        """);
        try
        {
            // A label and its unit: the label is in the pack as it is, so "İç {…}" does not cut it ("Inner direnç …").
            Assert.Equal("Internal resistance (0 = default) (Ω)", Lang.T("İç direnç (0 = varsayılan) (Ω)"));
            Assert.Equal("State: Ready", Lang.T("Durum: Hazır"));
            // The pattern still does what it is for.
            Assert.Equal("Inner ring", Lang.T("İç ring"));
            // A hole at the very end may stay empty (an optional ending the code leaves out).
            Assert.Equal("Thrust  : 0.2 N · exit 21 m/s", Lang.T("İtki    : 0.2 N · çıkış 21 m/s"));
            // A numbered copy of a named thing.
            Assert.Equal("Wall 2", Lang.T("Duvar 2"));
            Assert.Equal("Wall 12", Lang.T("Duvar 12"));
        }
        finally
        {
            Lang.Load("tr");
        }
    }

    [Fact]
    public void ALongRunOfLiveReadoutsLeavesTranslationsAsTheyWere()
    {
        // A readout drawn every frame asks for a new text each time (its value changes): after tens of thousands the
        // cache has turned over, and the interface's texts still translate, from the pack and from its patterns.
        Lang.LoadJson("en", """{ "strings": { "Ayarlar": "Settings" }, "patterns": { "Hız {v} m/s": "Speed {0} m/s" } }""");
        try
        {
            for (int i = 0; i < 25000; i++)
            {
                Assert.Equal($"Speed {i} m/s", Lang.T($"Hız {i} m/s"));
                if (i % 1000 == 0) Assert.Equal("Settings", Lang.T("Ayarlar"));
            }

            Assert.Equal("Speed 3 m/s", Lang.T("Hız 3 m/s"));
            // An English text the pack produced, seen again, is left as it is.
            Assert.Equal("Speed 24999 m/s", Lang.T("Speed 24999 m/s"));
        }
        finally
        {
            Lang.Load("tr");
        }
    }

    /// <summary>Texts English runs of every world and circuit showed Turkish, with the built-in pack.</summary>
    [Theory]
    [InlineData("Prob: OUTPUT", "Probe: OUTPUT")]
    [InlineData("0 hata · 2 uyarı · 0 bilgi · t = 0 s", "0 errors · 2 warnings · 0 info · t = 0 s")]
    [InlineData("İç direnç (0 = varsayılan) (Ω)", "Internal resistance (0 = default) (Ω)")]
    [InlineData("Duvar 2", "Wall 2")]
    [InlineData("İtki    : 0.2 N · 8.4 g/s · çıkış 21 m/s", "Thrust  : 0.2 N · 8.4 g/s · exit 21 m/s")]
    [InlineData("12k nokta", "12k points")]
    [InlineData("Doluluk 40.0 %", "Duty 40.0 %")]
    [InlineData("R1 → R7: adı değişecek, yeri ve izleri korunur", "R1 → R7: will be renamed, keeping its place and tracks")]
    [InlineData("1 parçanın adı değişecek, yeri korunur (R1 → R7)", "1 part will be renamed, keeping its place (R1 → R7)")]
    [InlineData("U2 devrede yok, kaldırılacak", "U2 is no longer in the circuit and will be removed")]
    [InlineData("1 parça kaldırılacak", "1 part will be removed")]
    [InlineData("3 parça eklenecek", "3 parts will be added")]
    [InlineData("Şema, PCB ve montaj arasında 2 tutarsızlık var.", "2 things are out of step between the schematic, the PCB and the assembly.")]
    [InlineData("RW-7: şema, PCB ve montaj arasında 1 tutarsızlık var.", "RW-7: 1 thing is out of step between the schematic, the PCB and the assembly.")]
    [InlineData("Montajın izlediği proje dosyası bulunamadı: D:\\p\\a.rwproj", "The project file this assembly follows was not found: D:\\p\\a.rwproj")]
    [InlineData("Şema değişti. Kartı güncellemek şunları yapacak:\n1 parça kalkacak (R2)", "The schematic changed. Updating the board will make these changes:\n1 part will be removed (R2)")]
    public void TextsRunsLeftTurkishAreEnglish(string turkish, string english)
    {
        Lang.Load("en");
        try
        {
            Assert.Equal(english, Lang.T(turkish));
        }
        finally
        {
            Lang.Load("tr");
        }
    }

    /// <summary>
    /// Every Turkish label written in the Studio's XAML has its English in the built-in pack, so a label added
    /// without one fails here instead of showing Turkish in the English interface.
    /// </summary>
    [Fact]
    public void EveryXamlLabelHasItsEnglish()
    {
        string? dir = System.AppContext.BaseDirectory;
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir, "Studio", "RobotWin.Studio"))) dir = System.IO.Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var turkish = new Regex("[çğıöşüÇĞİÖŞÜ]");
        var attr = new Regex("\\b(Text|Content|Header|ToolTip|Title)=\"([^\"{][^\"]*)\"");
        Lang.Load("en");
        try
        {
            var missing = new System.Collections.Generic.List<string>();
            foreach (var f in System.IO.Directory.GetFiles(System.IO.Path.Combine(dir!, "Studio", "RobotWin.Studio"), "*.xaml", System.IO.SearchOption.AllDirectories))
            {
                if (f.Contains("\\obj\\") || f.Contains("\\bin\\")) continue;
                foreach (Match m in attr.Matches(System.IO.File.ReadAllText(f)))
                {
                    string text = System.Net.WebUtility.HtmlDecode(m.Groups[2].Value).Replace("&#x0a;", "\n");
                    if (turkish.IsMatch(text) && Lang.T(text) == text) missing.Add(System.IO.Path.GetFileName(f) + ": " + text);
                }
            }

            Assert.True(missing.Count == 0, "XAML labels with no English:\n" + string.Join("\n", missing.Take(40)));
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
