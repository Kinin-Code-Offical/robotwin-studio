using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Tests;

/// <summary>[palette] The command palette finds a command from what the user means, word by word, in any order.</summary>
[Collection("Lang")]
public class PaletteMatchTests
{
    private static PaletteCommand Part(string name, string category) => new("Parça ekle", name, () => { }, null, category)
    {
        Context = "schematic",
        OnlyWhenAsked = true,
        Keywords = "ekle yerleştir koy add place " + category
    };

    private static PaletteCommand Probe(string refDes, string name, string category) => new("Ölç", $"{refDes} akımını izle", () => { }, null, name)
    {
        Context = "schematic",
        OnlyWhenAsked = true,
        Keywords = "ölç osiloskop akım prob watch current probe monitor " + name + " " + category
    };

    private static readonly PaletteCommand Drc = new("PCB", "DRC çalıştır (tasarım kuralı denetimi)", () => { })
    {
        Context = "pcb",
        Keywords = "denetle kontrol kural hata run check design rule"
    };

    private static readonly PaletteCommand Erc = new("Devre şeması", "Elektriksel kural denetimi (ERC)", () => { })
    {
        Context = "schematic",
        Keywords = "çalıştır denetle kontrol hata run check"
    };

    [Fact]
    public void AVerbAndAPartNameFindThePartInAnyOrder()
    {
        var mosfet = Part("IRLZ44N MOSFET", "Transistör");
        var resistor = Part("Direnç 220 Ω", "Pasif");
        Assert.True(PaletteMatch.WordScore(mosfet, "MOSFET ekle", "schematic") > 0);
        Assert.True(PaletteMatch.WordScore(mosfet, "ekle mosfet", null) > 0);
        Assert.True(PaletteMatch.WordScore(mosfet, "add mosfet", null) > 0);   // English verbs too
        Assert.Equal(-1, PaletteMatch.WordScore(resistor, "MOSFET ekle", "schematic"));
    }

    [Fact]
    public void ASentenceWithTurkishCaseEndingsFindsThePartsProbe()
    {
        var servo = Probe("U3", "SG90 Servo", "Motor");
        var resistor = Probe("R1", "Direnç 220 Ω", "Pasif");
        // "motorun" is "motor" with a case ending, "akımını" is "akım"
        Assert.True(PaletteMatch.WordScore(servo, "servo motorun akımını izle", "schematic") > 0);
        Assert.Equal(-1, PaletteMatch.WordScore(resistor, "servo motorun akımını izle", "schematic"));
        // The reference designator alone finds it, without the Turkish letters too
        Assert.True(PaletteMatch.WordScore(servo, "u3 akimi", null) > 0);
    }

    [Fact]
    public void TheCheckThatIsAskedForWinsOverTheOtherCheck()
    {
        int drc = PaletteMatch.WordScore(Drc, "DRC çalıştır", "schematic");
        int erc = PaletteMatch.WordScore(Erc, "DRC çalıştır", "schematic");
        Assert.True(drc > 0);
        Assert.Equal(-1, erc);   // "DRC" is not in the electrical rule check
        Assert.True(PaletteMatch.WordScore(Erc, "ERC çalıştır", "schematic") > 0);
        Assert.True(PaletteMatch.WordScore(Drc, "run design rule check", null) > 0);
    }

    [Fact]
    public void ThePageInFrontRanksItsOwnCommandsFirst()
    {
        int onPcb = PaletteMatch.WordScore(Drc, "kural denetimi", "pcb");
        int elsewhere = PaletteMatch.WordScore(Drc, "kural denetimi", "schematic");
        Assert.True(onPcb > elsewhere);
        // Both rule checks answer "kural denetimi": on the PCB page the DRC comes first, on the sheet the ERC
        Assert.True(PaletteMatch.WordScore(Drc, "kural denetimi", "pcb") > PaletteMatch.WordScore(Erc, "kural denetimi", "pcb"));
        Assert.True(PaletteMatch.WordScore(Erc, "kural denetimi", "schematic") > PaletteMatch.WordScore(Drc, "kural denetimi", "schematic"));
    }

    [Fact]
    public void ARunTogetherQueryStillMatchesLetterByLetter()
    {
        Assert.True(PaletteMatch.Score("Elektriksel kural denetimi (ERC)", "elkur") > 0);
        Assert.Equal(-1, PaletteMatch.Score("DRC çalıştır", "xyz"));
        Assert.Equal("isik sogutucu", PaletteMatch.Fold("Işık Soğutucu"));
    }
}
