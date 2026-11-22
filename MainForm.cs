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

internal sealed partial class MainForm : Form
{
    private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
    private readonly Timer rotation = new Timer();
    private readonly Random random = new Random();
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem nextRotation, trayApply;
    private readonly ContextMenuStrip trayMenu;
    private Settings settings;
    private WallpaperLibrary library;
    private BingClient bing;
    private List<Wallpaper> recent = new List<Wallpaper>();
    private Wallpaper selected;
    private CancellationTokenSource cardRequests, heroRequest, savedRequest;
    private DateTime nextFeedAttempt;
    private DateTimeOffset nextChange;
    private bool refreshing, changing, renderingLibrary, quitting, trayExplained, needsExploreRefresh = true;

    public MainForm()
    {
        BuildUi();
        string startupError = null;
        try
        {
            settings = Settings.Load();
            ValidateSettings(settings);
        }
        catch (Exception error) when (OperationalError(error))
        {
            startupError = "设置读取失败，当前使用默认设置。 " + ErrorText(error);
            settings = new Settings();
        }

        try { library = new WallpaperLibrary(settings.LibraryDirectory); }
        catch (Exception error) when (OperationalError(error)) { startupError = "无法打开壁纸库。 " + ErrorText(error); }
        bing = new BingClient(settings);
        try
        {
            var cached = BingClient.CachedFeed();
            if (cached != null && cached.Count <= 8 && cached.All(item => item?.Id != null &&
                DateTime.TryParseExact(item.Date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
                recent = cached;
        }
        catch (Exception error) when (OperationalError(error)) { startupError = "预览缓存读取失败。 " + ErrorText(error); }

        trayMenu = new ContextMenuStrip { ShowImageMargin = false };
        nextRotation = new ToolStripMenuItem { Enabled = false };
        trayMenu.Items.Add("打开 Bing Wallpapers", null, (_, __) => ShowFromTray());
        trayApply = new ToolStripMenuItem("应用最新壁纸", null, async (_, __) =>
        {
            if (recent.Count == 0)
                await RefreshFeedAsync(false);
            await ChangeWallpaperAsync(recent.FirstOrDefault(item => item.Downloadable));
        });
        trayMenu.Items.Add(trayApply);
        trayMenu.Items.Add(nextRotation);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("退出", null, (_, __) => { quitting = true; Close(); });
        tray = new NotifyIcon { Text = "Bing Wallpapers", Icon = Icon, Visible = true, ContextMenuStrip = trayMenu };
        tray.DoubleClick += (_, __) => ShowFromTray();
        rotation.Tick += async (_, __) => await RotateAsync();

        LoadSettingsFields();
        RenderLibrary();
        ScheduleRotation();
        Status(startupError ?? "正在连接 Bing…", startupError != null);
        Shown += async (_, __) =>
        {
            ShowRecent();
            if (startupError != null)
                MessageBox.Show(this, startupError, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            await RefreshFeedAsync(false);
        };
    }

    private void ShowPage(string view)
    {
        page = view;
        explore.Visible = view == "explore";
        collection.Visible = view == "library";
        preferences.Visible = view == "settings";
        HighlightNav(exploreNav, explore.Visible);
        HighlightNav(libraryNav, collection.Visible);
        HighlightNav(settingsNav, preferences.Visible);
        if (view == "explore" && bing != null && Visible && needsExploreRefresh)
            ShowRecent();
        if (view == "library" && library != null)
            UpdateSavedPreview();
        else if (savedRequest != null)
        {
            Cancel(ref savedRequest);
            savedPhoto.SetPhoto(null);
        }
    }

    private static void HighlightNav(ActionButton button, bool selectedNav)
    {
        button.Fill = selectedNav ? Theme.Tint : Theme.Rail;
        button.Stroke = button.Fill;
        button.ForeColor = selectedNav ? Theme.Accent : Theme.Ink;
        button.Invalidate();
    }

    public void ShowFromTray()
    {
        if (quitting)
            return;
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
        if (page == "explore" && needsExploreRefresh)
            ShowRecent();
        if (DateTime.UtcNow >= nextFeedAttempt)
            _ = RefreshFeedAsync(false);
    }

    private void HideToTray()
    {
        Hide();
        if (!trayExplained)
        {
            tray.ShowBalloonTip(3500, "Bing Wallpapers", "程序正在后台运行。双击托盘图标返回窗口。", ToolTipIcon.Info);
            trayExplained = true;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (!quitting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
        }
    }

    private async Task RefreshFeedAsync(bool explicitRefresh)
    {
        if (quitting || refreshing || (!explicitRefresh && DateTime.UtcNow < nextFeedAttempt))
            return;

        refreshing = true;
        refresh.Enabled = false;
        saveSettings.Enabled = false;
        if (Visible)
            Status("正在更新 Bing 壁纸列表…");
        try
        {
            var fetched = await bing.FetchFeedAsync(lifetime.Token);
            if (quitting)
                return;

            bool sameList = recent.Select(item => item.Id + item.Date).SequenceEqual(fetched.Select(item => item.Id + item.Date));
            recent = fetched;
            nextFeedAttempt = DateTime.UtcNow.AddHours(2);
            needsExploreRefresh |= !sameList || selected == null;
            if (Visible && page == "explore" && (needsExploreRefresh || explicitRefresh))
                ShowRecent(explicitRefresh);
            Status("Bing 列表已更新");
        }
        catch (Exception error) when (OperationalError(error))
        {
            if (quitting)
                return;
            nextFeedAttempt = DateTime.UtcNow.AddMinutes(10);
            if (recent.Count == 0)
                hero.Describe("BING 中国区", "暂时无法连接 Bing", "可刷新重试，或在设置中配置代理。");
            Status((recent.Count == 0 ? "连接 Bing 失败。 " : "当前显示缓存。 ") + ErrorText(error), true);
        }
        finally
        {
            refreshing = false;
            UpdateActionStates();
        }
    }

    private void ShowRecent(bool latest = false)
    {
        var item = latest ? recent.FirstOrDefault() : recent.Find(wallpaper => wallpaper.Id == selected?.Id) ?? recent.FirstOrDefault();
        SelectWallpaper(item);
        RenderRecent();
        needsExploreRefresh = false;
    }

    private void RenderRecent()
    {
        Cancel(ref cardRequests);
        cardRequests = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        recentCards.SuspendLayout();
        while (recentCards.Controls.Count > 0)
            recentCards.Controls[0].Dispose();
        recentCards.ColumnStyles.Clear();
        recentCards.ColumnCount = Math.Max(1, recent.Count);
        for (int index = 0; index < recent.Count; index++)
        {
            var item = recent[index];
            var card = new Thumbnail(item)
            {
                Dock = DockStyle.Fill, Selected = item.Id == selected?.Id,
                Margin = new Padding(0, 0, index == recent.Count - 1 ? 0 : S(8), 0)
            };
            recentCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / recent.Count));
            recentCards.Controls.Add(card, index, 0);
            tip.SetToolTip(card, item.Title + "  ·  " + item.DisplayDate);
            card.Click += (_, __) => SelectWallpaper(item);
            _ = LoadThumbnailAsync(card, cardRequests.Token);
        }
        recentCards.ResumeLayout();
        recentCount.Text = recent.Count == 0 ? "" : $"{recent.Count} 张";
    }

    private async Task LoadThumbnailAsync(Thumbnail card, CancellationToken token)
    {
        try
        {
            var image = await bing.PreviewAsync(card.Item, LocalFile(card.Item), true, token);
            if (!token.IsCancellationRequested && !card.IsDisposed)
                card.SetPhoto(image);
            else
                image.Dispose();
        }
        catch (Exception error) when (OperationalError(error))
        {
            if (!token.IsCancellationRequested && !card.IsDisposed)
                tip.SetToolTip(card, card.Item.Title + " · 缩略图加载失败。 " + ErrorText(error));
        }
    }

    private void SelectWallpaper(Wallpaper item)
    {
        selected = item;
        Cancel(ref heroRequest);
        hero.SetPhoto(null);
        foreach (Thumbnail card in recentCards.Controls)
        {
            card.Selected = item?.Id == card.Item.Id;
            card.Invalidate();
        }
        if (item == null)
            hero.Describe("BING 中国区", "正在获取每日壁纸", "网络连接成功后会显示预览。");
        else
        {
            string date = item.Date == DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                ? "今日壁纸  ·  " + item.DisplayDate : item.DisplayDate;
            hero.Describe(date, item.Title, item.Copyright);
            tip.SetToolTip(hero, item.Copyright);
            heroRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            _ = LoadPreviewAsync(hero, item, LocalFile(item), heroRequest.Token);
        }
        UpdateActionStates();
    }

    private async Task LoadPreviewAsync(PhotoPanel target, Wallpaper item, string file, CancellationToken token)
    {
        try
        {
            var image = await bing.PreviewAsync(item, file, false, token);
            if (!token.IsCancellationRequested && !target.IsDisposed)
                target.SetPhoto(image);
            else
                image.Dispose();
        }
        catch (Exception error) when (OperationalError(error))
        {
            if (!token.IsCancellationRequested && !target.IsDisposed)
            {
                target.Describe(item.DisplayDate, target.Large ? item.Title : "无法预览", "预览加载失败。 " + ErrorText(error));
                Status("预览加载失败。 " + ErrorText(error), true);
            }
        }
    }

    private string LocalFile(Wallpaper item)
    {
        var entry = library?.Find(item.Id);
        if (entry == null)
            return null;
        string path = library.PathOf(entry);
        return File.Exists(path) ? path : null;
    }

    private async Task ChangeWallpaperAsync(Wallpaper wallpaper, bool automatic = false)
    {
        if (quitting || changing || wallpaper == null)
            return;
        if (library == null)
        {
            Status("壁纸库不可用，请在设置中选择保存目录。", true);
            return;
        }
        changing = true;
        UpdateActionStates();
        Status("正在保存并设置壁纸…");
        bool saved = false;
        bool applied = false;
        try
        {
            var entry = await library.SaveAsync(wallpaper, bing, lifetime.Token);
            saved = true;
            lifetime.Token.ThrowIfCancellationRequested();
            await Task.Run(() => DesktopWallpaper.Apply(library.PathOf(entry)), lifetime.Token);
            applied = true;
            settings.CurrentId = wallpaper.Id;
            settings.Save();
            Status("已设为壁纸  ·  " + wallpaper.Title);
        }
        catch (Exception error) when (OperationalError(error))
        {
            if (quitting)
                return;
            string message = (applied ? "壁纸已更换，无法保存应用状态。 " :
                saved ? "图片已保存，设置壁纸失败。 " : "换图失败。 ") + ErrorText(error);
            Status(message, true);
            if (automatic && !Visible)
                tray.ShowBalloonTip(4000, "自动换图", message, ToolTipIcon.Warning);
        }
        finally
        {
            changing = false;
            if (!quitting)
            {
                if (saved)
                    RenderLibrary(wallpaper.Id);
                UpdateActionStates();
            }
        }
    }

    private void ScheduleRotation()
    {
        rotation.Stop();
        if (settings.RotationMode == "off")
        {
            schedule.Text = "自动换图已关闭";
            nextRotation.Text = schedule.Text;
        }
        else
        {
            int minutes = settings.RotationMode == "random"
                ? random.Next(settings.RandomMinimum, settings.RandomMaximum + 1)
                : settings.Interval;
            nextChange = DateTimeOffset.Now.AddMinutes(minutes);
            rotation.Interval = minutes * 60_000;
            rotation.Start();
            string due = nextChange.Date == DateTime.Today
                ? nextChange.ToString("HH:mm") : nextChange.ToString("MM-dd HH:mm");
            schedule.Text = "下次换图 " + due;
            nextRotation.Text = schedule.Text;
        }
    }

    private async Task RotateAsync()
    {
        rotation.Stop();
        try
        {
            if (quitting || changing)
                return;
            await RefreshFeedAsync(false);
            if (quitting || changing || settings.RotationMode == "off")
                return;

            var cached = library?.Entries.Where(entry => File.Exists(library.PathOf(entry)))
                .Select(entry => entry.Wallpaper) ?? Enumerable.Empty<Wallpaper>();
            var candidates = recent.Where(item => item.Downloadable).Concat(cached)
                .GroupBy(item => item.Id).Select(group => group.First())
                .OrderByDescending(item => item.Date, StringComparer.Ordinal).ToList();
            int current = candidates.FindIndex(item => item.Id == settings.CurrentId);
            if (candidates.Count == 0 || (candidates.Count == 1 && current == 0))
            {
                Status("自动换图暂停到下一次触发，目前没有其他可用壁纸。");
                return;
            }
            Wallpaper next;
            if (settings.RotationMode == "random")
            {
                if (current >= 0)
                    candidates.RemoveAt(current);
                next = candidates[random.Next(candidates.Count)];
            }
            else
                next = candidates[(current + 1) % candidates.Count];

            await ChangeWallpaperAsync(next, true);
        }
        finally
        {
            if (!quitting)
                ScheduleRotation();
        }
    }

    private SavedWallpaper SelectedSaved => savedGrid.CurrentRow?.Tag as SavedWallpaper;

    private void RenderLibrary(string preferredId = null)
    {
        preferredId = preferredId ?? SelectedSaved?.Wallpaper.Id;
        string filter = search.Text.Trim();
        var matches = library?.Entries.Where(entry => filter.Length == 0 ||
            entry.Wallpaper.Title.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
            entry.Wallpaper.Date.Contains(filter.Replace("-", "").Replace(".", ""))).ToList()
            ?? new List<SavedWallpaper>();
        librarySub.Text = library == null ? "保存目录不可用" : $"{library.Entries.Count} 张  ·  {library.DirectoryPath}";
        tip.SetToolTip(librarySub, library?.DirectoryPath);
        renderingLibrary = true;
        savedGrid.Rows.Clear();
        int preferredRow = 0;
        foreach (var entry in matches)
        {
            int index = savedGrid.Rows.Add(entry.Wallpaper.Title, entry.Wallpaper.DisplayDate);
            savedGrid.Rows[index].Tag = entry;
            if (entry.Wallpaper.Id == preferredId)
                preferredRow = index;
        }
        if (matches.Count != 0)
            savedGrid.CurrentCell = savedGrid.Rows[preferredRow].Cells[0];
        libraryEmpty.Controls[0].Text = filter.Length == 0 ? "这里还没有壁纸" : "没有找到匹配的壁纸";
        libraryEmpty.Visible = matches.Count == 0;
        savedGrid.Visible = matches.Count != 0;
        renderingLibrary = false;
        UpdateSavedPreview();
    }

    private void UpdateSavedPreview()
    {
        if (renderingLibrary || page != "library")
            return;
        Cancel(ref savedRequest);
        savedPhoto.SetPhoto(null);
        var entry = SelectedSaved;
        if (entry == null)
        {
            savedPhoto.Describe("", "壁纸预览", "");
            savedTitle.Text = "";
            savedDetail.Text = "换过的壁纸会保存在这里。";
        }
        else
        {
            bool exists = File.Exists(library.PathOf(entry));
            savedPhoto.Describe("", exists ? "正在加载预览" : "图片文件已移走", "");
            savedTitle.Text = entry.Wallpaper.Title;
            savedDetail.Text = $"{entry.Wallpaper.DisplayDate}    {entry.Width} × {entry.Height}\r\n{entry.Wallpaper.Copyright}";
            tip.SetToolTip(savedTitle, entry.FileName);
            tip.SetToolTip(savedDetail, entry.Wallpaper.Copyright);
            if (exists)
            {
                savedRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                _ = LoadPreviewAsync(savedPhoto, entry.Wallpaper, library.PathOf(entry), savedRequest.Token);
            }
        }
        UpdateActionStates();
    }

    private void OpenLibrary()
    {
        if (library == null)
            return;
        try
        {
            Directory.CreateDirectory(library.DirectoryPath);
            Process.Start(new ProcessStartInfo(library.DirectoryPath) { UseShellExecute = true });
        }
        catch (Exception error) when (OperationalError(error)) { Status(ErrorText(error), true); }
    }

    private void RevealSaved()
    {
        var entry = SelectedSaved;
        if (entry == null)
            return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{library.PathOf(entry)}\"") { UseShellExecute = true });
        }
        catch (Exception error) when (OperationalError(error)) { Status(ErrorText(error), true); }
    }

