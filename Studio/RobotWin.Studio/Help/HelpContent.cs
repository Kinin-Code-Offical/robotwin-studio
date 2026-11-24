using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RobotWin.Studio.Help
{
    public enum HelpTopicKind
    {
        /// <summary>A page of the user guide (Markdown file).</summary>
        Guide,

        /// <summary>A heading inside the index without its own file.</summary>
        Group,

        Shortcuts,
        News,
        Updates,
        About,

        /// <summary>The guide is not bundled with this copy.</summary>
        Missing
    }

    public sealed class HelpTopic
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public HelpTopicKind Kind { get; set; }
        public string? FilePath { get; set; }
        public List<HelpTopic> Children { get; } = new List<HelpTopic>();
        public HelpTopic? Parent { get; set; }

        public override string ToString() => Title;
    }

    /// <summary>
    /// The user guide bundled with the app: <c>Docs/kullanim/*.md</c> (index <c>README.md</c>) and the
    /// pictures in <c>Docs/images</c>, copied from the repository's <c>docs/</c> at build time.
    /// </summary>
    public sealed class HelpLibrary
    {
        /// <summary>The documentation site (GitHub Pages): guide, developer docs, downloads.</summary>
        public const string DocsSite = "https://kinin-code-offical.github.io/robotwin-studio/";
        /// <summary>
        /// [lang] The guide's folder under docs/ for the language in use: Turkish "kullanim", English "en/guide", another
        /// language its own "xx/guide" when it has one, else the English guide.
        /// </summary>
        public static string GuideFolder
        {
            get
            {
                string lang = Shell.Lang.Current;
                if (lang == "tr") return "kullanim";
                string own = lang + "/guide";
                return Directory.Exists(Path.Combine(AppContext.BaseDirectory, "Docs", own)) ? own : "en/guide";
            }
        }

        /// <summary>The "what's new" page of the guide in use.</summary>
        public static string WhatsNewPage => Shell.Lang.Current == "tr" ? "11-yenilikler.md" : "11-whats-new.md";

        public static string OnlineGuide => DocsSite + GuideFolder + "/";
        private static string RepoBlob => "https://github.com/Kinin-Code-Offical/robotwin-studio/blob/main/docs/" + GuideFolder + "/";

        private readonly Dictionary<string, MarkdownDocument> _docs = new Dictionary<string, MarkdownDocument>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _text = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private HelpLibrary(string? root)
        {
            Root = root;
        }

        /// <summary>Folder of the guide (null when it is not bundled).</summary>
        public string? Root { get; }

        public List<HelpTopic> Guide { get; } = new List<HelpTopic>();

        public IEnumerable<HelpTopic> AllGuidePages => Flatten(Guide).Where(t => t.Kind == HelpTopicKind.Guide);

        public static IEnumerable<HelpTopic> Flatten(IEnumerable<HelpTopic> topics)
        {
            foreach (var t in topics)
            {
                yield return t;
                foreach (var c in Flatten(t.Children)) yield return c;
            }
        }

        /// <summary>
        /// Finds the guide: <c>ROBOTWIN_HELP_DIR</c> (any folder with a README.md, for previews of unreleased docs),
        /// then the guide of the language in use next to the exe (<see cref="GuideFolder"/>), then the Turkish one.
        /// </summary>
        public static HelpLibrary Load()
        {
            string? root = null;
            string? env = Environment.GetEnvironmentVariable("ROBOTWIN_HELP_DIR");
            if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env)) root = Path.GetFullPath(env);
            else
            {
                foreach (var folder in new[] { GuideFolder, "kullanim" })
                {
                    string bundled = Path.Combine(AppContext.BaseDirectory, "Docs", folder.Replace('/', Path.DirectorySeparatorChar));
                    if (Directory.Exists(bundled) && Directory.EnumerateFiles(bundled, "*.md").Any())
                    {
                        root = bundled;
                        break;
                    }
                }
            }

            var lib = new HelpLibrary(root);
            lib.Build();
            return lib;
        }

        private void Build()
        {
            if (Root == null) return;
            string index = Path.Combine(Root, "README.md");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(index))
            {
                var doc = GetDocument(index);
                var home = new HelpTopic { Id = Id(index), Title = doc.Title ?? "Kullanım kılavuzu", Kind = HelpTopicKind.Guide, FilePath = index };
                Guide.Add(home);
                seen.Add(index);

                // Every link to a local page, in reading order; second-level headings of the index become groups.
                HelpTopic? group = null;
                foreach (var block in doc.Blocks)
                {
                    if (block is MdHeading h && h.Level == 2)
                    {
                        group = new HelpTopic { Id = "group:" + h.Anchor, Title = h.Text, Kind = HelpTopicKind.Group };
                        continue;
                    }

                    foreach (var (title, path) in Links(block, Root))
                    {
                        if (!seen.Add(path)) continue;
                        var page = new HelpTopic { Id = Id(path), Title = CleanTitle(title, path), Kind = HelpTopicKind.Guide, FilePath = path };
                        if (group != null)
                        {
                            if (!Guide.Contains(group)) Guide.Add(group);
                            page.Parent = group;
                            group.Children.Add(page);
                        }
                        else
                        {
                            Guide.Add(page);
                        }
                    }
                }
            }

            // Pages the index forgot, by file name (01-…, 02-… sort naturally).
            foreach (var file in Directory.EnumerateFiles(Root, "*.md").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string full = Path.GetFullPath(file);
                if (!seen.Add(full)) continue;
                Guide.Add(new HelpTopic { Id = Id(full), Title = GetDocument(full).Title ?? Path.GetFileNameWithoutExtension(full), Kind = HelpTopicKind.Guide, FilePath = full });
            }

            // Empty groups (their links pointed outside the guide) are dropped.
            Guide.RemoveAll(t => t.Kind == HelpTopicKind.Group && t.Children.Count == 0);

            // A single group ("İçindekiler") is just the table of contents: list its pages directly.
            var groups = Guide.Where(t => t.Kind == HelpTopicKind.Group).ToList();
            if (groups.Count == 1)
            {
                int at = Guide.IndexOf(groups[0]);
                Guide.RemoveAt(at);
                foreach (var c in groups[0].Children) c.Parent = null;
                Guide.InsertRange(at, groups[0].Children);
            }
        }

        private static string CleanTitle(string linkText, string path)
        {
            string t = linkText.Trim().Trim('*', '_', '`').Trim();
            if (t.Length == 0 || t.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return Path.GetFileNameWithoutExtension(path);
            return t;
        }

        /// <summary>Local .md links in a block; tables are read column by column (an index laid out in two columns stays in order).</summary>
        private static IEnumerable<(string Title, string Path)> Links(MdBlock block, string root)
        {
            IEnumerable<IEnumerable<MdInline>> runs = block switch
            {
                MdParagraph p => new[] { p.Content },
                MdList l => MarkdownDocument.Walk(new[] { (MdBlock)l }).OfType<MdParagraph>().Select(p => (IEnumerable<MdInline>)p.Content),
                MdTable t => Enumerable.Range(0, t.Header.Count).SelectMany(c => new[] { t.Header[c] }.Concat(t.Rows.Where(r => c < r.Count).Select(r => r[c]))).Select(c => (IEnumerable<MdInline>)c),
                _ => Array.Empty<IEnumerable<MdInline>>()
            };
            foreach (var run in runs)
            {
                foreach (var link in LinksIn(run))
                {
                    string? path = ResolveLocal(root, link.Url);
                    if (path != null && path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && File.Exists(path) && IsInside(root, path))
                        yield return (MdInline.PlainText(link.Children), path);
                }
            }
        }

        private static IEnumerable<MdLink> LinksIn(IEnumerable<MdInline> inlines)
        {
            foreach (var i in inlines)
            {
                if (i is MdLink l) yield return l;
                else if (i is MdStyled s)
                    foreach (var x in LinksIn(s.Children)) yield return x;
            }
        }

        public static bool IsInside(string root, string path) =>
            Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        /// <summary>A relative link ("02-devre.md#kablo") resolved against a folder; null for web links and pure anchors.</summary>
        public static string? ResolveLocal(string baseDir, string url)
        {
            if (string.IsNullOrWhiteSpace(url) || url.StartsWith("#", StringComparison.Ordinal) || Uri.TryCreate(url, UriKind.Absolute, out var u) && !u.IsFile) return null;
            string file = url.Split('#')[0].Split('?')[0];
            try
            {
                return Path.GetFullPath(Path.Combine(baseDir, Uri.UnescapeDataString(file).Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }
        }

        public static string? AnchorOf(string url)
        {
            int hash = url.IndexOf('#');
            return hash >= 0 && hash < url.Length - 1 ? Uri.UnescapeDataString(url.Substring(hash + 1)) : null;
        }

        /// <summary>The page on GitHub for a file of the guide that is not bundled (e.g. a link to the repository's LICENSE).</summary>
        public string OnlineUrl(string relativeFromRoot) => RepoBlob + relativeFromRoot.Replace('\\', '/');

        public string Id(string path) => "guide:" + (Root != null && IsInside(Root, path) ? Path.GetRelativePath(Root, path) : Path.GetFileName(path)).Replace('\\', '/');

        public HelpTopic? FindByPath(string path) => AllGuidePages.FirstOrDefault(t => string.Equals(t.FilePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));

        public MarkdownDocument GetDocument(string path)
        {
            if (_docs.TryGetValue(path, out var d)) return d;
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                text = "# Okunamadı\n\n" + ex.Message;
            }

            d = MarkdownParser.Parse(text);
            _docs[path] = d;
            return d;
        }

        public string GetPlainText(HelpTopic t)
        {
            if (t.FilePath == null) return string.Empty;
            if (_text.TryGetValue(t.FilePath, out var s)) return s;
            s = GetDocument(t.FilePath).PlainText();
            _text[t.FilePath] = s;
            return s;
        }
    }
}
