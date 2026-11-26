using RobotWin.Studio.Updates;

namespace RobotWin.Studio.Tests;

public class SemVersionTests
{
    [Theory]
    [InlineData("v2.0.0", 2, 0, 0, "")]
    [InlineData("2.1", 2, 1, 0, "")]
    [InlineData("3", 3, 0, 0, "")]
    [InlineData("2.1.0-beta.2", 2, 1, 0, "beta.2")]
    [InlineData("V2.1.3+abc123", 2, 1, 3, "")]
    [InlineData("2.0.0.0", 2, 0, 0, "")]
    public void Parses(string text, int major, int minor, int patch, string pre)
    {
        var v = SemVersion.Parse(text)!;
        Assert.Equal((major, minor, patch, pre), (v.Major, v.Minor, v.Patch, v.Prerelease));
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v2.x")]
    [InlineData("2.0.0-")]
    [InlineData("2..0")]
    public void RejectsGarbage(string text) => Assert.Null(SemVersion.Parse(text));

    [Theory]
    [InlineData("2.0.0", "2.0.1")]
    [InlineData("2.0.9", "2.1.0")]
    [InlineData("2.9.9", "10.0.0")]
    [InlineData("2.1.0-rc.1", "2.1.0")]
    [InlineData("2.1.0-alpha", "2.1.0-alpha.1")]
    [InlineData("2.1.0-alpha.1", "2.1.0-alpha.beta")]
    [InlineData("2.1.0-beta.2", "2.1.0-beta.11")]
    [InlineData("2.1.0-beta", "2.1.0-rc")]
    public void OrdersBySemVerPrecedence(string lower, string higher)
    {
        var a = SemVersion.Parse(lower)!;
        var b = SemVersion.Parse(higher)!;
        Assert.True(a < b);
        Assert.True(b > a);
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void BuildMetadataAndPrefixDoNotMatter() =>
        Assert.Equal(SemVersion.Parse("v2.1.0"), SemVersion.Parse("2.1.0+build.7"));

    [Fact]
    public void FromAssemblyVersion() =>
        Assert.Equal("2.0.0", SemVersion.FromVersion(new Version(2, 0, 0, 0)).ToString());
}

public class ReleaseParsingTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void ParsesTheRealGitHubResponse()
    {
        var releases = ReleaseParser.Parse(Fixture("github-releases.json"));
        var r = Assert.Single(releases);
        Assert.Equal("v2.0.0", r.Tag);
        Assert.Equal("2.0.0", r.DisplayVersion);
        Assert.False(r.IsPrerelease);
        Assert.False(r.IsDraft);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 9, 13, 27, TimeSpan.Zero), r.PublishedAt);
        Assert.StartsWith("## RobotWin Studio 2.0", r.Body);
        Assert.Equal("https://github.com/Kinin-Code-Offical/robotwin-studio/releases/tag/v2.0.0", r.HtmlUrl);
        var a = Assert.Single(r.Assets);
        Assert.Equal("RobotWinStudio-Setup-2.0.0.exe", a.Name);
        Assert.Equal(1745344, a.Size);
        Assert.True(a.IsInstaller);
        Assert.False(a.IsSelfContained);
        Assert.Equal("ca21592785de7f9c3ef39c4506b800e5f997499f36d2d621f3e3760ae4ec0803", a.Sha256);
        // The hash in the notes agrees with GitHub's digest.
        Assert.Equal(a.Sha256, ChecksumParser.Expected(r, a));
    }

    [Fact]
    public void RealReleaseWithBothInstallers()
    {
        var r = Assert.Single(ReleaseParser.Parse(Fixture("github-releases-v1.json")));
        Assert.Equal("1.0.0", r.DisplayVersion);
        Assert.Equal(2, r.Assets.Count(a => a.IsInstaller));
        var full = UpdateLogic.SelectAsset(r, selfContained: true, runtimeAvailable: false)!;
        var light = UpdateLogic.SelectAsset(r, selfContained: false, runtimeAvailable: true)!;
        Assert.Equal("RobotWinStudio-Setup-1.0.0-tam.exe", full.Name);
        Assert.Equal("RobotWinStudio-Setup-1.0.0.exe", light.Name);
        // Each installer's line in the notes matches GitHub's own digest of the upload.
        Assert.Equal(full.Sha256, ChecksumParser.Expected(r, full));
        Assert.Equal(light.Sha256, ChecksumParser.Expected(r, light));
        Assert.NotEqual(ChecksumParser.Expected(r, full), ChecksumParser.Expected(r, light));
        Assert.Equal("v1.0.0", UpdateLogic.FindUpdate(new[] { r }, SemVersion.Parse("0.9.0")!, false, null)!.Tag);
        Assert.Null(UpdateLogic.FindUpdate(new[] { r }, SemVersion.Parse("1.0.0")!, false, null));
    }

    [Fact]
    public void SortsNewestFirstAndKeepsFlags()
    {
        const string json = """
        [
          { "tag_name": "v2.0.0", "name": "", "prerelease": false, "draft": false, "published_at": "2026-11-20T09:00:00Z", "assets": [] },
          { "tag_name": "v2.2.0-beta.1", "prerelease": true, "published_at": "2026-12-20T09:00:00Z" },
          { "tag_name": "v2.1.0", "name": "Kış sürümü", "published_at": "2026-12-01T09:00:00Z",
            "assets": [ { "name": "RobotWinStudio-Setup-2.1.0-tam.exe", "size": 80000000, "browser_download_url": "https://example.invalid/a" },
                        { "name": "notes.txt", "size": 10 } ] },
          { "name": "no tag" },
          { "tag_name": "v3.0.0", "draft": true }
        ]
        """;
        var releases = ReleaseParser.Parse(json);
        Assert.Equal(new[] { "v3.0.0", "v2.2.0-beta.1", "v2.1.0", "v2.0.0" }, releases.Select(r => r.Tag));
        Assert.True(releases[0].IsDraft);
        Assert.True(releases[1].IsPrerelease);
        Assert.Equal("Kış sürümü", releases[2].Title);
        Assert.Equal("RobotWin Studio 2.0.0", releases[3].Title);
        var asset = Assert.Single(releases[2].Assets);   // the asset without a URL is dropped
        Assert.True(asset.IsSelfContained);
        Assert.Null(asset.Sha256);
    }

    [Fact]
    public void NewNamingThePlainInstallerIsTheFullOneAndLiteIsTheSmallOne()
    {
        const string json = """
        { "tag_name": "v2.1.0", "published_at": "2026-11-21T09:00:00Z",
          "assets": [ { "name": "RobotWinStudio-Setup-2.1.0.exe", "size": 80000000, "browser_download_url": "https://example.invalid/full" },
                      { "name": "RobotWinStudio-Setup-2.1.0-lite.exe", "size": 2000000, "browser_download_url": "https://example.invalid/lite" } ] }
        """;
        var r = Assert.Single(ReleaseParser.Parse(json));
        Assert.Equal("RobotWinStudio-Setup-2.1.0.exe", UpdateLogic.SelectAsset(r, selfContained: true, runtimeAvailable: false)!.Name);
        Assert.Equal("RobotWinStudio-Setup-2.1.0-lite.exe", UpdateLogic.SelectAsset(r, selfContained: false, runtimeAvailable: true)!.Name);
        Assert.True(r.Assets.Single(a => !a.IsLite).IsSelfContained);
        Assert.False(r.Assets.Single(a => a.IsLite).IsSelfContained);
    }

    [Fact]
    public void AcceptsASingleReleaseObject() =>
        Assert.Single(ReleaseParser.Parse("""{ "tag_name": "v2.0.1", "body": "a\r\nb" }"""));
}

