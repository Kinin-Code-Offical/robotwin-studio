using RobotWin.Studio.Help;

namespace RobotWin.Studio.Tests;

public class MarkdownTests
{
    [Fact]
    public void HeadingsGetGitHubAnchors()
    {
        var doc = MarkdownParser.Parse("# Kurulum ve İlk Adımlar\n\n## Sık sorulan sorular?\n\n## Sık sorulan sorular?\n\nAlt başlık\n---\n");
        var h = doc.Headings.ToList();
        Assert.Equal(4, h.Count);
        Assert.Equal("Kurulum ve İlk Adımlar", doc.Title);
        Assert.Equal("kurulum-ve-ilk-adımlar", h[0].Anchor);
        Assert.Equal("sık-sorulan-sorular", h[1].Anchor);
        Assert.Equal("sık-sorulan-sorular-1", h[2].Anchor);
        Assert.Equal(2, h[3].Level);
        Assert.Equal("Alt başlık", h[3].Text);
    }

    [Fact]
    public void InlineStyles()
    {
        var p = Assert.IsType<MdParagraph>(Assert.Single(MarkdownParser.Parse("**kalın** ve *italik*, _alt_ `kod *değil*` ~~eski~~ snake_case_ad 2 * 3").Blocks));
        var c = p.Content;
        Assert.Equal(MdStyle.Bold, Assert.IsType<MdStyled>(c[0]).Style);
        Assert.Contains(c, i => i is MdStyled { Style: MdStyle.Italic } s && MdInline.PlainText(s.Children) == "italik");
        Assert.Contains(c, i => i is MdStyled { Style: MdStyle.Italic } s && MdInline.PlainText(s.Children) == "alt");
        Assert.Contains(c, i => i is MdCode { Code: "kod *değil*" });
        Assert.Contains(c, i => i is MdStyled { Style: MdStyle.Strike });
        Assert.EndsWith("snake_case_ad 2 * 3", MdInline.PlainText(c));
    }

    [Fact]
    public void LinksImagesAndAutolinks()
    {
        var p = (MdParagraph)MarkdownParser.Parse("Bkz. [kurulum](kurulum.md#ilk-adim) ![ekran](../images/home.png \"Başlangıç\") <https://example.org> ve https://github.com/x.").Blocks[0];
        var links = p.Content.OfType<MdLink>().ToList();
        Assert.Equal("kurulum.md#ilk-adim", links[0].Url);
        Assert.Equal("https://example.org", links[1].Url);
        Assert.Equal("https://github.com/x", links[2].Url);   // trailing dot is not part of the URL
        var img = Assert.Single(p.Content.OfType<MdImage>());
        Assert.Equal("../images/home.png", img.Source);
        Assert.Equal("ekran", img.Alt);
    }

    [Fact]
    public void BadgeImageInsideLink()
    {
        var p = (MdParagraph)MarkdownParser.Parse("[![License: MIT](https://img.shields.io/x.svg)](LICENSE)").Blocks[0];
        var link = Assert.IsType<MdLink>(Assert.Single(p.Content));
        Assert.Equal("LICENSE", link.Url);
        Assert.IsType<MdImage>(Assert.Single(link.Children));
    }

    [Fact]
    public void Lists()
    {
        const string md = "- bir\n- iki\n  devam\n  - iç içe\n- [x] bitti\n\n1. ilk\n2. ikinci\n\n   ikinci paragraf\n3. üçüncü\n";
        var blocks = MarkdownParser.Parse(md).Blocks;
        var ul = Assert.IsType<MdList>(blocks[0]);
        Assert.False(ul.Ordered);
        Assert.Equal(3, ul.Items.Count);
        Assert.Equal("iki devam", MdInline.PlainText(((MdParagraph)ul.Items[1].Blocks[0]).Content));
        Assert.IsType<MdList>(ul.Items[1].Blocks[1]);
        Assert.True(ul.Items[2].Checked);
        var ol = Assert.IsType<MdList>(blocks[1]);
        Assert.True(ol.Ordered);
        Assert.Equal(3, ol.Items.Count);
        Assert.Equal(2, ol.Items[1].Blocks.Count);
    }

    [Fact]
    public void Tables()
    {
        const string md = "| Tuş | İş |\n| :--- | ---: |\n| `Ctrl+S` | Kaydet |\n| a \\| b | c |\n";
        var t = Assert.IsType<MdTable>(Assert.Single(MarkdownParser.Parse(md).Blocks));
        Assert.Equal(2, t.Header.Count);
        Assert.Equal(new[] { MdAlign.Left, MdAlign.Right }, t.Align);
        Assert.Equal(2, t.Rows.Count);
        Assert.IsType<MdCode>(t.Rows[0][0][0]);
        Assert.Equal("a | b", MdInline.PlainText(t.Rows[1][0]));
    }

    [Fact]
    public void CodeQuotesRulesAndHtml()
    {
        const string md = "```powershell\ndotnet build\n  # girintili\n```\n\n> Not: **dikkat**\n> ikinci satır\n\n---\n\n<!-- yorum -->\n<p align=\"center\">Ortada<br>metin</p>\n";
        var b = MarkdownParser.Parse(md).Blocks;
        var code = Assert.IsType<MdCodeBlock>(b[0]);
        Assert.Equal("powershell", code.Language);
        Assert.Equal("dotnet build\n  # girintili", code.Code);
        var q = Assert.IsType<MdQuote>(b[1]);
        Assert.Equal("Not: dikkat ikinci satır", MdInline.PlainText(((MdParagraph)q.Blocks[0]).Content));
        Assert.IsType<MdRule>(b[2]);
        var html = Assert.IsType<MdParagraph>(b[3]);
        Assert.Equal("Ortada\nmetin", MdInline.PlainText(html.Content));
    }

    [Fact]
    public void ParsesTheRepositoryReadme()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "RobotWinStudio.sln"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var doc = MarkdownParser.Parse(File.ReadAllText(Path.Combine(dir!, "README.md")));
        Assert.NotNull(doc.Title);
        Assert.Contains(MarkdownDocument.Walk(doc.Blocks), b => b is MdTable);
        Assert.Contains(MarkdownDocument.Walk(doc.Blocks), b => b is MdCodeBlock);
        Assert.True(doc.PlainText().Length > 1000);
    }

    [Fact]
    public void SearchIsCaseAndAccentTolerant()
    {
        var hit = HelpSearch.Match("kurulum", "Kurulum", "Uygulamayı KURMAK için Şimdi güncelle düğmesine basın. Sonra şimdi tekrar.", "simdi")!;
        Assert.Equal(2, hit.Count);
        Assert.Contains("Şimdi güncelle", hit.Snippet);
        Assert.Null(HelpSearch.Match("x", "Başlık", "metin", "yok"));
        Assert.Null(HelpSearch.Match("x", "Başlık", "metin", "m"));               // too short
        Assert.Equal(1, HelpSearch.Match("x", "Işık", "metin", "isik")!.Count);  // title hit
    }
}
