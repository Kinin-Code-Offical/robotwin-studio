using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RobotWin.Studio.Help
{
    /// <summary>Where a rendered document lives and what its links do.</summary>
    public sealed class MarkdownContext
    {
        /// <summary>Folder of the Markdown file (relative images and links resolve against it).</summary>
        public string? BaseDirectory { get; set; }

        /// <summary>Called with the raw link target when a link is clicked.</summary>
        public Action<string>? OnLink { get; set; }

        /// <summary>Text to highlight (search), accent- and case-insensitive.</summary>
        public string? Highlight { get; set; }

        /// <summary>Added to every heading level (release notes inside the news feed sit one level lower).</summary>
        public int HeadingOffset { get; set; }

        /// <summary>Filled while rendering: heading anchor → block (for #links and the topic tree).</summary>
        public Dictionary<string, Block> Anchors { get; } = new Dictionary<string, Block>(StringComparer.Ordinal);

        /// <summary>Filled while rendering: the highlighted runs, in document order.</summary>
        public List<TextElement> Highlights { get; } = new List<TextElement>();
    }

    /// <summary>Renders <see cref="MarkdownDocument"/> into a WPF FlowDocument with the app's flat theme.</summary>
    public static class MarkdownView
    {
        private static T R<T>(string key) => (T)Application.Current.FindResource(key);

        public static FlowDocument CreateDocument()
        {
            var doc = new FlowDocument
            {
                FontFamily = R<FontFamily>("F.Ui"),
                FontSize = 14,
                Foreground = R<Brush>("B.TextSecondary"),
                Background = Brushes.Transparent,
                PagePadding = new Thickness(0),
                TextAlignment = TextAlignment.Left,
                LineHeight = 22,
                IsHyphenationEnabled = false,
                ColumnWidth = double.PositiveInfinity
            };
            return doc;
        }

        public static FlowDocument Render(MarkdownDocument md, MarkdownContext ctx)
        {
            var doc = CreateDocument();
            doc.Blocks.AddRange(RenderBlocks(md, ctx));
            return doc;
        }

        public static List<Block> RenderBlocks(MarkdownDocument md, MarkdownContext ctx) => md.Blocks.Select(b => Block(b, ctx, 0)).Where(b => b != null).Select(b => b!).ToList();

        /// <summary>A read-only viewer that scrolls (guide pages).</summary>
        public static FlowDocumentScrollViewer ScrollViewer(FlowDocument doc) => new FlowDocumentScrollViewer
        {
            Document = doc,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsToolBarVisible = false,
            Zoom = 100,
            Focusable = true,
            SelectionBrush = R<Brush>("B.Accent")
        };

        /// <summary>A read-only viewer that grows with its content (cards inside a scrolling list).</summary>
        public static RichTextBox InlineViewer(FlowDocument doc)
        {
            var box = new RichTextBox
            {
                Document = doc,
                IsReadOnly = true,
                IsDocumentEnabled = true,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Foreground = R<Brush>("B.TextSecondary"),
                CaretBrush = Brushes.Transparent,
                FocusVisualStyle = null,
                SelectionBrush = R<Brush>("B.Accent"),
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            // RichTextBox has its own chrome; strip it to the bare document.
            box.Template = BareTemplate();
            return box;
        }

        private static ControlTemplate? _bare;

        private static ControlTemplate BareTemplate()
        {
            if (_bare != null) return _bare;
            var t = new ControlTemplate(typeof(RichTextBox));
            var host = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
            host.SetValue(System.Windows.Controls.ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            host.SetValue(System.Windows.Controls.ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            host.SetValue(UIElement.FocusableProperty, false);
            t.VisualTree = host;
            _bare = t;
            return t;
        }

        // ------------------------------------------------------------------ blocks

        private static Block? Block(MdBlock b, MarkdownContext ctx, int depth)
        {
            switch (b)
            {
                case MdHeading h:
                {
                    int level = Math.Clamp(h.Level + ctx.HeadingOffset, 1, 6);
                    var p = new Paragraph
                    {
                        FontFamily = level <= 2 ? R<FontFamily>("F.Display") : R<FontFamily>("F.Ui"),
                        FontSize = level switch { 1 => 26, 2 => 20, 3 => 16, 4 => 14.5, _ => 13.5 },
                        FontWeight = level <= 2 ? FontWeights.Normal : FontWeights.SemiBold,
                        Foreground = R<Brush>("B.Text"),
                        Margin = new Thickness(0, level == 1 ? 0 : level == 2 ? 26 : 18, 0, level <= 2 ? 10 : 6),
                        LineHeight = double.NaN
                    };
                    AddInlines(p.Inlines, h.Content, ctx);
                    if (level <= 2)
                    {
                        // A hairline under the big headings, like GitHub.
                        p.BorderBrush = R<Brush>("B.Border");
                        p.BorderThickness = new Thickness(0, 0, 0, 1);
                        p.Padding = new Thickness(0, 0, 0, 8);
                    }

                    if (h.Anchor.Length > 0 && !ctx.Anchors.ContainsKey(h.Anchor)) ctx.Anchors[h.Anchor] = p;
                    return p;
                }

                case MdParagraph para:
                {
                    // A paragraph that is only an image (or images) becomes a picture block.
                    var visible = para.Content.Where(i => !(i is MdText t && string.IsNullOrWhiteSpace(t.Text)) && i is not MdLineBreak).ToList();
                    if (visible.Count > 0 && visible.All(i => i is MdImage))
                    {
                        var sec = new Section { Margin = new Thickness(0, 4, 0, 14) };
                        foreach (MdImage img in visible) sec.Blocks.Add(ImageBlock(img, ctx));
                        return sec;
                    }

                    var p = new Paragraph { Margin = new Thickness(0, 0, 0, 12) };
                    AddInlines(p.Inlines, para.Content, ctx);
                    return p;
                }

                case MdCodeBlock code:
                {
                    var p = new Paragraph
                    {
                        FontFamily = R<FontFamily>("F.Mono"),
                        FontSize = 12.5,
                        LineHeight = 19,
                        Foreground = R<Brush>("B.Text"),
                        Background = R<Brush>("B.Raised"),
                        BorderBrush = R<Brush>("B.Border"),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(14, 10, 14, 10),
                        Margin = new Thickness(0, 2, 0, 14)
                    };
                    var lines = code.Code.Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (i > 0) p.Inlines.Add(new LineBreak());
                        AddText(p.Inlines, lines[i], ctx);
                    }

                    return p;
                }

                case MdList list:
                {
                    var l = new List
                    {
                        MarkerStyle = list.Ordered ? TextMarkerStyle.Decimal : depth % 2 == 0 ? TextMarkerStyle.Disc : TextMarkerStyle.Circle,
                        StartIndex = Math.Max(1, list.Start),
                        Margin = new Thickness(0, 0, 0, depth == 0 ? 12 : 2),
                        Padding = new Thickness(24, 0, 0, 0),
                        MarkerOffset = 8
                    };
                    foreach (var item in list.Items)
                    {
                        var li = new ListItem();
                        foreach (var inner in item.Blocks)
                        {
                            var blk = Block(inner, ctx, depth + 1);
                            if (blk == null) continue;
                            if (blk is Paragraph ip && inner is MdParagraph) ip.Margin = new Thickness(0, 0, 0, 4);
                            li.Blocks.Add(blk);
                        }

                        if (item.Checked != null)
                        {
                            var first = li.Blocks.FirstBlock as Paragraph ?? new Paragraph();
                            if (li.Blocks.FirstBlock == null) li.Blocks.Add(first);
                            var box = new Run(item.Checked == true ? "☑ " : "☐ ") { Foreground = R<Brush>(item.Checked == true ? "B.Success" : "B.TextMuted") };
                            if (first.Inlines.FirstInline != null) first.Inlines.InsertBefore(first.Inlines.FirstInline, box);
                            else first.Inlines.Add(box);
                        }

                        if (li.Blocks.Count == 0) li.Blocks.Add(new Paragraph());
                        l.ListItems.Add(li);
                    }

                    return l;
                }

                case MdTable table:
                    return Table(table, ctx);

                case MdQuote q:
                {
                    var sec = new Section
                    {
                        BorderBrush = R<Brush>("B.Accent"),
                        BorderThickness = new Thickness(3, 0, 0, 0),
                        Padding = new Thickness(14, 2, 0, 2),
                        Margin = new Thickness(0, 2, 0, 14),
                        Foreground = R<Brush>("B.TextSecondary")
                    };
                    foreach (var inner in q.Blocks)
                    {
                        var blk = Block(inner, ctx, depth);
                        if (blk == null) continue;
                        if (blk is Paragraph ip && inner is MdParagraph) ip.Margin = new Thickness(0, 2, 0, 2);
                        sec.Blocks.Add(blk);
                    }

                    return sec;
                }

                case MdRule:
                    return new BlockUIContainer(new Border { Height = 1, Background = R<Brush>("B.Border"), Margin = new Thickness(0, 10, 0, 10) });
            }

            return null;
        }

        private static Block Table(MdTable t, MarkdownContext ctx)
        {
            var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 2, 0, 16), BorderBrush = R<Brush>("B.Border"), BorderThickness = new Thickness(1, 1, 0, 0) };
            int cols = Math.Max(1, t.Header.Count);
            // FlowDocument tables cannot size columns to their content; weight them by their longest text instead. The
            // weights are normalized to sum to 1: with large raw star values the layout squeezed the last column to a
            // letter's width.
            var weights = new double[cols];
            for (int c = 0; c < cols; c++)
            {
                int longest = new[] { c < t.Header.Count ? t.Header[c] : null }.Concat(t.Rows.Select(r => c < r.Count ? r[c] : null))
                    .Where(x => x != null).Select(x => MdInline.PlainText(x!).Length).DefaultIfEmpty(0).Max();
                weights[c] = Math.Clamp(longest, 8, 60);
            }

            double total = weights.Sum();
            foreach (double w in weights) table.Columns.Add(new TableColumn { Width = new GridLength(w / total, GridUnitType.Star) });
            var group = new TableRowGroup();
            table.RowGroups.Add(group);
            bool emptyHeader = t.Header.All(h => MdInline.PlainText(h).Trim().Length == 0);
            if (!emptyHeader) group.Rows.Add(Row(t.Header, t, ctx, true));
            foreach (var r in t.Rows) group.Rows.Add(Row(r, t, ctx, false));
            return table;
        }

        private static TableRow Row(List<List<MdInline>> cells, MdTable t, MarkdownContext ctx, bool header)
        {
            var row = new TableRow();
            if (header) row.Background = R<Brush>("B.Raised");
            for (int c = 0; c < Math.Max(1, t.Header.Count); c++)
            {
                var p = new Paragraph { Margin = new Thickness(0), LineHeight = 20, FontSize = 13.5 };
                if (header)
                {
                    p.FontWeight = FontWeights.SemiBold;
                    p.Foreground = R<Brush>("B.Text");
                }

                if (c < cells.Count) AddInlines(p.Inlines, cells[c], ctx);
                p.TextAlignment = (c < t.Align.Count ? t.Align[c] : MdAlign.None) switch
                {
                    MdAlign.Center => TextAlignment.Center,
                    MdAlign.Right => TextAlignment.Right,
                    _ => TextAlignment.Left
                };
                row.Cells.Add(new TableCell(p)
                {
                    Padding = new Thickness(10, 6, 10, 6),
                    BorderBrush = R<Brush>("B.Border"),
                    BorderThickness = new Thickness(0, 0, 1, 1)
                });
            }

            return row;
        }

        // ------------------------------------------------------------------ images

        private static Block ImageBlock(MdImage img, MarkdownContext ctx)
        {
            var source = LoadImage(img.Source, ctx.BaseDirectory);
            if (source == null)
            {
                // Remote or missing pictures are not fetched: show what they are instead.
                var p = new Paragraph { Foreground = R<Brush>("B.TextMuted"), FontStyle = FontStyles.Italic, Margin = new Thickness(0, 0, 0, 6) };
                p.Inlines.Add(new Run("[Görsel: " + (img.Alt.Length > 0 ? img.Alt : Path.GetFileName(img.Source)) + "]"));
                return p;
            }

            var image = new Image { Source = source, Stretch = Stretch.Uniform, ToolTip = img.Alt.Length > 0 ? img.Alt : null };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            // Natural size in DIPs, never larger than the column (Viewbox shrinks, never enlarges).
            image.Width = source.Width;
            image.Height = source.Height;
            var frame = new Border
            {
                BorderBrush = R<Brush>("B.Border"),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = image }
            };
            var block = new BlockUIContainer(frame) { Margin = new Thickness(0, 0, 0, 6) };
            if (img.Alt.Length > 0)
            {
                var sec = new Section();
                sec.Blocks.Add(block);
                sec.Blocks.Add(new Paragraph(new Run(img.Alt)) { FontSize = 12, Foreground = R<Brush>("B.TextMuted"), Margin = new Thickness(0, 0, 0, 8) });
                return sec;
            }

            return block;
        }

        /// <summary>Local files relative to the page, or https images on GitHub's own hosts; anything else is not loaded.</summary>
        public static BitmapSource? LoadImage(string src, string? baseDir)
        {
            try
            {
                if (Uri.TryCreate(src, UriKind.Absolute, out var abs) && (abs.Scheme == Uri.UriSchemeHttps || abs.Scheme == Uri.UriSchemeHttp))
                {
                    if (abs.Scheme != Uri.UriSchemeHttps || !IsTrustedImageHost(abs.Host) || abs.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return null;
                    var web = new BitmapImage();
                    web.BeginInit();
                    web.UriSource = abs;
                    web.CacheOption = BitmapCacheOption.OnLoad;
                    web.EndInit();
                    return web;
                }

                if (baseDir == null) return null;
                string path = Path.GetFullPath(Path.Combine(baseDir, Uri.UnescapeDataString(src.Split('?', '#')[0]).Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(path) || path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex) when (ex is IOException || ex is UriFormatException || ex is NotSupportedException || ex is ArgumentException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                return null;
            }
        }

        private static bool IsTrustedImageHost(string host) =>
            host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);

        // ------------------------------------------------------------------ inlines

        private static void AddInlines(InlineCollection into, IEnumerable<MdInline> inlines, MarkdownContext ctx)
        {
            foreach (var i in inlines)
            {
                switch (i)
                {
                    case MdText t:
                        AddText(into, t.Text, ctx);
                        break;
                    case MdLineBreak:
                        into.Add(new LineBreak());
                        break;
                    case MdCode c:
                    {
                        var span = new Span
                        {
                            FontFamily = R<FontFamily>("F.Mono"),
                            FontSize = 12.5,
                            Foreground = R<Brush>("B.Text"),
                            Background = R<Brush>("B.Raised")
                        };
                        AddText(span.Inlines, " " + c.Code + " ", ctx);
                        into.Add(span);
                        break;
                    }

                    case MdStyled s:
                    {
                        Span span = s.Style switch
                        {
                            MdStyle.Bold => new Bold { Foreground = R<Brush>("B.Text") },
                            MdStyle.Italic => new Italic(),
                            _ => new Span { TextDecorations = TextDecorations.Strikethrough }
                        };
                        AddInlines(span.Inlines, s.Children, ctx);
                        into.Add(span);
                        break;
                    }

                    case MdLink l:
                    {
                        var link = new Hyperlink
                        {
                            Foreground = R<Brush>("B.Accent"),
                            TextDecorations = null,
                            Cursor = System.Windows.Input.Cursors.Hand,
                            ToolTip = l.Url
                        };
                        string url = l.Url;
                        link.Click += (_, e) =>
                        {
                            ctx.OnLink?.Invoke(url);
                            e.Handled = true;
                        };
                        link.MouseEnter += (_, _) => link.TextDecorations = TextDecorations.Underline;
                        link.MouseLeave += (_, _) => link.TextDecorations = null;
                        AddInlines(link.Inlines, l.Children, ctx);
                        into.Add(link);
                        break;
                    }

                    case MdImage img:
                    {
                        // Inline picture (e.g. a badge): small, or its alt text when not loadable.
                        var src = LoadImage(img.Source, ctx.BaseDirectory);
                        if (src != null)
                        {
                            var im = new Image { Source = src, Height = Math.Min(src.Height, 20), Stretch = Stretch.Uniform, ToolTip = img.Alt };
                            into.Add(new InlineUIContainer(im) { BaselineAlignment = BaselineAlignment.Center });
                        }
                        else if (img.Alt.Length > 0)
                        {
                            into.Add(new Run(img.Alt) { Foreground = R<Brush>("B.TextMuted") });
                        }

                        break;
                    }
                }
            }
        }

        private static Brush? _highlight;

        /// <summary>The accent at ~35 % opacity: a flat marker that keeps the text readable.</summary>
        private static Brush HighlightBrush
        {
            get
            {
                if (_highlight != null) return _highlight;
                var a = R<Color>("C.Accent");
                var b = new SolidColorBrush(Color.FromArgb(0x5A, a.R, a.G, a.B));
                b.Freeze();
                return _highlight = b;
            }
        }

        /// <summary>Plain text, with the search term (if any) marked.</summary>
        private static void AddText(InlineCollection into, string text, MarkdownContext ctx)
        {
            string? q = ctx.Highlight?.Trim();
            if (string.IsNullOrEmpty(q) || q.Length < 2)
            {
                into.Add(new Run(text));
                return;
            }

            string ft = HelpSearch.Fold(text), fq = HelpSearch.Fold(q);
            int at = 0;
            for (int hit = ft.IndexOf(fq, StringComparison.Ordinal); hit >= 0; hit = ft.IndexOf(fq, at, StringComparison.Ordinal))
            {
                if (hit > at) into.Add(new Run(text.Substring(at, hit - at)));
                var mark = new Run(text.Substring(hit, fq.Length)) { Background = HighlightBrush, Foreground = R<Brush>("B.Text") };
                into.Add(mark);
                ctx.Highlights.Add(mark);
                at = hit + fq.Length;
            }

            if (at < text.Length) into.Add(new Run(text.Substring(at)));
        }
    }
}