public class ChecksumTests
{
    private const string H1 = "ca21592785de7f9c3ef39c4506b800e5f997499f36d2d621f3e3760ae4ec0803";
    private const string H2 = "0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void ReadsTheReleaseNotesLine()
    {
        var map = ChecksumParser.Parse($"Notlar\n\nSHA-256 (RobotWinStudio-Setup-2.0.0.exe): `{H1}`\n");
        Assert.Equal(H1, map["RobotWinStudio-Setup-2.0.0.exe"]);
    }

    [Fact]
    public void ReadsSeveralFormats()
    {
        string notes = $"""
            SHA-256 (RobotWinStudio-Setup-2.1.0.exe): `{H1}`
            SHA256 (RobotWinStudio-Setup-2.1.0-tam.exe) = {H2}
            {H1}  other-file.zip
            - **RobotWinStudio-Setup-2.1.0-x.exe**: `{H2}`
            Not a hash: 1234
            """;
        var map = ChecksumParser.Parse(notes);
        Assert.Equal(H1, map["RobotWinStudio-Setup-2.1.0.exe"]);
        Assert.Equal(H2.ToLowerInvariant(), map["RobotWinStudio-Setup-2.1.0-tam.exe"]);
        Assert.Equal(H1, map["other-file.zip"]);
        Assert.Equal(H2.ToLowerInvariant(), map["RobotWinStudio-Setup-2.1.0-x.exe"]);
        Assert.Equal(4, map.Count);
    }

