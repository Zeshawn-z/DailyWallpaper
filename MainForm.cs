using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace BingWallpapers;

internal sealed partial class MainForm : AntdUI.Window
{
    private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
    private readonly Timer wallpaperTimer = new Timer(), clock = new Timer { Interval = 1000 };
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip trayMenu;
    private Settings settings;
    private WallpaperLibrary library;
    private BingClient bing;
    private List<Wallpaper> recent = new List<Wallpaper>();
    private Wallpaper selected;
    private SavedWallpaper savedEntry;
    private CancellationTokenSource heroRequest, recentRequest, galleryRequest, inspectorRequest, desktopRequest;
    private DateTimeOffset refreshAfter, nextAttempt;
    private bool ready, loadingControls, refreshing, changing, quitting, editingDetails;
    private int libraryFilter, order;

    public MainForm() : this(false) { }

    public MainForm(bool startHidden)
    {
        BuildUi();
        string startupIssue = null;
        try
        {
            settings = Settings.Load();
            Validate(settings);
        }
        catch (Exception error) when (Operational(error))
        {
            startupIssue = error.Message;
            settings = new Settings();
        }
        try { library = new WallpaperLibrary(settings.LibraryDirectory); }
        catch (Exception error) when (Operational(error)) { startupIssue = error.Message; }
        bing = new BingClient(settings);
        try { recent = GetCached(); }
        catch (Exception error) when (Operational(error)) { startupIssue = error.Message; }

        trayMenu = MakeMenu();
        trayMenu.Items.Add("Open Dayframe", null, (_, __) => ShowFromTray());
        trayMenu.Items.Add("Next wallpaper", null, async (_, __) => await ChangeNowAsync());
        trayMenu.Items.Add("Pause / resume auto-change", null, (_, __) => TogglePaused());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, __) => { quitting = true; Close(); });
        tray = new NotifyIcon { Text = "Dayframe", Icon = Icon, Visible = true, ContextMenuStrip = trayMenu };
        tray.DoubleClick += (_, __) => ShowFromTray();
        wallpaperTimer.Tick += async (_, __) => await OnWallpaperTimerAsync();
        clock.Tick += (_, __) => PaintRotationStatus();
        VisibleChanged += (_, __) =>
        {
            if (Visible && ready) clock.Start();
            else clock.Stop();
        };
        ready = true;
        LoadOptions();
        RenderLibrary();
        ResetSchedule();
        Status(startupIssue ?? "");
        Shown += async (_, __) =>
        {
            if (startHidden)
                Hide();
            else
                ShowRecent();
            await RefreshAsync();
        };
    }

    private void ShowPage(string view)
    {
        page = view;
        todayPage.Visible = view == "today";
        libraryPage.Visible = view == "library";
        rotationPage.Visible = view == "rotation";
        settingsPage.Visible = view == "settings";
        string[] names = { "today", "library", "rotation", "settings" };
        for (int i = 0; i < navigation.Length; i++)
        {
            navigation[i].Selected = names[i] == view;
            navigation[i].Font = Palette.Font(14, names[i] == view);
            navigation[i].Invalidate();
        }
        if (!ready)
            return;
        if (view == "library")
        {
            Attempt(() => library?.Reload());
            RenderLibrary(savedEntry?.Wallpaper.Id);
            QueueGalleryPreviews();
        }
        else if (view == "today" && (recentCards.Controls.Count == 0 || selected == null))
            ShowRecent();
        else if (view == "rotation")
        {
            ConfigureRotationUi();
            ShowRecentChanges();
            PaintRotationStatus();
        }
    }

    public void ShowFromTray()
    {
        if (quitting) return;
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
        ShowPage(page);
        _ = RefreshAsync();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (!quitting && e.CloseReason == CloseReason.UserClosing && settings?.KeepInTray == true)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private List<Wallpaper> GetCached() =>
        bing.CachedFeed()?.Where(item => item != null && !string.IsNullOrEmpty(item.Id) &&
            DateTime.TryParseExact(item.Date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)).ToList()
        ?? new List<Wallpaper>();

    private async Task RefreshAsync(bool force = false)
    {
        if (quitting || refreshing || (!force && DateTimeOffset.Now < refreshAfter))
            return;
        refreshing = true;
        try
        {
            var fetched = await bing.FetchFeedAsync(lifetime.Token);
            if (quitting) return;
            bool changed = !recent.Select(item => item.Id).SequenceEqual(fetched.Select(item => item.Id));
            recent = fetched;
            refreshAfter = DateTimeOffset.Now.AddHours(1);
            if (page == "today" && Visible && (changed || selected == null || force))
                ShowRecent();
            else if (changed)
            {
                ClearRecentCards();
                selected = null;
            }
            PickQueue();
            if (force)
                Status("Bing wallpapers updated");
        }
        catch (Exception error) when (Operational(error))
        {
            if (quitting) return;
            refreshAfter = DateTimeOffset.Now.AddMinutes(10);
            if (recent.Count == 0)
                heroTitle.Text = "Could not load Bing wallpapers";
            Status(recent.Count == 0 ? ErrorText(error) : "Showing cached Bing wallpapers", true);
        }
        finally { refreshing = false; UpdateButtons(); }
    }

    private void ClearRecentCards()
    {
        Cancel(ref recentRequest);
        while (recentCards.Controls.Count != 0)
            recentCards.Controls[0].Dispose();
    }

    private void ShowRecent()
    {
        ClearRecentCards();
        recentRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        string previousId = selected?.Id;
        SelectToday(recent.Find(item => item.Id == previousId) ?? recent.FirstOrDefault());
        recentCards.SuspendLayout();
        recentCards.ColumnStyles.Clear();
        recentCards.ColumnCount = Math.Max(1, recent.Count);
        foreach (var item in recent)
        {
            bool newest = item == recent[0];
            var tile = new PhotoTile(item, null, newest ? "Today" : item.Day.ToString("ddd d", English), true)
            {
                Dock = DockStyle.Fill, Margin = new Padding(0, 0, S(4), 0)
            };
            recentCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / recent.Count));
            recentCards.Controls.Add(tile);
            tile.Click += (_, __) => SelectToday(item);
            tip.SetToolTip(tile, item.Title);
            _ = LoadTileAsync(tile, recentRequest.Token);
        }
        recentCards.ResumeLayout();
        RefreshTodayLabels();
    }

    private void SelectToday(Wallpaper item)
    {
        selected = item;
        Cancel(ref heroRequest);
        hero.SetImage(null);
        RefreshTodayLabels();
        if (item == null)
            return;
        heroRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        _ = LoadPhotoAsync(hero, item, false, heroRequest.Token);
    }

    private void RefreshTodayLabels()
    {
        var item = selected;
        var entry = library?.Find(item?.Id);
        bool current = item != null && settings?.CurrentId == item.Id;
        bool saved = entry != null && library.Exists(entry);
        bool latest = item != null && recent.FirstOrDefault()?.Id == item.Id;
        todayDate.Text = item == null ? "Bing" : (latest ? "Today" : item.Day.ToString("ddd", English)) +
            " · " + item.Day.ToString("ddd, d MMM", English);
        todayState.Text = current ? "On your desktop" : saved ? "Saved" : "Not set yet";
        todayState.Glyph = current ? "desktop" : saved ? "check" : null;
        todayState.GlyphSize = 14;
        todayState.Width = S(current ? 148 : 112);
        todayState.GlyphColor = current ? Palette.Amber : Palette.Blue;
        todayState.Invalidate();
        resolution.Text = entry != null ? $"{entry.Width} × {entry.Height}" : item?.Downloadable == true ? "UHD" : "";
        resolution.Visible = item != null;
        heroTitle.Text = item?.Title ?? "Getting today’s wallpaper…";
        heroCredit.Text = Attribution(item);
        tip.SetToolTip(heroCredit, item?.Copyright ?? "");
        about.Visible = !string.IsNullOrEmpty(item?.CopyrightLink);
        fit.Text = settings?.Fit ?? "Fill";
        hero.Contain = fit.Text == "Fit" || fit.Text == "Center";
        hero.Invalidate();
        favorite.GlyphColor = entry?.Favorite == true ? Palette.Amber : Palette.Text;
        favorite.AccessibleName = entry?.Favorite == true ? "Remove from favorites" : "Add to favorites";
        favorite.Invalidate();
        apply.Text = changing ? "Working…" : IsCurrentFit(item) ? "Current wallpaper" : "Set as wallpaper";
        fileHint.Text = item == null ? "" : saved ? $"{entry.FileName} · {FormatBytes(library.Bytes(entry))}" :
            item.Downloadable ? "Saves as " + (library?.ProposedName(item) ?? item.Title + "_" + item.Date + ".jpg") : "Download unavailable";
        marketName.Text = settings?.Market == "zh-CN" ? "Bing · China" : "Bing · United States";
        PlaceToday(Logic(todayPage.Width), Logic(todayPage.Height));
        foreach (PhotoTile tile in recentCards.Controls)
        {
            tile.Selected = item?.Id == tile.Wallpaper.Id;
            var stored = library?.Find(tile.Wallpaper.Id);
            tile.Saved = stored != null && library.Exists(stored);
            tile.OnDesktop = settings?.CurrentId == tile.Wallpaper.Id;
            tile.Invalidate();
        }
        UpdateButtons();
    }

    private string LocalPath(Wallpaper item)
    {
        var stored = library?.Find(item.Id);
        return stored != null && library.Exists(stored) ? library.PathOf(stored) : null;
    }

    private async Task LoadPhotoAsync(PhotoView view, Wallpaper item, bool thumbnail, CancellationToken token)
    {
        try
        {
            string file = LocalPath(item);
            if (file == null && item.BingPath == null)
                return;
            var image = await bing.PreviewAsync(item, file, thumbnail, token);
            if (token.IsCancellationRequested || view.IsDisposed) image.Dispose();
            else view.SetImage(image);
        }
        catch (Exception error) when (Operational(error))
        {
            if (!token.IsCancellationRequested && !view.IsDisposed)
                tip.SetToolTip(view, ErrorText(error));
        }
    }

    private async Task LoadTileAsync(PhotoTile tile, CancellationToken token)
    {
        try
        {
            var photo = await bing.PreviewAsync(tile.Wallpaper, LocalPath(tile.Wallpaper), true, token);
            if (token.IsCancellationRequested || tile.IsDisposed) photo.Dispose();
            else tile.SetImage(photo);
        }
        catch (Exception error) when (Operational(error))
        {
            if (!token.IsCancellationRequested && !tile.IsDisposed)
                tip.SetToolTip(tile, tile.Wallpaper.Title + " · " + ErrorText(error));
        }
    }

    private bool CanApply(Wallpaper item) => item != null && library != null &&
        (item.Downloadable || LocalPath(item) != null);

    private bool IsCurrentFit(Wallpaper item) => item != null && settings.CurrentId == item.Id && settings.CurrentFit == settings.Fit;

    private async Task ApplyAsync(Wallpaper item)
    {
        if (quitting || changing || !CanApply(item))
            return;
        changing = true;
        RefreshTodayLabels();
        try
        {
            var entry = await library.SaveAsync(item, bing, lifetime.Token);
            await Task.Run(() => DesktopWallpaper.Apply(library.PathOf(entry), settings.Fit), lifetime.Token);
            string at = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);
            settings.CurrentId = item.Id;
            settings.CurrentFit = settings.Fit;
            settings.History.Insert(0, new ChangeRecord { Id = item.Id, Title = entry.Wallpaper.Title, At = at });
            settings.History = settings.History.Take(20).ToList();
            settings.Save();
            library.Update(entry.With(appliedAt: at));
            Status("Wallpaper set · " + entry.Wallpaper.Title);
        }
        catch (Exception error) when (Operational(error)) { Report(error); }
        finally
        {
            changing = false;
            if (!quitting)
            {
                UpdateDesktopCard();
                RefreshTodayLabels();
                if (page == "library") RenderLibrary(item.Id);
                if (page == "rotation") ShowRecentChanges();
                PickQueue();
            }
        }
    }

    private async Task ToggleTodayFavoriteAsync()
    {
        if (changing || !CanApply(selected))
            return;
        changing = true;
        UpdateButtons();
        try
        {
            var entry = await library.SaveAsync(selected, bing, lifetime.Token);
            library.Update(entry.With(favorite: !entry.Favorite));
            Status(entry.Favorite ? "Removed from favorites" : "Added to favorites");
        }
        catch (Exception error) when (Operational(error)) { Report(error); }
        finally
        {
            changing = false;
            if (!quitting) { RefreshTodayLabels(); PickQueue(); }
        }
    }

    private void UpdateButtons()
    {
        if (!ready || quitting)
            return;
        apply.Enabled = !changing && CanApply(selected) && !IsCurrentFit(selected);
        favorite.Enabled = !changing && CanApply(selected);
        more.Enabled = selected != null;
        applySaved.Enabled = !changing && CanApply(savedEntry?.Wallpaper);
        revealSaved.Enabled = savedEntry != null && library.Exists(savedEntry);
        removeSaved.Enabled = !changing && savedEntry != null;
        editTitle.Enabled = !changing && savedEntry != null;
        savedFavorite.Enabled = savedRotation.Enabled = !changing && savedEntry != null;
        saveSettings.Enabled = !changing && !refreshing;
        previous.Enabled = next.Enabled = !changing && RotationPool().Count > 0;
        changeNow.Enabled = !changing && (queued != null || RotationPool().Count > 0);
    }

    private Wallpaper FindWallpaper(string id) => id == null ? null :
        library?.Find(id)?.Wallpaper ?? recent.Find(item => item.Id == id);

    private void UpdateDesktopCard()
    {
        Cancel(ref desktopRequest);
        desktopPreview.SetImage(null);
        var item = FindWallpaper(settings.CurrentId);
        nowTitle.Text = item?.Title ?? settings.History.FirstOrDefault()?.Title ?? "No wallpaper set";
        if (item == null)
            return;
        desktopRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        _ = LoadPhotoAsync(desktopPreview, item, true, desktopRequest.Token);
    }

    private List<Wallpaper> RotationPool()
    {
        if (settings.RotationMode == "daily" || settings.Pool == "bing")
        {
            var pool = recent.Where(item => (item.Downloadable || LocalPath(item) != null) &&
                library?.Find(item.Id)?.Rotation != false);
            return (settings.RotationMode == "daily" ? pool.Take(1) : pool).ToList();
        }
        return library?.Entries.Where(entry => entry.Rotation && library.Exists(entry) &&
            (settings.Pool != "favorites" || entry.Favorite))
            .OrderByDescending(entry => entry.Wallpaper.Date, StringComparer.Ordinal)
            .Select(entry => entry.Wallpaper).ToList() ?? new List<Wallpaper>();
    }

    private void PickQueue(bool another = false)
    {
        if (!ready || quitting) return;
        var pool = RotationPool();
        if (!another && queued != null && queued.Id != settings.CurrentId && pool.Any(item => item.Id == queued.Id))
        {
            PaintRotationStatus();
            return;
        }
        queued = plan.Pick(pool, settings, another, queued?.Id) ?? (another ? queued : null);
        Cancel(ref queueRequest);
        nextPreview.SetImage(null);
        if (queued != null)
        {
            queueRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            _ = LoadPhotoAsync(nextPreview, queued, true, queueRequest.Token);
        }
        PaintRotationStatus();
        UpdateButtons();
    }

    private async Task ChangeNowAsync()
    {
        if (changing) return;
        if (RotationPool().Count == 0)
            await RefreshAsync();
        PickQueue();
        await ApplyAsync(queued ?? RotationPool().FirstOrDefault());
    }

    private async Task StepAsync(int direction)
    {
        if (direction > 0) { await ChangeNowAsync(); return; }
        var previousItem = settings.History.Skip(1).Select(entry => FindWallpaper(entry.Id)).FirstOrDefault(CanApply);
        if (previousItem != null)
        {
            await ApplyAsync(previousItem);
            return;
        }
        var pool = RotationPool();
        int index = pool.FindIndex(candidate => candidate.Id == settings.CurrentId);
        if (pool.Count > 1 || index < 0 && pool.Count == 1)
            await ApplyAsync(pool[(index <= 0 ? pool.Count : index) - 1]);
    }

    private bool IsPaused(DateTimeOffset now) => settings.PausedUntil == "manual" ||
        DateTimeOffset.TryParse(settings.PausedUntil, out var until) && until > now;

    private bool WaitingForPower() => settings.PauseOnBattery &&
        SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;

    private DateTimeOffset? ResumeAt() =>
        DateTimeOffset.TryParse(settings.PausedUntil, out var until) && until > DateTimeOffset.Now ? until : (DateTimeOffset?)null;

    private void ResetSchedule()
    {
        plan.Reset(settings, DateTimeOffset.Now, ResumeAt());
        nextAttempt = DateTimeOffset.MinValue;
        ArmTimer();
        queued = null;
        PickQueue();
    }

    private void ArmTimer()
    {
        wallpaperTimer.Stop();
        if (settings.PausedUntil == "manual" || plan.Times.Count == 0)
            return;
        var target = nextAttempt > plan.Times[0] ? nextAttempt : plan.Times[0];
        wallpaperTimer.Interval = (int)Math.Max(250, Math.Min(int.MaxValue, (target - DateTimeOffset.Now).TotalMilliseconds));
        wallpaperTimer.Start();
    }

    private async Task OnWallpaperTimerAsync()
    {
        wallpaperTimer.Stop();
        if (quitting || plan.Times.Count == 0)
            return;
        var expected = plan.Times;
        try
        {
            if (changing || WaitingForPower())
            {
                nextAttempt = DateTimeOffset.Now.AddMinutes(1);
                return;
            }
            if (settings.Pool == "bing" || settings.RotationMode == "daily")
                await RefreshAsync(settings.RotationMode == "daily");
            if (quitting || settings.RotationEnabled != true || IsPaused(DateTimeOffset.Now) || expected != plan.Times)
                return;
            PickQueue();
            if (queued != null)
                await ApplyAsync(queued);
            nextAttempt = DateTimeOffset.MinValue;
            if (expected == plan.Times)
                plan.Advance(settings, DateTimeOffset.Now);
        }
        finally { if (!quitting) { ArmTimer(); PaintRotationStatus(); } }
    }

    private void TogglePaused()
    {
        if (settings.RotationEnabled != true)
            UpdateSettings(() => settings.RotationEnabled = true, true);
        else
            UpdateSettings(() => settings.PausedUntil = IsPaused(DateTimeOffset.Now) ? null : "manual", true);
    }

    private void ShowPauseChoices()
    {
        Popup(pauseFor, ("15 minutes", () => PauseFor(15)), ("1 hour", () => PauseFor(60)),
            ("4 hours", () => PauseFor(240)), ("Until resumed", () => PauseFor(null)),
            ("Resume now", () => UpdateSettings(() => settings.PausedUntil = null, true)));
    }

    private void PauseFor(int? minutes) => UpdateSettings(() => settings.PausedUntil = minutes.HasValue
        ? DateTimeOffset.Now.AddMinutes(minutes.Value).ToString("o", CultureInfo.InvariantCulture) : "manual", true);

    private void ShowFitChoices()
    {
        Popup(fit, new[] { "Fill", "Fit", "Stretch", "Center", "Tile" }.Select(value =>
            (value, (Action)(() => { UpdateSettings(() => settings.Fit = value, false); RefreshTodayLabels(); }))).ToArray());
    }

    private void ShowTodayActions()
    {
        Popup(more, ("Refresh from Bing", async () => await RefreshAsync(true)),
            ("About this image", OpenAbout), ("Show in Library", () => { ShowPage("library"); RenderLibrary(selected?.Id); }),
            ("Open library folder", OpenLibrary), ("Minimize to tray", Hide));
    }

    private void OpenAbout()
    {
        if (Uri.TryCreate(selected?.CopyrightLink, UriKind.Absolute, out var website) &&
            (website.Scheme == "https" || website.Scheme == "http"))
            Attempt(() => Process.Start(new ProcessStartInfo(website.AbsoluteUri) { UseShellExecute = true }));
    }

    private void OpenLibrary()
    {
        if (library == null) return;
        Attempt(() =>
        {
            Directory.CreateDirectory(library.DirectoryPath);
            Process.Start(new ProcessStartInfo(library.DirectoryPath) { UseShellExecute = true });
        });
    }

    internal void Reveal(string path) => Attempt(() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }));

    internal void Copy(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            Attempt(() => Clipboard.SetText(text));
    }

    private static ContextMenuStrip MakeMenu() => new ContextMenuStrip
    {
        ShowImageMargin = false, BackColor = Palette.Surface, ForeColor = Palette.Text, Font = Palette.Font(13)
    };

    private static void Popup(Control anchor, params (string Title, Action Action)[] actions)
    {
        var menu = MakeMenu();
        foreach (var item in actions)
            menu.Items.Add(item.Title, null, (_, __) => item.Action());
        menu.Closed += (_, __) => menu.Dispose();
        menu.Show(anchor, new Point(0, anchor.Height + S(3)));
    }

    internal void Attempt(Action action)
    {
        try { action(); }
        catch (Exception error) when (Operational(error)) { Report(error); }
    }

    private void Status(string value, bool error = false)
    {
        if (quitting)
            return;
        footer.Text = value;
        footer.Visible = !string.IsNullOrEmpty(value);
        footer.ForeColor = error ? Palette.Error : Palette.Muted;
        tip.SetToolTip(footer, value);
    }

    internal void Report(Exception error)
    {
        if (quitting)
            return;
        Status(ErrorText(error), true);
        if (!Visible)
            tray?.ShowBalloonTip(3500, "Dayframe", ErrorText(error), ToolTipIcon.Warning);
    }

    private static bool Operational(Exception error) => error is IOException || error is UnauthorizedAccessException ||
        error is ArgumentException || error is HttpRequestException || error is OperationCanceledException ||
        error is SerializationException || error is System.Security.SecurityException || error is Win32Exception ||
        error is System.Runtime.InteropServices.ExternalException;

    private static string ErrorText(Exception error) => error is OperationCanceledException ? "The request timed out or was cancelled."
        : error.InnerException is System.Net.WebException ? error.InnerException.Message : error.Message;

    internal static string FormatBytes(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / (1024 * 1024d):0.#} MB"
        : bytes >= 1024 ? $"{bytes / 1024d:0.#} KB" : $"{bytes} B";

    private static string Attribution(Wallpaper wallpaper)
    {
        string text = wallpaper?.Copyright ?? "";
        int start = text.LastIndexOf('©');
        return start < 0 ? text : text.Substring(start).TrimEnd(' ', ')');
    }

    private static void Cancel(ref CancellationTokenSource source)
    {
        source?.Cancel();
        source?.Dispose();
        source = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            quitting = true;
            lifetime.Cancel();
            Cancel(ref heroRequest); Cancel(ref recentRequest); Cancel(ref galleryRequest); Cancel(ref inspectorRequest);
            Cancel(ref desktopRequest); Cancel(ref queueRequest); Cancel(ref historyRequest);
            wallpaperTimer.Dispose(); clock.Dispose(); tray?.Dispose(); trayMenu?.Dispose();
            bing?.Dispose(); lifetime.Dispose(); tip.Dispose();
        }
        base.Dispose(disposing);
    }
}
