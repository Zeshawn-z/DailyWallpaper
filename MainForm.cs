using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
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
    private ContextMenuStrip trayMenu;
    private readonly BingTitles titles = new BingTitles();
    private BingClient titleRequest;
    private Settings settings;
    private WallpaperLibrary library;
    private BingClient bing;
    private List<Wallpaper> recent = new List<Wallpaper>();
    private Wallpaper selected;
    private SavedWallpaper savedEntry;
    private CancellationTokenSource heroRequest, recentRequest, inspectorRequest, desktopRequest;
    private DateTimeOffset refreshAfter, nextAttempt;
    private bool ready, loadingControls, refreshing, changing, quitting, editingDetails;
    private bool wasMinimized, restoreRepaintPending;
    private int libraryFilter, order;

    public MainForm() : this(false) { }

    public MainForm(bool startHidden)
    {
        string startupIssue = null;
        try
        {
            settings = Settings.Load();
            Strings.SetLanguage(settings.Language);
            Validate(settings);
        }
        catch (Exception error) when (Operational(error))
        {
            startupIssue = error.Message;
            settings = new Settings();
        }
        Strings.SetLanguage(settings.Language);
        Palette.SetTheme(settings.Theme);
        InitializeWindow();
        BuildUi();
        try { library = new WallpaperLibrary(settings.LibraryDirectory); }
        catch (Exception error) when (Operational(error)) { startupIssue = error.Message; }
        bing = new BingClient(settings);
        try
        {
            recent = GetCached();
            titles.Load();
            titles.Add(recent);
            titles.Add(BingClient.CachedFeed(settings.Market == "zh-CN" ? "en-US" : "zh-CN") ?? new List<Wallpaper>());
        }
        catch (Exception error) when (Operational(error)) { startupIssue = error.Message; }

        tray = new NotifyIcon { Text = "Dayframe " + Program.Version, Icon = Icon, Visible = true };
        RebuildTrayMenu();
        tray.DoubleClick += (_, __) => ShowFromTray();
        wallpaperTimer.Tick += async (_, __) => await OnWallpaperTimerAsync();
        clock.Tick += (_, __) => PaintRotationStatus();
        VisibleChanged += (_, __) =>
        {
            if (Visible && ready)
            {
                clock.Start();
                QueueRestoreRepaint();
            }
            else clock.Stop();
        };
        ready = true;
        LoadOptions();
        ResetSchedule();
        if (startupIssue != null)
            NotifyError(startupIssue);
        Shown += async (_, __) =>
        {
            if (startHidden)
                Hide();
            else
                ShowRecent();
            await RefreshAsync();
        };
    }

    private void RebuildTrayMenu()
    {
        var previousMenu = trayMenu;
        trayMenu = MakeMenu();
        trayMenu.Items.Add(Strings.Get("Open Dayframe"), null, (_, __) => ShowFromTray());
        trayMenu.Items.Add(Strings.Get("Next wallpaper"), null, async (_, __) => await ChangeNowAsync());
        trayMenu.Items.Add(Strings.Get("Pause / resume auto-change"), null, (_, __) => TogglePaused());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(Strings.Get("Exit"), null, (_, __) => { quitting = true; Close(); });
        tray.ContextMenuStrip = trayMenu;
        previousMenu?.Dispose();
    }

    private void ShowPage(string view)
    {
        page = view;
        PlaceActivePage();
        Panel active = ActivePage;
        active.BringToFront();
        active.Show();
        foreach (Control other in shell.Controls)
            if (other != active)
                other.Hide();
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
        }
        else if (view == "today" && recentCards.Controls.Count == 0)
            ShowRecent();
        else if (view == "rotation")
        {
            ConfigureRotationUi();
            ShowRecentChanges();
            PaintRotationStatus();
        }
    }

    internal bool IsResizing { get; private set; }

    protected override void WndProc(ref Message message)
    {
        const int SizeMessage = 0x0005;
        const int SizeMinimized = 1;
        base.WndProc(ref message);
        if (message.Msg != SizeMessage)
            return;
        if (message.WParam.ToInt32() == SizeMinimized)
            wasMinimized = true;
        else if (wasMinimized)
        {
            wasMinimized = false;
            QueueRestoreRepaint();
        }
    }

    private void QueueRestoreRepaint()
    {
        if (restoreRepaintPending || !ready || !IsHandleCreated)
            return;
        restoreRepaintPending = true;
        BeginInvoke((Action)(() =>
        {
            restoreRepaintPending = false;
            if (quitting || !Visible || WindowState == FormWindowState.Minimized)
                return;
            const uint InvalidateRegion = 0x0001, IncludeChildren = 0x0080, PaintNow = 0x0100;
            RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, InvalidateRegion | IncludeChildren | PaintNow);
        }));
    }

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr window, IntPtr area, IntPtr region, uint flags);

    protected override void OnResizeBegin(EventArgs e)
    {
        IsResizing = true;
        base.OnResizeBegin(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState != FormWindowState.Minimized)
            PlaceUi();
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        IsResizing = false;
        if (page == "today")
            todayPage.Invalidate(true);
        else if (page == "library")
        {
            libraryPage.Invalidate(true);
        }
        base.OnResizeEnd(e);
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
            SaveTitlesIfChanged(titles.Add(fetched));
            refreshAfter = DateTimeOffset.Now.AddHours(1);
            if (page == "today" && Visible && (changed || selected == null || force))
                ShowRecent();
            else if (changed)
            {
                ClearRecentCards();
                selected = null;
            }
            PickQueue();
            _ = RefreshTitlesAsync();
        }
        catch (Exception error) when (Operational(error))
        {
            if (quitting) return;
            refreshAfter = DateTimeOffset.Now.AddMinutes(10);
            if (recent.Count == 0)
                heroTitle.Text = Strings.Get("Could not load Bing wallpapers");
            NotifyError(recent.Count == 0 ? ErrorText(error) : Strings.Get("Showing cached Bing wallpapers"));
        }
        finally { refreshing = false; UpdateButtons(); }
    }

    private async Task RefreshTitlesAsync()
    {
        var client = bing;
        if (client == titleRequest)
            return;
        titleRequest = client;
        string otherMarket = settings.Market == "zh-CN" ? "en-US" : "zh-CN";
        try
        {
            var alternate = await client.FetchArchiveAsync(otherMarket, 0, lifetime.Token);
            if (quitting || client != bing) return;
            bool changed = titles.Add(alternate);
            SaveTitlesIfChanged(changed);
            if (changed)
                RefreshDisplayedTitles();

            if (recent.Any(item => titles.Missing(item, otherMarket)))
            {
                // The endpoints can differ by a day. Check the adjacent archive page by image ID.
                var olderTitles = await client.FetchArchiveAsync(otherMarket, 7, lifetime.Token);
                if (quitting || client != bing) return;
                changed = titles.Add(olderTitles);
                SaveTitlesIfChanged(changed);
                if (changed)
                    RefreshDisplayedTitles();
            }
        }
        catch (Exception error) when (Operational(error))
        {
            Debug.WriteLine("Bing alternate titles: " + error.Message);
        }
        finally
        {
            if (titleRequest == client)
                titleRequest = null;
        }
    }

    private void SaveTitlesIfChanged(bool changed)
    {
        if (!changed) return;
        try { titles.Save(); }
        catch (Exception error) when (Operational(error)) { Debug.WriteLine("Bing title cache: " + error.Message); }
    }

    private string TitleFor(Wallpaper item) => titles.Get(item, settings.Language);

    private string TitleFor(ChangeRecord entry) => entry == null ? null :
        TitleFor(FindWallpaper(entry.Id)) ?? titles.Get(entry.Id, entry.Title, settings.Language);

    private void RefreshDisplayedTitles()
    {
        RefreshTodayLabels();
        if (page == "library" && order == 2)
            RenderLibrary();
        else
            gallery.Invalidate();
        if (savedEntry != null && !editTitle.Focused)
            editTitle.Text = TitleFor(savedEntry.Wallpaper);
        UpdateDesktopTitle();
        UpdateHistoryTitles();
        PaintRotationStatus();
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
            var tile = new PhotoTile(item, newest ? Strings.Get("Today") : Strings.Date(item.Day, "ddd d", "ddd d日"),
                TitleFor(item))
            {
                Dock = DockStyle.Fill, Margin = new Padding(0, 0, S(4), 0)
            };
            recentCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / recent.Count));
            recentCards.Controls.Add(tile);
            tile.Click += (_, __) => SelectToday(item);
            tip.SetToolTip(tile, tile.DisplayTitle);
            _ = LoadTileAsync(tile, recentRequest.Token);
        }
        recentCards.ResumeLayout();
        RefreshTodayLabels();
    }

    private void SelectToday(Wallpaper item)
    {
        Cancel(ref heroRequest);
        if (item == null || item.Id == selected?.Id && hero.Loaded ||
            item.BingPath == null && LocalPath(item) == null)
        {
            if (item == null || item.Id != selected?.Id)
                hero.SetImage(null);
            selected = item;
            RefreshTodayLabels();
            return;
        }
        heroRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        _ = LoadPhotoAsync(hero, item, false, heroRequest.Token, () =>
        {
            selected = item;
            RefreshTodayLabels();
        });
    }

    private void RefreshTodayLabels()
    {
        var item = selected;
        var entry = library?.Find(item?.Id);
        bool current = item != null && settings?.CurrentId == item.Id;
        bool saved = entry != null && library.Exists(entry);
        bool latest = item != null && recent.FirstOrDefault()?.Id == item.Id;
        todayDate.Text = item == null ? "Bing" : Strings.Date(item.Day, "ddd, d MMM", "M月d日 ddd");
        if (latest)
            todayDate.Text = Strings.Get("Today") + " · " + todayDate.Text;
        todayState.Text = Strings.Get(current ? "On your desktop" : saved ? "Saved" : "Not set yet");
        todayState.Glyph = current ? "desktop" : saved ? "check" : null;
        todayState.GlyphSize = 14;
        todayState.Width = S(current ? 148 : 112);
        todayState.GlyphColor = current ? Palette.Amber : Palette.PhotoBlue;
        todayState.Invalidate();
        resolution.Text = entry != null ? $"{entry.Width} × {entry.Height}" : item?.Downloadable == true ? "UHD" : "";
        resolution.Visible = item != null;
        heroTitle.Text = TitleFor(item) ?? Strings.Get("Getting today’s wallpaper…");
        heroCredit.Text = Attribution(item);
        tip.SetToolTip(heroCredit, item?.Copyright ?? "");
        about.Visible = !string.IsNullOrEmpty(item?.CopyrightLink);
        string fitMode = settings.Fit;
        fit.Text = Strings.Get(fitMode);
        hero.Contain = fitMode == "Fit" || fitMode == "Center";
        favorite.GlyphColor = entry?.Favorite == true ? Palette.Amber : Palette.PhotoInk;
        favorite.AccessibleName = Strings.Get(entry?.Favorite == true ? "Remove from favorites" : "Add to favorites");
        favorite.Invalidate();
        apply.Text = Strings.Get(changing ? "Working…" : IsCurrentFit(item) ? "Current wallpaper" : "Set as wallpaper");
        fileHint.Text = item == null ? "" : saved ? $"{entry.FileName} · {FormatBytes(library.Bytes(entry))}" :
            item.Downloadable ? Strings.Format("Saves as {0}", library?.ProposedName(item, TitleFor(item)) ??
                TitleFor(item) + "_" + item.Date + ".jpg") : Strings.Get("Download unavailable");
        marketName.Text = Strings.Get(settings?.Market == "zh-CN" ? "Bing · China" : "Bing · United States");
        if (page == "today")
            PlaceToday(Logic(todayPage.Width), Logic(todayPage.Height));
        foreach (PhotoTile tile in recentCards.Controls)
        {
            bool selectedTile = item?.Id == tile.Wallpaper.Id;
            var stored = library?.Find(tile.Wallpaper.Id);
            bool savedTile = stored != null && library.Exists(stored);
            bool desktopTile = settings?.CurrentId == tile.Wallpaper.Id;
            if (tile.Selected != selectedTile || tile.Saved != savedTile || tile.OnDesktop != desktopTile)
            {
                tile.Selected = selectedTile;
                tile.Saved = savedTile;
                tile.OnDesktop = desktopTile;
                tile.Invalidate();
            }
            tile.SetTitle(TitleFor(tile.Wallpaper));
            tip.SetToolTip(tile, tile.DisplayTitle);
        }
        UpdateButtons();
    }

    private string LocalPath(Wallpaper item)
    {
        var stored = library?.Find(item.Id);
        return stored != null && library.Exists(stored) ? library.PathOf(stored) : null;
    }

    private async Task LoadPhotoAsync(PhotoView view, Wallpaper item, bool thumbnail, CancellationToken token,
        Action loaded = null)
    {
        try
        {
            string file = LocalPath(item);
            if (file == null && item.BingPath == null)
                return;
            var image = await bing.PreviewAsync(item, file, thumbnail, token);
            if (token.IsCancellationRequested || view.IsDisposed) image.Dispose();
            else
            {
                view.SetImage(image);
                tip.SetToolTip(view, "");
                loaded?.Invoke();
            }
        }
        catch (Exception error) when (Operational(error))
        {
            if (!token.IsCancellationRequested && !view.IsDisposed)
            {
                tip.SetToolTip(view, ErrorText(error));
                if (view == hero)
                    Report(error);
            }
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
                tip.SetToolTip(tile, tile.DisplayTitle + " · " + ErrorText(error));
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
            var entry = await library.SaveAsync(item, bing, lifetime.Token, TitleFor(item));
            await Task.Run(() => DesktopWallpaper.Apply(library.PathOf(entry), settings.Fit), lifetime.Token);
            string at = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);
            settings.CurrentId = item.Id;
            settings.CurrentFit = settings.Fit;
            settings.History.Insert(0, new ChangeRecord { Id = item.Id, Title = entry.Wallpaper.Title, At = at });
            settings.History = settings.History.Take(20).ToList();
            settings.Save();
            library.Update(entry.With(appliedAt: at));
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
            var entry = await library.SaveAsync(selected, bing, lifetime.Token, TitleFor(selected));
            library.Update(entry.With(favorite: !entry.Favorite));
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
        bool canRotate = !changing && RotationCandidates().Any();
        previous.Enabled = next.Enabled = canRotate;
        changeNow.Enabled = !changing && (queued != null || canRotate);
    }

    private Wallpaper FindWallpaper(string id) => id == null ? null :
        library?.Find(id)?.Wallpaper ?? recent.Find(item => item.Id == id);

    private void UpdateDesktopCard()
    {
        Cancel(ref desktopRequest);
        var item = FindWallpaper(settings.CurrentId);
        UpdateDesktopTitle();
        if (item == null)
        {
            desktopPreview.SetImage(null);
            return;
        }
        desktopRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        _ = LoadPhotoAsync(desktopPreview, item, true, desktopRequest.Token);
    }

    private void UpdateDesktopTitle()
    {
        nowTitle.Text = TitleFor(FindWallpaper(settings.CurrentId)) ?? TitleFor(settings.History.FirstOrDefault()) ??
            Strings.Get("No wallpaper set");
    }

    private IEnumerable<Wallpaper> RotationCandidates()
    {
        if (settings.RotationMode == "daily" || settings.Pool == "bing")
        {
            var pool = recent.Where(item => (item.Downloadable || LocalPath(item) != null) &&
                library?.Find(item.Id)?.Rotation != false);
            return settings.RotationMode == "daily" ? pool.Take(1) : pool;
        }
        return library?.Entries.Where(entry => entry.Rotation && library.Exists(entry) &&
            (settings.Pool != "favorites" || entry.Favorite))
            .Select(entry => entry.Wallpaper) ?? Enumerable.Empty<Wallpaper>();
    }

    private List<Wallpaper> RotationPool()
    {
        var candidates = RotationCandidates();
        return (settings.RotationMode == "daily" || settings.Pool == "bing" ? candidates :
            candidates.OrderByDescending(item => item.Date, StringComparer.Ordinal)).ToList();
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
        UpdateQueuePreview();
        PaintRotationStatus();
        UpdateButtons();
    }

    private void UpdateQueuePreview()
    {
        Cancel(ref queueRequest);
        if (queued != null)
        {
            queueRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            _ = LoadPhotoAsync(nextPreview, queued, true, queueRequest.Token);
        }
        else
            nextPreview.SetImage(null);
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
        ShowImageMargin = false, BackColor = Palette.Surface, ForeColor = Palette.Text, Font = Palette.Font(13),
        Renderer = new ToolStripProfessionalRenderer(new MenuColors())
    };

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Palette.Surface;
        public override Color MenuBorder => Palette.Line;
        public override Color MenuItemBorder => Palette.Line;
        public override Color MenuItemSelected => Palette.Hover;
        public override Color SeparatorDark => Palette.Line;
        public override Color SeparatorLight => Palette.Line;
    }

    private void Popup(Control anchor, params (string Title, Action Action)[] actions)
    {
        var menu = MakeMenu();
        foreach (var item in actions)
            menu.Items.Add(Strings.Get(item.Title), null, (_, __) => item.Action());
        // WinForms still uses the menu handle after raising Closed.
        menu.Closed += (_, __) => BeginInvoke((Action)menu.Dispose);
        menu.Show(anchor, new Point(0, anchor.Height + S(3)));
    }

    internal void Attempt(Action action)
    {
        try { action(); }
        catch (Exception error) when (Operational(error)) { Report(error); }
    }

    private void NotifyError(string message)
    {
        if (!quitting)
            tray?.ShowBalloonTip(3500, "Dayframe", message, ToolTipIcon.Warning);
    }

    internal void Report(Exception error) => NotifyError(ErrorText(error));

    private static bool Operational(Exception error) => error is IOException || error is UnauthorizedAccessException ||
        error is ArgumentException || error is HttpRequestException || error is OperationCanceledException ||
        error is SerializationException || error is System.Security.SecurityException || error is Win32Exception ||
        error is System.Runtime.InteropServices.ExternalException;

    private static string ErrorText(Exception error) => error is OperationCanceledException ? Strings.Get("The request timed out or was cancelled.")
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
            metadataWindow?.Close();
            lifetime.Cancel();
            Cancel(ref heroRequest); Cancel(ref recentRequest); Cancel(ref inspectorRequest);
            Cancel(ref desktopRequest); Cancel(ref queueRequest); Cancel(ref historyRequest);
            wallpaperTimer.Dispose(); clock.Dispose(); tray?.Dispose(); trayMenu?.Dispose();
            bing?.Dispose(); lifetime.Dispose(); tip.Dispose();
        }
        base.Dispose(disposing);
    }
}