    [Fact]
    public void UnnamedHashAppliesOnlyWhenThereIsOneInstaller()
    {
        var one = new ReleaseInfo { Body = $"SHA-256: {H1}", Assets = { new ReleaseAsset { Name = "RobotWinStudio-Setup-2.1.0.exe", DownloadUrl = "x" } } };
        Assert.Equal(H1, ChecksumParser.Expected(one, one.Assets[0]));

        var two = new ReleaseInfo
        {
            Body = $"SHA-256: {H1}",
            Assets =
            {
                new ReleaseAsset { Name = "RobotWinStudio-Setup-2.1.0.exe", DownloadUrl = "x", Sha256 = H2.ToLowerInvariant() },
                new ReleaseAsset { Name = "RobotWinStudio-Setup-2.1.0-tam.exe", DownloadUrl = "y" }
            }
        };
        Assert.Equal(H2.ToLowerInvariant(), ChecksumParser.Expected(two, two.Assets[0]));   // falls back to GitHub's digest
        Assert.Null(ChecksumParser.Expected(two, two.Assets[1]));
    }

    [Fact]
    public void NoNotesNoHashes() => Assert.Empty(ChecksumParser.Parse(null));
}

public class UpdateSelectionTests
{
    private static ReleaseInfo Release(string tag, bool pre = false, bool draft = false, params string[] assets) => new ReleaseInfo
    {
        Tag = tag,
        Version = SemVersion.Parse(tag),
        IsPrerelease = pre,
        IsDraft = draft,
        Assets = assets.Select(a => new ReleaseAsset { Name = a, DownloadUrl = "https://example.invalid/" + a }).ToList()
    };

    private static readonly ReleaseInfo Both = Release("v2.1.0", assets: new[] { "RobotWinStudio-Setup-2.1.0.exe", "RobotWinStudio-Setup-2.1.0-tam.exe", "SHA256SUMS.txt" });
    private static readonly ReleaseInfo LightOnly = Release("v2.0.0", assets: new[] { "RobotWinStudio-Setup-2.0.0.exe" });
    private static readonly ReleaseInfo FullOnly = Release("v2.0.5", assets: new[] { "RobotWinStudio-Setup-2.0.5-tam.exe" });

    [Fact]
    public void SelfContainedCopyGetsTheTamInstaller() =>
        Assert.Equal("RobotWinStudio-Setup-2.1.0-tam.exe", UpdateLogic.SelectAsset(Both, selfContained: true, runtimeAvailable: false)!.Name);

    [Fact]
    public void FrameworkDependentCopyGetsTheSmallInstaller() =>
        Assert.Equal("RobotWinStudio-Setup-2.1.0.exe", UpdateLogic.SelectAsset(Both, selfContained: false, runtimeAvailable: true)!.Name);

    [Fact]
    public void SelfContainedFallsBackOnlyWhenTheRuntimeIsInstalled()
    {
        Assert.Null(UpdateLogic.SelectAsset(LightOnly, selfContained: true, runtimeAvailable: false));
        Assert.Equal("RobotWinStudio-Setup-2.0.0.exe", UpdateLogic.SelectAsset(LightOnly, selfContained: true, runtimeAvailable: true)!.Name);
    }

    [Fact]
    public void FrameworkDependentFallsBackToTheFullInstaller() =>
        Assert.Equal("RobotWinStudio-Setup-2.0.5-tam.exe", UpdateLogic.SelectAsset(FullOnly, selfContained: false, runtimeAvailable: true)!.Name);

    // [setup] From 1.7 on: one setup and the components it downloads.
    private static readonly ReleaseInfo Components = Sized(Release("v1.7.0", assets: new[]
    {
        "RobotWinStudio-Setup-1.7.0.exe", "RobotWinStudio-1.7.0-app.rwpack", "RobotWinStudio-1.7.0-dotnet.rwpack",
        "RobotWinStudio-1.7.0-arduino.rwpack", "RobotWinStudio-1.7.0-occt.rwpack", "RobotWin-Headless-1.7.0-win-x64.zip", "SHA256SUMS.txt"
    }));