    private void DeleteSaved()
    {
        var entry = SelectedSaved;
        if (entry == null || changing)
            return;
        if (MessageBox.Show(this, $"删除本地图片和对应的壁纸记录？\n\n{entry.FileName}", "删除壁纸",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK)
            return;
        if (changing)
        {
            Status("正在换图，请稍后再删除。", true);
            return;
        }
        try
        {
            library.Remove(entry);
            Status("已删除  ·  " + entry.Wallpaper.Title);
        }
        catch (Exception error) when (OperationalError(error)) { Status(ErrorText(error), true); }
        finally { RenderLibrary(); }
    }

    private void BrowseForDirectory()
    {
        using (var dialog = new FolderBrowserDialog { Description = "选择 Bing 壁纸的保存目录", SelectedPath = folderPath.Text })
        {
            if (dialog.ShowDialog(this) == DialogResult.OK)
                folderPath.Text = dialog.SelectedPath;
        }
    }

    private void LoadSettingsFields()
    {
        folderPath.Text = settings.LibraryDirectory;
        proxyAddress.Text = settings.ProxyAddress;
        proxyMode.SelectedIndex = settings.ProxyMode == "custom" ? 2 : settings.ProxyMode == "direct" ? 1 : 0;
        interval.Value = settings.Interval;
        randomMinimum.Value = settings.RandomMinimum;
        randomMaximum.Value = settings.RandomMaximum;
        rotationMode.SelectedIndex = settings.RotationMode == "random" ? 2 : settings.RotationMode == "fixed" ? 1 : 0;
        UpdateRotationInputs();
    }

    private void UpdateRotationInputs()
    {
        bool fixedMode = rotationMode.SelectedIndex == 1;
        bool randomMode = rotationMode.SelectedIndex == 2;
        intervalLabel.Visible = interval.Visible = fixedMode;
        rangeLabel.Visible = randomMinimum.Visible = maxLabel.Visible = randomMaximum.Visible = randomMode;
    }

    private void SaveSettings()
    {
        if (changing || refreshing)
            return;
        try
        {
            string path = Environment.ExpandEnvironmentVariables(folderPath.Text.Trim());
            if (path.Length < 3 || !Path.IsPathRooted(path) || path[0] == '\\' && path[1] != '\\' ||
                path.Length >= 2 && path[1] == ':' && path[2] != '\\' && path[2] != '/')
                throw new ArgumentException("请输入完整的保存路径，例如 D:\\Bing Wallpapers。");

            var updated = new Settings
            {
                LibraryDirectory = Path.GetFullPath(path),
                ProxyMode = new[] { "system", "direct", "custom" }[proxyMode.SelectedIndex],
                ProxyAddress = proxyAddress.Text.Trim(),
                RotationMode = new[] { "off", "fixed", "random" }[rotationMode.SelectedIndex],
                Interval = (int)interval.Value,
                RandomMinimum = (int)randomMinimum.Value,
                RandomMaximum = (int)randomMaximum.Value,
                CurrentId = settings.CurrentId
            };
            ValidateSettings(updated);
            bool changeFolder = library == null || !string.Equals(updated.LibraryDirectory, settings.LibraryDirectory, StringComparison.OrdinalIgnoreCase);
            bool changeProxy = updated.ProxyMode != settings.ProxyMode ||
                (updated.ProxyMode == "custom" && updated.ProxyAddress != settings.ProxyAddress);
            bool changeRotation = updated.RotationMode != settings.RotationMode || updated.Interval != settings.Interval ||
                updated.RandomMinimum != settings.RandomMinimum || updated.RandomMaximum != settings.RandomMaximum;
            var newLibrary = changeFolder ? new WallpaperLibrary(updated.LibraryDirectory) : library;
            if (changeFolder)
                Directory.CreateDirectory(updated.LibraryDirectory);
            updated.Save();
            settings = updated;
            library = newLibrary;
            folderPath.Text = updated.LibraryDirectory;
            if (changeRotation)
                ScheduleRotation();
            if (changeFolder)
                RenderLibrary();
            if (changeProxy)
            {
                Cancel(ref cardRequests);
                Cancel(ref heroRequest);
                Cancel(ref savedRequest);
                bing.Dispose();
                bing = new BingClient(settings);
                needsExploreRefresh = true;
                _ = RefreshFeedAsync(true);
            }
            else
                Status("设置已保存");
            UpdateActionStates();
        }
        catch (Exception error) when (OperationalError(error))
        {
            Status("无法保存设置。 " + ErrorText(error), true);
        }
    }

    private static void ValidateSettings(Settings value)
    {
        if (string.IsNullOrWhiteSpace(value.LibraryDirectory) || value.LibraryDirectory.Length > 218)
            throw new ArgumentException("请选择较短的壁纸保存路径。");
        if (value.ProxyMode != "system" && value.ProxyMode != "direct" && value.ProxyMode != "custom")
            throw new ArgumentException("代理模式无效。");
        if (value.ProxyMode == "custom")
            BingClient.ParseProxy(value.ProxyAddress ?? "");
        if (value.RotationMode != "off" && value.RotationMode != "fixed" && value.RotationMode != "random")
            throw new ArgumentException("自动换图模式无效。");
        if (value.Interval < 1 || value.Interval > 10080 || value.RandomMinimum < 1 || value.RandomMinimum > 10080 ||
            value.RandomMaximum < 1 || value.RandomMaximum > 10080 ||
            (value.RotationMode == "random" && value.RandomMaximum < value.RandomMinimum))
            throw new ArgumentException("随机间隔的最长时间必须大于或等于最短时间。");
    }

    private void UpdateActionStates()
    {
        if (quitting)
            return;
        refresh.Enabled = !refreshing;
        saveSettings.Enabled = !refreshing && !changing;
        string selectedFile = selected == null ? null : LocalFile(selected);
        apply.Enabled = !changing && library != null && (selected?.Downloadable == true || selectedFile != null);
        apply.Text = changing ? "正在设置…" :
            selected?.Downloadable == false && selectedFile == null ? "此图不可下载" : "设为壁纸";
        var entry = SelectedSaved;
        bool exists = entry != null && library != null && File.Exists(library.PathOf(entry));
        applySaved.Enabled = !changing && (exists || entry?.Wallpaper.Downloadable == true);
        revealSaved.Enabled = exists;
        removeSaved.Enabled = !changing && entry != null;
        revealLibrary.Enabled = library != null;
        trayApply.Enabled = !changing && library != null;
    }

    private void Status(string message, bool error = false)
    {
        if (quitting)
            return;
        footer.Text = message;
        footer.ForeColor = error ? Theme.Alert : Theme.Muted;
        tip.SetToolTip(footer, message);
    }

    private static bool OperationalError(Exception error) =>
        error is IOException || error is UnauthorizedAccessException || error is ArgumentException ||
        error is HttpRequestException || error is OperationCanceledException || error is SerializationException ||
        error is Win32Exception || error is System.Runtime.InteropServices.ExternalException;

    private static string ErrorText(Exception error)
    {
        if (error is OperationCanceledException) return "请求超时或已取消。";
        if (error is HttpRequestException)
            return "请检查网络或代理。 " + (error.InnerException?.Message ?? error.Message);
        return error.Message;
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
            Cancel(ref cardRequests);
            Cancel(ref heroRequest);
            Cancel(ref savedRequest);
            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
            }
            rotation.Dispose();
            trayMenu?.Dispose();
            bing?.Dispose();
            lifetime.Dispose();
            tip.Dispose();
            logo?.Image?.Dispose();
        }
        base.Dispose(disposing);
    }
}
