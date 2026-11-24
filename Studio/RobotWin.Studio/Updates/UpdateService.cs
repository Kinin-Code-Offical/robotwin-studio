using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RobotWin.Studio.Updates
{
    /// <summary>An update problem, with a message written for the user (Turkish).</summary>
    public sealed class UpdateException : Exception
    {
        public UpdateException(string message, Exception? inner = null) : base(message, inner)
        {
        }
    }

    public readonly struct DownloadProgress
    {
        public DownloadProgress(long received, long total, double bytesPerSecond)
        {
            Received = received;
            Total = total;
            BytesPerSecond = bytesPerSecond;
        }

        public long Received { get; }
        public long Total { get; }
        public double BytesPerSecond { get; }
        public double Fraction => Total > 0 ? Math.Min(1, (double)Received / Total) : 0;
    }

    public sealed class DownloadResult
    {
        public string Path { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;

        /// <summary>The hash was known (release notes or GitHub) and matched.</summary>
        public bool HashVerified { get; set; }
    }

    public sealed class Fetched<T>
    {
        public Fetched(T value, bool fromCache, string? warning)
        {
            Value = value;
            FromCache = fromCache;
            Warning = warning;
        }

        public T Value { get; }
        public bool FromCache { get; }

        /// <summary>Why the cached copy was used (offline, rate limit …).</summary>
        public string? Warning { get; }
    }

    /// <summary>
    /// GitHub access for updates and news: anonymous HTTPS GETs with a User-Agent, short timeouts,
    /// ETag caching (a 304 does not count against GitHub's 60 requests/hour limit) and an on-disk copy
    /// so everything still shows offline.
    /// </summary>
    public static class UpdateService
    {
        public const string Owner = "Kinin-Code-Offical";
        public const string Repo = "robotwin-studio";
        public const string RepoUrl = "https://github.com/" + Owner + "/" + Repo;
        public const string ReleasesPage = RepoUrl + "/releases";
        public const string ReleasesApi = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases?per_page=100";
        public const string NewsUrl = "https://raw.githubusercontent.com/" + Owner + "/" + Repo + "/main/docs/news.json";
        public const string UserAgent = "RobotWinStudio-Updater";

        private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

        private static readonly HttpClient Http = CreateClient();

        /// <summary>%APPDATA%\RobotWinStudio\updates: cached API answers and the read/unread state.</summary>
        public static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RobotWinStudio", "updates");

        /// <summary>%LOCALAPPDATA%\RobotWinStudio\Updates: downloaded installers (cleaned up on the next start).</summary>
        public static string DownloadFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RobotWinStudio", "Updates");

        private static HttpClient CreateClient()
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            };
            var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "2"));
            return http;
        }

        // ------------------------------------------------------------------ releases & news

        public static async Task<Fetched<List<ReleaseInfo>>> GetReleasesAsync(CancellationToken ct)
        {
            var (json, cached, warning) = await GetCachedAsync(ReleasesApi, "releases", "application/vnd.github+json", ct).ConfigureAwait(false);
            try
            {
                return new Fetched<List<ReleaseInfo>>(ReleaseParser.Parse(json), cached, warning);
            }
            catch (JsonException ex)
            {
                throw new UpdateException("GitHub'dan gelen sürüm listesi okunamadı.", ex);
            }
        }

        public static List<ReleaseInfo>? CachedReleases()
        {
            string? json = ReadCache("releases");
            if (json == null) return null;
            try
            {
                return ReleaseParser.Parse(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Announcements from docs/news.json on the default branch; the copy bundled with the app when offline.</summary>
        public static async Task<Fetched<List<NewsItem>>> GetNewsAsync(CancellationToken ct)
        {
            try
            {
                var (json, cached, warning) = await GetCachedAsync(NewsUrl, "news", "application/json", ct).ConfigureAwait(false);
                return new Fetched<List<NewsItem>>(NewsParser.Parse(json), cached, warning);
            }
            catch (Exception ex) when (ex is UpdateException || ex is JsonException)
            {
                var bundled = BundledNews();
                if (bundled != null) return new Fetched<List<NewsItem>>(bundled, true, ex is UpdateException ? ex.Message : "Haber dosyası okunamadı.");
                throw ex as UpdateException ?? new UpdateException("Haber dosyası okunamadı.", ex);
            }
        }

        public static List<NewsItem>? CachedNews()
        {
            string? json = ReadCache("news");
            if (json != null)
            {
                try
                {
                    return NewsParser.Parse(json);
                }
                catch (JsonException)
                {
                }
            }

            return BundledNews();
        }

        private static List<NewsItem>? BundledNews()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Docs", "news.json");
            try
            {
                return File.Exists(path) ? NewsParser.Parse(File.ReadAllText(path)) : null;
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>GET with ETag; on any failure the last good answer from disk (with a warning), else an <see cref="UpdateException"/>.</summary>
        private static async Task<(string Json, bool Cached, string? Warning)> GetCachedAsync(string url, string name, string accept, CancellationToken ct)
        {
            string? cached = ReadCache(name);
            string? etag = cached != null ? ReadFile(name + ".etag") : null;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(ApiTimeout);
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Accept.ParseAdd(accept);
                if (url.StartsWith("https://api.github.com/", StringComparison.Ordinal)) req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                if (etag != null) req.Headers.TryAddWithoutValidation("If-None-Match", etag);
                using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (resp.StatusCode == HttpStatusCode.NotModified && cached != null) return (cached, false, null);
                if (!resp.IsSuccessStatusCode) throw new UpdateException(DescribeStatus(resp));
                string json = await resp.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                WriteFile(name + ".json", json);
                if (resp.Headers.ETag != null) WriteFile(name + ".etag", resp.Headers.ETag.ToString());
                return (json, false, null);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException || ex is UpdateException || ex is IOException)
            {
                if (ex is OperationCanceledException && ct.IsCancellationRequested) throw;
                string message = Describe(ex);
                if (cached != null) return (cached, true, message);
                throw ex as UpdateException ?? new UpdateException(message, ex);
            }
        }

        private static string DescribeStatus(HttpResponseMessage resp)
        {
            int code = (int)resp.StatusCode;
            bool limited = code == 429 || (code == 403 && resp.Headers.TryGetValues("X-RateLimit-Remaining", out var rem) && string.Join("", rem) == "0");
            if (limited)
            {
                string when = string.Empty;
                if (resp.Headers.TryGetValues("X-RateLimit-Reset", out var reset) && long.TryParse(string.Join("", reset), out long unix))
                    when = " (" + DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("HH:mm") + " sonra)";
                return "GitHub'ın saatlik istek sınırı doldu; biraz sonra yeniden deneyin" + when + ".";
            }

            return code switch
            {
                404 => "GitHub'da sürüm bilgisi bulunamadı (404).",
                >= 500 => $"GitHub şu anda yanıt veremiyor ({code}); daha sonra yeniden deneyin.",
                _ => $"GitHub isteği reddetti ({code} {resp.ReasonPhrase})."
            };
        }

        /// <summary>A Turkish explanation of a network or file error.</summary>
        public static string Describe(Exception ex) => ex switch
        {
            UpdateException u => u.Message,
            OperationCanceledException => "GitHub zamanında yanıt vermedi (zaman aşımı). İnternet bağlantınızı denetleyip yeniden deneyin.",
            HttpRequestException { InnerException: SocketException } => "İnternete bağlanılamadı ya da GitHub'a ulaşılamıyor. Bağlantınızı denetleyin.",
            HttpRequestException { InnerException: System.Security.Authentication.AuthenticationException } => "GitHub ile güvenli bağlantı kurulamadı (sertifika sorunu). Bilgisayarın saatini ve ağ ayarlarını denetleyin.",
            HttpRequestException h => "Ağ hatası: " + h.Message,
            UnauthorizedAccessException => "Dosya yazılamadı: erişim reddedildi.",
            IOException io => "Dosya yazılamadı: " + io.Message,
            _ => ex.Message
        };

        // ------------------------------------------------------------------ download

        /// <summary>
        /// Downloads an installer into <paramref name="folder"/> (default: the update cache), checking the size and the
        /// SHA-256 from the release notes / GitHub. A matching file that is already there is reused.
        /// </summary>
        public static async Task<DownloadResult> DownloadAsync(ReleaseInfo release, ReleaseAsset asset, string? folder, IProgress<DownloadProgress>? progress, CancellationToken ct)
        {
            folder ??= DownloadFolder;
            string? expected = ChecksumParser.Expected(release, asset);
            string target = Path.Combine(folder, asset.Name);
            string part = target + ".part";
            try
            {
                Directory.CreateDirectory(folder);
                if (File.Exists(target) && (asset.Size <= 0 || new FileInfo(target).Length == asset.Size))
                {
                    string have = await HashFileAsync(target, ct).ConfigureAwait(false);
                    if (expected == null || have == expected)
                    {
                        progress?.Report(new DownloadProgress(asset.Size, asset.Size, 0));
                        return new DownloadResult { Path = target, Sha256 = have, HashVerified = expected != null };
                    }
                }

                using var req = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
                req.Headers.Accept.ParseAdd("application/octet-stream");
                using var connect = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connect.CancelAfter(ApiTimeout);
                using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, connect.Token).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) throw new UpdateException(resp.StatusCode == HttpStatusCode.NotFound ? "Kurulum dosyası GitHub'da bulunamadı (404)." : DescribeStatus(resp));
                long total = resp.Content.Headers.ContentLength ?? asset.Size;
                if (asset.Size > 0 && total > 0 && total != asset.Size) throw new UpdateException($"Sunucunun bildirdiği boyut ({total} bayt) sürümdeki boyutla ({asset.Size} bayt) aynı değil.");

                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long received = 0;
                var started = DateTime.UtcNow;
                var lastReport = DateTime.MinValue;
                await using (var input = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                {
                    var buffer = new byte[1 << 16];
                    while (true)
                    {
                        // A connection that stops delivering data for 30 s counts as broken.
                        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        stall.CancelAfter(StallTimeout);
                        int n = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), stall.Token).ConfigureAwait(false);
                        if (n == 0) break;
                        await output.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                        sha.AppendData(buffer, 0, n);
                        received += n;
                        var now = DateTime.UtcNow;
                        if ((now - lastReport).TotalMilliseconds > 120)
                        {
                            lastReport = now;
                            double secs = Math.Max(0.001, (now - started).TotalSeconds);
                            progress?.Report(new DownloadProgress(received, total, received / secs));
                        }
                    }
                }

                progress?.Report(new DownloadProgress(received, total, 0));
                if (asset.Size > 0 && received != asset.Size)
                    throw new UpdateException($"İndirme eksik kaldı ({UpdateLogic.FormatSize(received)} / {UpdateLogic.FormatSize(asset.Size)}). Yeniden deneyin.");
                string hash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                if (expected != null && hash != expected)
                    throw new UpdateException("İndirilen dosya doğrulanamadı: SHA-256 değeri sürüm notlarındakiyle eşleşmiyor. Dosya silindi; yeniden deneyin.");
                File.Move(part, target, true);
                return new DownloadResult { Path = target, Sha256 = hash, HashVerified = expected != null };
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException || ex is IOException || ex is UnauthorizedAccessException || ex is UpdateException)
            {
                TryDelete(part);
                if (ex is OperationCanceledException && ct.IsCancellationRequested) throw;
                if (ex is OperationCanceledException) throw new UpdateException("Bağlantı koptu ya da çok yavaşladı (30 saniye veri gelmedi). Yeniden deneyin.", ex);
                throw ex as UpdateException ?? new UpdateException(Describe(ex), ex);
            }
        }

        public static async Task<string> HashFileAsync(string path, CancellationToken ct)
        {
            await using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
            using var sha = SHA256.Create();
            return Convert.ToHexString(await sha.ComputeHashAsync(f, ct).ConfigureAwait(false)).ToLowerInvariant();
        }

        /// <summary>Removes installers left over from earlier updates (a running one is skipped).</summary>
        public static void CleanDownloads()
        {
            try
            {
                var dir = new DirectoryInfo(DownloadFolder);
                if (!dir.Exists) return;
                foreach (var f in dir.GetFiles())
                {
                    if (DateTime.UtcNow - f.LastWriteTimeUtc > TimeSpan.FromMinutes(10)) TryDelete(f.FullName);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        // ------------------------------------------------------------------ files

        private static string? ReadCache(string name) => ReadFile(name + ".json");

        private static string? ReadFile(string name)
        {
            if (Shell.Unattended.Active) return null;   // snapshots and checks show what ships with the build, not this machine's cache
            try
            {
                string path = Path.Combine(DataFolder, name);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static void WriteFile(string name, string content)
        {
            if (Shell.Unattended.Active) return;
            try
            {
                Directory.CreateDirectory(DataFolder);
                File.WriteAllText(Path.Combine(DataFolder, name), content);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}