    private static ReleaseInfo Sized(ReleaseInfo r)
    {
        foreach (var a in r.Assets)
            a.Size = a.Name.EndsWith("-app.rwpack") ? 50 : a.Name.EndsWith("-dotnet.rwpack") ? 60 : a.Name.EndsWith("-arduino.rwpack") ? 80 : a.Name.EndsWith("-occt.rwpack") ? 15 : a.Name.EndsWith(".exe") ? 1 : 30;
        return r;
    }

    [Fact]
    public void EveryCopyGetsTheOneSetup()
    {
        Assert.True(Components.HasComponents);
        Assert.False(Both.HasComponents);
        foreach (bool own in new[] { true, false })
            foreach (bool runtime in new[] { true, false })
                Assert.Equal("RobotWinStudio-Setup-1.7.0.exe", UpdateLogic.SelectAsset(Components, own, runtime)!.Name);
    }

    [Fact]
    public void ComponentIdsComeFromTheNames()
    {
        Assert.Equal(new[] { "app", "dotnet", "arduino", "occt" }, Components.Assets.Where(a => a.IsComponent).Select(a => a.ComponentId).ToArray());
        Assert.Null(new ReleaseAsset { Name = "RobotWinStudio-Setup-1.7.0.exe" }.ComponentId);
    }

    [Fact]
    public void AnUpdateDownloadsTheSetupAndWhatTheCopyHas()
    {
        var setup = UpdateLogic.SelectAsset(Components, false, true)!;
        // a lite copy: the app; a custom one: what it has; a full one (or an older full package): every component
        Assert.Equal(1 + 50, UpdateLogic.DownloadSize(Components, setup, new InstallInfo { Kind = "lite", Components = new[] { "app" } }));
        Assert.Equal(1 + 50 + 15, UpdateLogic.DownloadSize(Components, setup, new InstallInfo { Kind = "custom", Components = new[] { "app", "occt" } }));
        Assert.Equal(1 + 50 + 60 + 80 + 15, UpdateLogic.DownloadSize(Components, setup, new InstallInfo { Kind = "full", Components = new[] { "app", "dotnet" } }));
        Assert.Equal(1 + 50 + 60 + 80 + 15, UpdateLogic.DownloadSize(Components, setup, new InstallInfo { SelfContained = true, Components = new[] { "app", "dotnet" } }));
        // an older release: its installer alone
        var legacy = UpdateLogic.SelectAsset(Both, false, true)!;
        legacy.Size = 120;
        Assert.Equal(120, UpdateLogic.DownloadSize(Both, legacy, new InstallInfo()));
    }

    [Fact]
    public void TheCopysContentsInALine()
    {
        Assert.Equal("Tam kurulum · .NET dahil · Arduino derleyicisi · STEP / IGES",
            new InstallInfo { Kind = "full", SelfContained = true, Components = new[] { "app", "dotnet", "arduino", "occt" } }.ContentText);
        Assert.Equal("Hafif kurulum · .NET 10 Masaüstü Çalışma Zamanı ile", new InstallInfo { Kind = "lite", Components = new[] { "app" } }.ContentText);
        Assert.Equal("Tam kurulum · .NET dahil · Arduino derleyicisi", new InstallInfo { SelfContained = true, Components = new[] { "app", "dotnet", "arduino" } }.ContentText);
    }

    [Fact]
    public void NoInstallerNoAsset() =>
        Assert.Null(UpdateLogic.SelectAsset(Release("v2.2.0", assets: new[] { "source.zip" }), false, true));

    [Fact]
    public void FindsTheNewestNewerRelease()
    {
        var releases = new[] { LightOnly, Both, FullOnly, Release("v2.2.0-beta.1", pre: true), Release("v9.0.0", draft: true) };
        var current = SemVersion.Parse("2.0.0")!;
        Assert.Equal("v2.1.0", UpdateLogic.FindUpdate(releases, current, includePrereleases: false, skippedVersion: null)!.Tag);
        Assert.Equal("v2.2.0-beta.1", UpdateLogic.FindUpdate(releases, current, includePrereleases: true, skippedVersion: null)!.Tag);
        Assert.Null(UpdateLogic.FindUpdate(releases, SemVersion.Parse("2.1.0")!, false, null));
    }

    [Fact]
    public void SkippedVersionIsNotOfferedButANewerOneIs()
    {
        var current = SemVersion.Parse("2.0.0")!;
        Assert.Null(UpdateLogic.FindUpdate(new[] { Both }, current, false, "2.1.0"));
        Assert.Equal("v2.1.0", UpdateLogic.FindUpdate(new[] { Both }, current, false, "2.0.5")!.Tag);
    }

