using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

// Pure update logic: no WPF, no registry, no network. Linked into RobotWin.Studio.Tests as source.
namespace RobotWin.Studio.Updates
{
    /// <summary>Semantic version (SemVer 2.0 precedence). Accepts "v2.1.0", "2.1", "2.1.0-beta.2", "2.1.0+build".</summary>
    public sealed class SemVersion : IComparable<SemVersion>, IEquatable<SemVersion>
    {
        private SemVersion(int major, int minor, int patch, string prerelease)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            Prerelease = prerelease;
        }

        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

        /// <summary>Pre-release label without the dash ("beta.2"), empty for a release.</summary>
        public string Prerelease { get; }

        public bool IsPrerelease => Prerelease.Length > 0;

        public static SemVersion? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string s = text.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            int plus = s.IndexOf('+');
            if (plus >= 0) s = s.Substring(0, plus);
            string pre = string.Empty;
            int dash = s.IndexOf('-');
            if (dash >= 0)
            {
                pre = s.Substring(dash + 1);
                s = s.Substring(0, dash);
                if (pre.Length == 0 || pre.Split('.').Any(p => p.Length == 0 || !p.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))) return null;
            }

            var parts = s.Split('.');
            if (parts.Length < 1 || parts.Length > 4) return null;
            var n = new int[3];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int v)) return null;
                if (i < 3) n[i] = v;   // a fourth (assembly revision) part is ignored
            }

            return new SemVersion(n[0], n[1], n[2], pre);
        }

        public static SemVersion FromVersion(Version v) => new SemVersion(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build), string.Empty);

        public int CompareTo(SemVersion? other)
        {
            if (other is null) return 1;
            int c = Major.CompareTo(other.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(other.Minor);
            if (c != 0) return c;
            c = Patch.CompareTo(other.Patch);
            if (c != 0) return c;
            if (!IsPrerelease && !other.IsPrerelease) return 0;
            if (!IsPrerelease) return 1;      // 2.1.0 > 2.1.0-rc.1
            if (!other.IsPrerelease) return -1;
            var a = Prerelease.Split('.');
            var b = other.Prerelease.Split('.');
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                bool an = int.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out int ai);
                bool bn = int.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out int bi);
                if (an && bn) c = ai.CompareTo(bi);
                else if (an) c = -1;          // numeric identifiers sort before alphanumeric ones
                else if (bn) c = 1;
                else c = string.CompareOrdinal(a[i], b[i]);
                if (c != 0) return Math.Sign(c);
            }

            return a.Length.CompareTo(b.Length);
        }

        public bool Equals(SemVersion? other) => other is not null && CompareTo(other) == 0;

        public override bool Equals(object? obj) => obj is SemVersion v && Equals(v);

        public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);

        public static bool operator >(SemVersion a, SemVersion b) => a.CompareTo(b) > 0;

        public static bool operator <(SemVersion a, SemVersion b) => a.CompareTo(b) < 0;

        public override string ToString() => $"{Major}.{Minor}.{Patch}" + (IsPrerelease ? "-" + Prerelease : string.Empty);
    }

    public sealed class ReleaseAsset
    {
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public string DownloadUrl { get; set; } = string.Empty;

        /// <summary>SHA-256 reported by GitHub for the uploaded file (hex, lower case), when available.</summary>
        public string? Sha256 { get; set; }

        /// <summary>
        /// The self-contained installer (carries .NET, no runtime needed). Releases from 1.1 on name it
        /// plainly and the small one "-lite"; older ones called the full one "-tam". Set by
        /// <see cref="ReleaseInfo.ClassifyInstallers"/> because the plain name means either, by release.
        /// </summary>
        public bool IsSelfContained { get; set; }

        /// <summary>The small installer that needs the .NET 9 Desktop Runtime (1.1 and later).</summary>
        public bool IsLite => Name.EndsWith("-lite.exe", StringComparison.OrdinalIgnoreCase);

        public bool IsInstaller => Name.StartsWith("RobotWinStudio-Setup-", StringComparison.OrdinalIgnoreCase) && Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class ReleaseInfo
    {
        /// <summary>
        /// Which installer carries .NET: with a "-lite" asset (1.1 and later) the plain one is the full
        /// package; without one (older releases) only "-tam" is.
        /// </summary>
        public void ClassifyInstallers()
        {
            bool newNaming = Assets.Any(a => a.IsInstaller && a.IsLite);
            foreach (var a in Assets)
                a.IsSelfContained = a.IsInstaller && (newNaming ? !a.IsLite : a.Name.EndsWith("-tam.exe", StringComparison.OrdinalIgnoreCase));
        }

        public string Tag { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public SemVersion? Version { get; set; }
        public bool IsPrerelease { get; set; }
        public bool IsDraft { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
        public string Body { get; set; } = string.Empty;
        public string HtmlUrl { get; set; } = string.Empty;
        public List<ReleaseAsset> Assets { get; set; } = new List<ReleaseAsset>();

        /// <summary>"2.1.0" (or the tag when it is not a version).</summary>
        public string DisplayVersion => Version?.ToString() ?? Tag;

        public string Title => string.IsNullOrWhiteSpace(Name) ? "RobotWin Studio " + DisplayVersion : Name;
    }

    /// <summary>Reads the GitHub Releases REST API response (a list, or a single release object).</summary>
    public static class ReleaseParser
    {
        public static List<ReleaseInfo> Parse(string json)
        {
            var list = new List<ReleaseInfo>();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    var r = ParseOne(e);
                    if (r != null) list.Add(r);
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var r = ParseOne(doc.RootElement);
                if (r != null) list.Add(r);
            }

            // Newest first: by version, then by date.
            return list.OrderByDescending(r => r.Version, Comparer<SemVersion?>.Create((a, b) => a is null ? (b is null ? 0 : -1) : a.CompareTo(b)))
                .ThenByDescending(r => r.PublishedAt ?? DateTimeOffset.MinValue).ToList();
        }

        private static ReleaseInfo? ParseOne(JsonElement e)
        {
            if (e.ValueKind != JsonValueKind.Object) return null;
            string tag = Str(e, "tag_name");
            if (tag.Length == 0) return null;
            var r = new ReleaseInfo
            {
                Tag = tag,
                Name = Str(e, "name"),
                Version = SemVersion.Parse(tag),
                IsPrerelease = Bool(e, "prerelease"),
                IsDraft = Bool(e, "draft"),
                Body = Str(e, "body").Replace("\r\n", "\n"),
                HtmlUrl = Str(e, "html_url")
            };
            string published = Str(e, "published_at");
            if (published.Length == 0) published = Str(e, "created_at");
            if (DateTimeOffset.TryParse(published, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when)) r.PublishedAt = when;
            if (e.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    if (a.ValueKind != JsonValueKind.Object) continue;
                    var asset = new ReleaseAsset
                    {
                        Name = Str(a, "name"),
                        DownloadUrl = Str(a, "browser_download_url"),
                        Size = a.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number && sz.TryGetInt64(out long size) ? size : 0
                    };
                    string digest = Str(a, "digest");
                    if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && ChecksumParser.IsSha256(digest.Substring(7))) asset.Sha256 = digest.Substring(7).ToLowerInvariant();
                    if (asset.Name.Length > 0 && asset.DownloadUrl.Length > 0) r.Assets.Add(asset);
                }
            }

            r.ClassifyInstallers();
            return r;
        }

        private static string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

        private static bool Bool(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    }

    /// <summary>
    /// Finds SHA-256 lines in release notes. Understood forms:
    ///   SHA-256 (RobotWinStudio-Setup-2.0.0.exe): `ca21…03`
    ///   SHA256: ca21…03                         (one file: applies to every asset)
    ///   ca21…03  RobotWinStudio-Setup-2.0.0.exe  (sha256sum output)
    ///   RobotWinStudio-Setup-2.0.0.exe: ca21…03
    /// </summary>
    public static class ChecksumParser
    {
        private static readonly Regex Labelled = new Regex(@"SHA-?256\s*(?:\(\s*(?<file>[^)]+?)\s*\))?\s*[:=]\s*`?(?<hash>[0-9a-fA-F]{64})`?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex SumStyle = new Regex(@"^\s*`?(?<hash>[0-9a-fA-F]{64})`?\s+\*?`?(?<file>[^\s`]+)`?\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex FileFirst = new Regex(@"^\s*[-*]?\s*\**`?(?<file>[\w.\-]+\.(?:exe|zip|msi|msix))`?\**\s*[:=\-–—]\s*`?(?<hash>[0-9a-fA-F]{64})`?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool IsSha256(string s) => s.Length == 64 && s.All(Uri.IsHexDigit);

        /// <summary>File name → hash (lower case). A hash without a file name is stored under the empty key.</summary>
        public static Dictionary<string, string> Parse(string? notes)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(notes)) return map;
            foreach (var raw in notes.Replace("\r\n", "\n").Split('\n'))
            {
                var m = Labelled.Match(raw);
                if (!m.Success) m = SumStyle.Match(raw);
                if (!m.Success) m = FileFirst.Match(raw);
                if (!m.Success) continue;
                string file = m.Groups["file"].Success ? m.Groups["file"].Value.Trim().Trim('`', '*') : string.Empty;
                map[file] = m.Groups["hash"].Value.ToLowerInvariant();
            }

            return map;
        }

        /// <summary>The expected hash for an asset: the notes line naming it, else a lone unnamed hash, else GitHub's digest.</summary>
        public static string? Expected(ReleaseInfo release, ReleaseAsset asset)
        {
            var map = Parse(release.Body);
            if (map.TryGetValue(asset.Name, out var h)) return h;
            if (map.TryGetValue(string.Empty, out h) && release.Assets.Count(a => a.IsInstaller) <= 1) return h;
            return asset.Sha256;
        }
    }

    /// <summary>What kind of copy is running; decides which installer and which arguments an update uses.</summary>
    public enum InstallMode
    {
        /// <summary>Installed for this user (HKCU uninstall entry points to the app folder).</summary>
        PerUser,

        /// <summary>Installed for all users (HKLM entry): the installer needs elevation.</summary>
        AllUsers,

        /// <summary>Unpacked with /PORTABLE (Uninstall.exe present, no uninstall entry).</summary>
        Portable,

        /// <summary>A build folder or hand-copied files: updates are downloaded, not installed in place.</summary>
        Development
    }

    public sealed class InstallInfo
    {
        public InstallMode Mode { get; set; }
        public string AppDirectory { get; set; } = string.Empty;

        /// <summary>coreclr.dll next to the exe: the self-contained ("-tam") build.</summary>
        public bool SelfContained { get; set; }

        /// <summary>.NET 9 Desktop Runtime installed on this PC.</summary>
        public bool RuntimeAvailable { get; set; }

        public bool DesktopShortcut { get; set; } = true;
        public bool FileTypesRegistered { get; set; } = true;

        public bool CanInstallInPlace => Mode != InstallMode.Development;

        public string ModeText => Mode switch
        {
            InstallMode.PerUser => "Bu kullanıcı için kurulu",
            InstallMode.AllUsers => "Tüm kullanıcılar için kurulu",
            InstallMode.Portable => "Taşınabilir kopya",
            _ => "Geliştirme derlemesi"
        };
    }

    public static class UpdateLogic
    {
        /// <summary>How the installer recorded itself: an uninstall entry (HKCU / HKLM) whose InstallLocation is the app folder, else Uninstall.exe alone = portable.</summary>
        public static InstallMode DetectMode(string appDir, string? perUserLocation, string? allUsersLocation, bool hasUninstaller)
        {
            if (SamePath(appDir, perUserLocation)) return InstallMode.PerUser;
            if (SamePath(appDir, allUsersLocation)) return InstallMode.AllUsers;
            return hasUninstaller ? InstallMode.Portable : InstallMode.Development;
        }

        public static bool SamePath(string a, string? b)
        {
            if (string.IsNullOrWhiteSpace(b)) return false;
            static string N(string p) => p.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');
            return string.Equals(N(a), N(b), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The installer for this copy: the same flavour as what is running ("-tam" when self-contained),
        /// falling back to the other one only if it can run here.
        /// </summary>
        public static ReleaseAsset? SelectAsset(ReleaseInfo release, bool selfContained, bool runtimeAvailable)
        {
            release.ClassifyInstallers();   // whichever naming scheme the release uses
            var installers = release.Assets.Where(a => a.IsInstaller).ToList();
            var full = installers.FirstOrDefault(a => a.IsSelfContained);
            var light = installers.FirstOrDefault(a => !a.IsSelfContained);
            if (selfContained) return full ?? (runtimeAvailable ? light : null);
            return light ?? full;
        }

        /// <summary>The newest release that is newer than <paramref name="current"/> (null when up to date).</summary>
        public static ReleaseInfo? FindUpdate(IEnumerable<ReleaseInfo> releases, SemVersion current, bool includePrereleases, string? skippedVersion)
        {
            var skipped = SemVersion.Parse(skippedVersion);
            ReleaseInfo? best = null;
            foreach (var r in releases)
            {
                if (r.IsDraft || r.Version == null) continue;
                if ((r.IsPrerelease || r.Version.IsPrerelease) && !includePrereleases) continue;
                if (!(r.Version > current)) continue;
                if (best == null || r.Version > best.Version!) best = r;
            }

            if (best != null && skipped != null && best.Version!.Equals(skipped)) return null;
            return best;
        }

        /// <summary>Command line for Installer/setup.cpp: silent install into the current folder, start the app afterwards.</summary>
        public static string InstallerArguments(InstallInfo info)
        {
            var args = new List<string> { "/S" };
            if (info.Mode == InstallMode.Portable) args.Add("/PORTABLE");
            if (info.Mode == InstallMode.AllUsers) args.Add("/ALLUSERS");
            if (info.Mode != InstallMode.Portable)
            {
                if (!info.DesktopShortcut) args.Add("/NODESKTOP");
                if (!info.FileTypesRegistered) args.Add("/NOTYPES");
            }

            args.Add("/LAUNCH");
            // setup.cpp reads everything after "/D=" verbatim; quoting keeps paths with spaces intact.
            args.Add("/D=" + info.AppDirectory.TrimEnd('\\'));
            return string.Join(" ", args.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a));
        }

        public static string FormatSize(long bytes)
        {
            var c = RobotWin.Studio.Shell.Lang.Culture;
            if (bytes >= 1024L * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.0", c) + " MB";
            if (bytes >= 1024) return (bytes / 1024.0).ToString("0", c) + " KB";
            return bytes.ToString(c) + " B";
        }
    }

    // ===================================================================== news

    /// <summary>An item of the Haberler feed: an announcement from news.json or a release's notes.</summary>
    public sealed class NewsItem
    {
        public string Id { get; set; } = string.Empty;
        public DateTimeOffset Date { get; set; }
        public string Title { get; set; } = string.Empty;

        /// <summary>Markdown.</summary>
        public string Body { get; set; } = string.Empty;

        public string? Link { get; set; }
        public string? Image { get; set; }

        /// <summary>Set for release notes.</summary>
        public ReleaseInfo? Release { get; set; }

        public bool IsRelease => Release != null;
    }

    /// <summary>
    /// docs/news.json:
    /// { "version": 1, "items": [ { "id": "hosgeldin", "date": "2026-11-20", "title": "…", "body": "Markdown…",
    ///   "link": "https://…" (optional), "image": "https://… or images/x.png" (optional),
    ///   "en": { "title": "…", "body": "…", "link": "…" } (optional, one per language) } ] }
    /// A bare array of items is accepted too. Items without id or title are skipped. [lang] An item is read in the
    /// language in use when it has it, else in English, else as written (Turkish).
    /// </summary>
    public static class NewsParser
    {
        /// <summary>The item's texts in the language in use (its own, else English); null for Turkish or none.</summary>
        private static JsonElement? Local(JsonElement e)
        {
            string lang = RobotWin.Studio.Shell.Lang.Current;
            if (lang == "tr") return null;
            foreach (var id in new[] { lang, "en" })
            {
                if (e.TryGetProperty(id, out var l) && l.ValueKind == JsonValueKind.Object) return l;
            }

            return null;
        }

        private static string Pick(JsonElement? local, JsonElement e, string name) =>
            local is { } l && Str(l, name) is { Length: > 0 } v ? v : Str(e, name);

        public static List<NewsItem> Parse(string json)
        {
            var list = new List<NewsItem>();
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            JsonElement items = doc.RootElement;
            if (items.ValueKind == JsonValueKind.Object && !items.TryGetProperty("items", out items)) return list;
            if (items.ValueKind != JsonValueKind.Array) return list;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in items.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var local = Local(e);
                string id = Str(e, "id"), title = Pick(local, e, "title");
                if (id.Length == 0 || title.Length == 0 || !seen.Add(id)) continue;
                if (!DateTimeOffset.TryParse(Str(e, "date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)) date = DateTimeOffset.MinValue;
                list.Add(new NewsItem
                {
                    Id = id,
                    Date = date,
                    Title = title,
                    Body = Pick(local, e, "body").Replace("\r\n", "\n"),
                    Link = Url(Pick(local, e, "link")),
                    Image = NullIfEmpty(Str(e, "image"))
                });
            }

            return list.OrderByDescending(n => n.Date).ToList();
        }

        /// <summary>Announcements plus one item per published release (newest first).</summary>
        public static List<NewsItem> Merge(IEnumerable<NewsItem> announcements, IEnumerable<ReleaseInfo> releases)
        {
            var all = announcements.ToList();
            foreach (var r in releases.Where(r => !r.IsDraft))
            {
                all.Add(new NewsItem
                {
                    Id = ReleaseNewsId(r),
                    Date = r.PublishedAt ?? DateTimeOffset.MinValue,
                    Title = r.Title,
                    Body = r.Body,
                    Link = NullIfEmpty(r.HtmlUrl),
                    Release = r
                });
            }

            return all.OrderByDescending(n => n.Date).ThenBy(n => n.IsRelease ? 1 : 0).ToList();
        }

        public static string ReleaseNewsId(ReleaseInfo r) => "release:" + r.Tag;

        private static string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? string.Empty).Trim() : string.Empty;

        private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

        // Only web links are opened from the feed.
        private static string? Url(string s) =>
            Uri.TryCreate(s, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) ? s : null;
    }

    /// <summary>[news] What a version brought, read from the guide's "Yenilikler" page.</summary>
    public static class WhatsNew
    {
        /// <summary>
        /// This version's section of the bundled guide page: its bold summary line and the bold lead-ins of its first
        /// bullets ("**Kablo yolu:** …" → "Kablo yolu").
        /// </summary>
        public static (string? Summary, List<string> Headlines) Headlines(string version, string file, int max = 7)
        {
            var list = new List<string>();
            if (!File.Exists(file)) return (null, list);
            string? summary = null;
            bool inside = false;
            foreach (string raw in File.ReadLines(file))
            {
                string line = raw.Trim();
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    if (inside) break;
                    inside = line.Substring(3).StartsWith(version + " ", StringComparison.Ordinal) || line.Substring(3) == version;
                    continue;
                }

                if (!inside) continue;
                if (summary == null && line.StartsWith("**", StringComparison.Ordinal) && line.EndsWith("**", StringComparison.Ordinal) && line.Length > 4)
                {
                    summary = line.Trim('*');
                    continue;
                }

                if (line.StartsWith("### ", StringComparison.Ordinal) && list.Count > 0) break;   // the first list only (what is new)
                if (!line.StartsWith("- **", StringComparison.Ordinal)) continue;
                int end = line.IndexOf("**", 4, StringComparison.Ordinal);
                if (end < 0) continue;
                list.Add(line.Substring(4, end - 4).TrimEnd(':', ' '));
                if (list.Count >= max) break;
            }

            return (summary, list);
        }
    }
}
