using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Updates
{
    /// <summary>Persisted between runs: when we last asked GitHub and which news items were read.</summary>
    public sealed class UpdateState
    {
        public DateTime? LastCheckUtc { get; set; }
        public List<string> ReadNews { get; set; } = new List<string>();

        /// <summary>False until the first run marked the older release notes as read.</summary>
        public bool NewsInitialized { get; set; }

        private static string StorePath => Path.Combine(UpdateService.DataFolder, "state.json");

        public static UpdateState Load()
        {
            try
            {
                if (File.Exists(StorePath)) return JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(StorePath)) ?? new UpdateState();
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
            }

            return new UpdateState();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                File.WriteAllText(StorePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// The app's view of releases, news and the available update. Created on the UI thread; <see cref="Changed"/>
    /// is always raised there.
    /// </summary>
    public sealed class UpdateManager
    {
        private static UpdateManager? _current;
        private readonly SynchronizationContext? _ui;
        private readonly UpdateState _state;
        private InstallInfo? _install;
        private List<NewsItem> _announcements = new List<NewsItem>();
        private Task? _running;

        private UpdateManager()
        {
            _ui = SynchronizationContext.Current;
            _state = UpdateState.Load();
            CurrentVersion = InstallEnvironment.CurrentVersion;
            Releases = UpdateService.CachedReleases() ?? new List<ReleaseInfo>();
            _announcements = UpdateService.CachedNews() ?? new List<NewsItem>();
            Recompute();
        }

        public static UpdateManager Current => _current ??= new UpdateManager();

        public event EventHandler? Changed;

        public SemVersion CurrentVersion { get; }

        public InstallInfo Install => _install ??= InstallEnvironment.Detect();

        /// <summary>Every release (newest first), from GitHub or the cached copy.</summary>
        public IReadOnlyList<ReleaseInfo> Releases { get; private set; }

        /// <summary>Announcements and release notes, newest first.</summary>
        public IReadOnlyList<NewsItem> News { get; private set; } = Array.Empty<NewsItem>();

        /// <summary>The newer release to offer (respects pre-release and "skip this version" settings).</summary>
        public ReleaseInfo? Available { get; private set; }

        public bool IsChecking { get; private set; }

        /// <summary>Last problem talking to GitHub (Turkish), null after a clean check.</summary>
        public string? LastError { get; private set; }

        public DateTime? LastCheckUtc => _state.LastCheckUtc;

        /// <summary>"Daha sonra": hides the notice until the next start.</summary>
        public bool NoticeDismissed { get; private set; }

        public bool ShowNotice => Available != null && !NoticeDismissed;

        public int UnreadCount => News.Count(IsUnread);

        public bool IsUnread(NewsItem n) => !_state.ReadNews.Contains(n.Id);

        public void MarkRead(IEnumerable<NewsItem> items)
        {
            bool changed = false;
            foreach (var n in items)
            {
                if (_state.ReadNews.Contains(n.Id)) continue;
                _state.ReadNews.Add(n.Id);
                changed = true;
            }

            if (!changed) return;
            _state.Save();
            Raise();
        }

        public void Dismiss()
        {
            NoticeDismissed = true;
            Raise();
        }

        /// <summary>"Bu sürümü atla": not offered again until a newer one appears.</summary>
        public void Skip(ReleaseInfo release)
        {
            AppSettings.Current.SkippedUpdateVersion = release.DisplayVersion;
            AppSettings.Current.Save();
            Recompute();
            Raise();
        }

        public void ClearSkip()
        {
            AppSettings.Current.SkippedUpdateVersion = string.Empty;
            AppSettings.Current.Save();
            Recompute();
            Raise();
        }

        /// <summary>Re-applies the settings (pre-releases on/off) to the known releases.</summary>
        public void Refresh()
        {
            Recompute();
            Raise();
        }

        /// <summary>At start-up: if enabled and the last check is a day old, asks GitHub in the background.</summary>
        public void StartupCheck()
        {
            UpdateService.CleanDownloads();
            if (!AppSettings.Current.AutoCheckUpdates) return;
            if (_state.LastCheckUtc is { } last && DateTime.UtcNow - last < TimeSpan.FromHours(23)) return;
            _ = CheckAsync();
        }

        /// <summary>Fetches releases and news (one request each, ETag-cached). Concurrent calls share one run.</summary>
        public Task CheckAsync()
        {
            if (IsChecking && _running != null) return _running;
            IsChecking = true;
            _running = RunCheckAsync();
            Raise();
            return _running;
        }

        private async Task RunCheckAsync()
        {
            string? error = null;
            List<ReleaseInfo>? releases = null;
            List<NewsItem>? announcements = null;
            bool fresh = false;
            try
            {
                var r = await UpdateService.GetReleasesAsync(CancellationToken.None).ConfigureAwait(false);
                releases = r.Value;
                error = r.Warning;
                fresh = !r.FromCache;
            }
            catch (UpdateException ex)
            {
                error = ex.Message;
            }

            try
            {
                announcements = (await UpdateService.GetNewsAsync(CancellationToken.None).ConfigureAwait(false)).Value;
            }
            catch (UpdateException)
            {
                // News are optional; the release error (if any) says enough.
            }

            // Results are applied on the UI thread.
            Post(() =>
            {
                if (releases != null) Releases = releases;
                if (announcements != null) _announcements = announcements;
                if (fresh) _state.LastCheckUtc = DateTime.UtcNow;
                LastError = error;
                IsChecking = false;
                Recompute();
                _state.Save();
                Raise();
            });
        }

        private void Recompute()
        {
            var s = AppSettings.Current;
            Available = UpdateLogic.FindUpdate(Releases, CurrentVersion, s.IncludePrereleases, s.SkippedUpdateVersion);
            News = NewsParser.Merge(_announcements, Releases.Where(r => s.IncludePrereleases || !r.IsPrerelease));
            if (!_state.NewsInitialized && Releases.Count > 0)
            {
                // First run: notes of versions older than this one are history, not news.
                foreach (var r in Releases.Where(r => r.Version != null && r.Version < CurrentVersion))
                {
                    string id = NewsParser.ReleaseNewsId(r);
                    if (!_state.ReadNews.Contains(id)) _state.ReadNews.Add(id);
                }

                _state.NewsInitialized = true;
                _state.Save();
            }
        }

        private void Post(Action a)
        {
            if (_ui != null) _ui.Post(_ => a(), null);
            else a();
        }

        private void Raise() => Post(() => Changed?.Invoke(this, EventArgs.Empty));
    }
}