    [Fact]
    public void PrereleaseTagsAreFilteredEvenIfNotFlagged() =>
        Assert.Null(UpdateLogic.FindUpdate(new[] { Release("v2.1.0-rc.1") }, SemVersion.Parse("2.0.0")!, false, null));
}

[Collection("Lang")]   // reads the language in use
public class InstallModeTests
{
    private const string Dir = @"C:\Users\a\AppData\Local\Programs\RobotWin Studio";

    [Fact]
    public void DetectsHowTheInstallerRecordedItself()
    {
        Assert.Equal(InstallMode.PerUser, UpdateLogic.DetectMode(Dir, Dir + "\\", null, true));
        Assert.Equal(InstallMode.AllUsers, UpdateLogic.DetectMode(@"C:\Program Files\RobotWin Studio", null, "\"c:\\program files\\robotwin studio\"", true));
        Assert.Equal(InstallMode.Portable, UpdateLogic.DetectMode(@"E:\RobotWin", Dir, null, true));
        Assert.Equal(InstallMode.Development, UpdateLogic.DetectMode(@"D:\src\builds\Debug", Dir, null, false));
    }

    [Fact]
    public void InstallerArgumentsKeepTheFolderAndChoices()
    {
        var info = new InstallInfo { Mode = InstallMode.PerUser, AppDirectory = Dir + "\\", DesktopShortcut = false, FileTypesRegistered = true };
        Assert.Equal($"/S /PASSIVE /NODESKTOP /LAUNCH \"/D={Dir}\"", UpdateLogic.InstallerArguments(info));

        info = new InstallInfo { Mode = InstallMode.AllUsers, AppDirectory = @"C:\Program Files\RobotWin Studio", FileTypesRegistered = false };
        Assert.Equal("/S /PASSIVE /ALLUSERS /NOTYPES /LAUNCH \"/D=C:\\Program Files\\RobotWin Studio\"", UpdateLogic.InstallerArguments(info));

        info = new InstallInfo { Mode = InstallMode.Portable, AppDirectory = @"E:\RobotWin", DesktopShortcut = false };
        Assert.Equal(@"/S /PASSIVE /PORTABLE /LAUNCH /D=E:\RobotWin", UpdateLogic.InstallerArguments(info));
    }

    [Fact]
    public void FormatsSizesInTurkish()
    {
        Assert.Equal("1,7 MB", UpdateLogic.FormatSize(1745344));
        Assert.Equal("12 KB", UpdateLogic.FormatSize(12 * 1024));
    }
}

[Collection("Lang")]   // reads the language in use
public class NewsTests
{
    [Fact]
    public void ShippedNewsFileIsValid()
    {
        var items = NewsParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "news.json")));
        Assert.NotEmpty(items);
        Assert.All(items, i =>
        {
            Assert.False(string.IsNullOrWhiteSpace(i.Id));
            Assert.False(string.IsNullOrWhiteSpace(i.Title));
            Assert.NotEqual(DateTimeOffset.MinValue, i.Date);
        });
        Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public void ParsesSchemaAndSkipsInvalidItems()
    {
        const string json = """
        {
          "version": 1,
          // comments and trailing commas are tolerated
          "items": [
            { "id": "a", "date": "2026-11-01", "title": "Eski", "body": "x\r\ny" },
            { "id": "b", "date": "2026-12-24T10:00:00+03:00", "title": "Yeni", "body": "**kalın**", "link": "https://example.org/b", "image": "https://example.org/b.png" },
            { "id": "", "title": "kimliksiz" },
            { "id": "c", "title": "" },
            { "id": "a", "title": "kopya" },
            { "id": "d", "title": "Tarihsiz", "link": "javascript:alert(1)" },
          ]
        }
        """;
        var items = NewsParser.Parse(json);
        Assert.Equal(new[] { "b", "a", "d" }, items.Select(i => i.Id));
        Assert.Equal("https://example.org/b", items[0].Link);
        Assert.Equal("https://example.org/b.png", items[0].Image);
        Assert.Equal("x\ny", items[1].Body);
        Assert.Null(items[2].Link);                      // only http(s) links are kept
        Assert.Equal(DateTimeOffset.MinValue, items[2].Date);
    }

    [Fact]
    public void AcceptsABareArrayAndOddRoots()
    {
        Assert.Single(NewsParser.Parse("""[ { "id": "x", "title": "T", "date": "2026-01-01" } ]"""));
        Assert.Empty(NewsParser.Parse("""{ "version": 1 }"""));
        Assert.Empty(NewsParser.Parse("42"));
    }

    [Fact]
    public void MergesReleaseNotesIntoTheFeed()
    {
        var news = NewsParser.Parse("""[ { "id": "hello", "title": "Merhaba", "date": "2026-11-25" } ]""");
        var releases = new[]
        {
            new ReleaseInfo { Tag = "v2.0.0", Version = SemVersion.Parse("2.0.0"), PublishedAt = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero), Body = "notlar", HtmlUrl = "https://github.com/x" },
            new ReleaseInfo { Tag = "v2.1.0", Version = SemVersion.Parse("2.1.0"), PublishedAt = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero) },
            new ReleaseInfo { Tag = "v9", IsDraft = true }
        };
        var feed = NewsParser.Merge(news, releases);
        Assert.Equal(new[] { "release:v2.1.0", "hello", "release:v2.0.0" }, feed.Select(f => f.Id));
        Assert.True(feed[0].IsRelease);
        Assert.Equal("RobotWin Studio 2.1.0", feed[0].Title);
        Assert.Equal("notlar", feed[2].Body);
    }
}

public class WhatsNewTests
{
    private static string Guide([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(here)!, "..", "..", "docs", "kullanim", "11-yenilikler.md"));

    [Fact]
    public void ReadsTheHeadlinesOfAVersionFromTheGuidePage()
    {
        var (summary, headlines) = WhatsNew.Headlines("1.3.0", Guide());
        Assert.NotNull(summary);
        Assert.Contains("Kablolar", summary);
        Assert.Equal("Kablo yolu", headlines[0]);
        Assert.Equal(7, headlines.Count);   // the "Yenilikler" list only, capped
        Assert.DoesNotContain(headlines, h => h.StartsWith("Teller", System.StringComparison.Ordinal));   // not the fixes
    }

    [Fact]
    public void AnOlderVersionHasItsOwnSectionAndAnUnknownOneNone()
    {
        var (_, older) = WhatsNew.Headlines("1.2.0", Guide());
        Assert.Contains(older, h => h.StartsWith("Bıçak", System.StringComparison.Ordinal));
        var (none, empty) = WhatsNew.Headlines("9.9.9", Guide());
        Assert.Null(none);
        Assert.Empty(empty);
    }
}

// [setup] install.json: what the one setup recorded about the installation it made.
public class InstallProfileTests
{
    [Fact]
    public void ReadsWhatTheSetupWrote()
    {
        var p = InstallProfile.Parse("{\n  \"product\": \"RobotWin Studio\",\n  \"version\": \"1.7.0\",\n  \"type\": \"custom\",\n  \"components\": [\"app\", \"OCCT\"],\n  \"language\": \"tr\",\n  \"where\": \"portable\",\n  \"source\": \"D:\\\\packs\",\n  \"installed\": \"2026-11-25T10:56:29Z\"\n}\n")!;
        Assert.Equal("1.7.0", p.Version);
        Assert.Equal("custom", p.Type);
        Assert.Equal(new[] { "app", "occt" }, p.Components);
        Assert.True(p.Has("occt"));
        Assert.False(p.Has("arduino"));
        Assert.Equal("tr", p.Language);
        Assert.Equal("portable", p.Where);
    }

    [Fact]
    public void ABrokenOrOddRecordIsNoRecord()
    {
        Assert.Null(InstallProfile.Parse("{ \"version\": "));
        Assert.Null(InstallProfile.Parse("[1, 2]"));
        var empty = InstallProfile.Parse("{}")!;
        Assert.Empty(empty.Components);
        Assert.Equal(string.Empty, empty.Language);
    }

    [Fact]
    public void AnOlderSetupsCopyIsReadOffItsFolder()
    {
        string dir = Path.Combine(Path.GetTempPath(), "rw-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "tools", "arduino"));
            File.WriteAllText(Path.Combine(dir, "tools", "arduino", "arduino-cli.exe"), string.Empty);
            Assert.Equal(new[] { "app", "arduino" }, InstallProfile.FromDisk(dir));
            File.WriteAllText(Path.Combine(dir, "coreclr.dll"), string.Empty);   // the full package carried .NET beside the exe
            Directory.CreateDirectory(Path.Combine(dir, "occt"));
            File.WriteAllText(Path.Combine(dir, "occt", "RobotWin.Occt.dll"), string.Empty);
            Assert.Equal(new[] { "app", "dotnet", "arduino", "occt" }, InstallProfile.FromDisk(dir));
            Assert.Null(InstallProfile.Read(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
