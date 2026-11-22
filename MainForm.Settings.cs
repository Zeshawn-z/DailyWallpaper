using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using UI = AntdUI;

namespace BingWallpapers;

internal sealed partial class MainForm
{
    private InkLabel settingsTitle, settingsSub, appearanceTitle, languageLabel, themeLabel, versionLabel,
        folderTitle, folderHelp, networkTitle, networkHelp, marketTitle, marketHelp;
    private FramePanel appearanceCard, folderCard, networkCard, marketCard;
    private FrameButton browse, saveSettings, openLibrary;
    private UI.Input folderPath, proxyAddress;
    private UI.Select language, theme, proxyMode, market;

    private void MakeSettings()
    {
        settingsTitle = LabelOn(settingsPage, "Settings", 42, false, Palette.Text, "Instrument Serif");
        settingsSub = LabelOn(settingsPage, "Manage your library location and connection to Bing.", 13, false, Palette.Muted);
        saveSettings = ButtonOn(settingsPage, "Save settings", "check", 16, ButtonLook.Primary, SavePreferences);
        versionLabel = LabelOn(settingsPage, "Dayframe " + Program.Version, 12, false, Palette.Muted);
        versionLabel.Align = StringAlignment.Far;
        appearanceCard = PanelOn(settingsPage, Palette.Surface, 17, true);
        appearanceTitle = LabelOn(appearanceCard, "Appearance", 22, false, Palette.Text, "Instrument Serif");
        languageLabel = LabelOn(appearanceCard, "Language", 13, false, Palette.Subtle);
        themeLabel = LabelOn(appearanceCard, "Theme", 13, false, Palette.Subtle);
        language = Choices(appearanceCard, "Language", "English", "简体中文");
        language.Font = Palette.Font(14, false, "Microsoft YaHei UI");
        theme = Choices(appearanceCard, "Theme", "Dark", "Light");
        language.SelectedIndexChanged += (_, __) => QueueAppearanceChange();
        theme.SelectedIndexChanged += (_, __) => QueueAppearanceChange();

        folderCard = PanelOn(settingsPage, Palette.Surface, 17, true);
        folderTitle = LabelOn(folderCard, "Library location", 22, false, Palette.Text, "Instrument Serif");
        folderHelp = LabelOn(folderCard, "New wallpapers and wallpapers.json are saved side by side.", 13, false, Palette.Subtle);
        folderPath = InputOn(folderCard, "Wallpaper directory");
        browse = ButtonOn(folderCard, "Browse…", "folder", 16, ButtonLook.Soft, BrowseDirectory);
        openLibrary = ButtonOn(folderCard, "Open current folder", "external", 16, ButtonLook.Plain, OpenLibrary);

        networkCard = PanelOn(settingsPage, Palette.Surface, 17, true);
        networkTitle = LabelOn(networkCard, "Network proxy", 22, false, Palette.Text, "Instrument Serif");
        networkHelp = LabelOn(networkCard, "Used for Bing lists, previews and original downloads.", 13, false, Palette.Subtle);
        proxyMode = Choices(networkCard, "Proxy mode", "Follow system", "Direct connection", "Custom HTTP proxy");
        proxyAddress = InputOn(networkCard, "HTTP proxy address");
        proxyAddress.PlaceholderText = "127.0.0.1:7890";
        proxyMode.SelectedIndexChanged += (_, __) => proxyAddress.Enabled = proxyMode.SelectedIndex == 2;

        marketCard = PanelOn(settingsPage, Palette.Surface, 17, true);
        marketTitle = LabelOn(marketCard, "Bing market", 22, false, Palette.Text, "Instrument Serif");
        marketHelp = LabelOn(marketCard, "Controls which daily images appear in Today.", 13, false, Palette.Subtle);
        market = Choices(marketCard, "Bing market", "United States", "China");
    }

    private static UI.Select Choices(Control parent, string name, params string[] labels)
    {
        var control = Add(parent, new UI.Select
        {
            List = true, Radius = 10, BackColor = Palette.Canvas, BorderColor = Palette.Line,
            Font = Palette.Font(), AccessibleName = Strings.Get(name)
        });
        control.Items.AddRange(labels.Select(Strings.Get).ToArray());
        return control;
    }

    private void PlaceSettings(int w, int h)
    {
        int target = w - 48 - (h < 742 ? 14 : 0);
        int y = Logic(settingsPage.AutoScrollPosition.Y);
        Put(settingsTitle, 24, y + 21, target - 155, 50);
        Put(settingsSub, 24, y + 68, target - 155, 24);
        Put(saveSettings, target - 115, y + 31, 139, 40);
        Put(versionLabel, target - 156, y + 73, 180, 20);
        Put(appearanceCard, 24, y + 104, target, 135);
        int column = (target - 60) / 2;
        Put(appearanceTitle, 20, 12, target - 40, 37);
        Put(languageLabel, 20, 49, column, 23);
        Put(themeLabel, 40 + column, 49, column, 23);
        Put(language, 17, 74, column + 4, 42);
        Put(theme, 37 + column, 74, column + 4, 42);
        Put(folderCard, 24, y + 255, target, 156);
        Put(folderTitle, 20, 13, target - 40, 38);
        Put(folderHelp, 20, 50, target - 40, 26);
        Put(folderPath, 17, 84, target - 151, 43);
        Put(browse, target - 125, 85, 105, 40);
        Put(openLibrary, target - 207, 15, 187, 32);
        Put(networkCard, 24, y + 427, target, 145);
        Put(networkTitle, 20, 13, target - 40, 38);
        Put(networkHelp, 20, 50, target - 40, 26);
        Put(proxyMode, 17, 84, 218, 42);
        Put(proxyAddress, 244, 84, target - 263, 42);
        Put(marketCard, 24, y + 588, target, 145);
        Put(marketTitle, 20, 13, target - 40, 38);
        Put(marketHelp, 20, 50, target - 40, 26);
        Put(market, 17, 84, 218, 42);
        settingsPage.AutoScrollMinSize = new Size(0, S(756));
    }

    private void LoadOptions()
    {
        loadingControls = true;
        language.SelectedIndex = settings.Language == "zh-CN" ? 1 : 0;
        theme.SelectedIndex = settings.Theme == "light" ? 1 : 0;
        folderPath.Text = settings.LibraryDirectory;
        proxyAddress.Text = settings.ProxyAddress ?? "";
        proxyMode.SelectedIndex = settings.ProxyMode == "custom" ? 2 : settings.ProxyMode == "direct" ? 1 : 0;
        market.SelectedIndex = settings.Market == "zh-CN" ? 1 : 0;
        interval.Value = settings.Interval;
        minInterval.Value = settings.RandomMinimum;
        maxInterval.Value = settings.RandomMaximum;
        master.Checked = settings.RotationEnabled == true;
        avoid.Checked = settings.AvoidRecent == true;
        keepTray.Checked = settings.KeepInTray == true;
        onBattery.Checked = settings.PauseOnBattery;
        startWindows.Checked = StartWithWindows.Enabled();
        loadingControls = false;
        ConfigureRotationUi();
        UpdateDesktopCard();
    }

    private void BrowseDirectory()
    {
        using (var dialog = new FolderBrowserDialog
        {
            Description = Strings.Get("Choose Dayframe’s wallpaper folder"), SelectedPath = folderPath.Text
        })
            if (dialog.ShowDialog(this) == DialogResult.OK)
                folderPath.Text = dialog.SelectedPath;
    }

    private void QueueAppearanceChange()
    {
        if (ready && !loadingControls && !quitting)
            BeginInvoke(new Action(ApplyAppearance));
    }

    private void ApplyAppearance()
    {
        if (!ready || quitting || language.SelectedIndex < 0 || theme.SelectedIndex < 0)
            return;
        string newLanguage = language.SelectedIndex == 1 ? "zh-CN" : "en-US";
        string newTheme = theme.SelectedIndex == 1 ? "light" : "dark";
        if (newLanguage == settings.Language && newTheme == settings.Theme)
            return;

        var updated = settings.Copy();
        updated.Language = newLanguage;
        updated.Theme = newTheme;
        try { updated.Save(); }
        catch (Exception error) when (Operational(error))
        {
            loadingControls = true;
            language.SelectedIndex = settings.Language == "zh-CN" ? 1 : 0;
            theme.SelectedIndex = settings.Theme == "light" ? 1 : 0;
            loadingControls = false;
            Report(error);
            return;
        }

        // The controls already contain any unsaved values. Keep them when changing only colors.
        if (newLanguage == settings.Language)
        {
            settings = updated;
            ApplyTheme(newTheme);
            return;
        }

        // Preserve draft form values when rebuilding the labels in another language.
        string draftPath = folderPath.Text, draftProxy = proxyAddress.Text, query = search.Text;
        int draftProxyMode = proxyMode.SelectedIndex, draftMarket = market.SelectedIndex;
        int scroll = -settingsPage.AutoScrollPosition.Y;
        bool focusLanguage = language.Focused, focusTheme = theme.Focused;
        settings = updated;
        ready = false;
        SuspendLayout();
        try
        {
            Cancel(ref heroRequest); Cancel(ref recentRequest);
            Cancel(ref inspectorRequest); Cancel(ref desktopRequest); Cancel(ref queueRequest); Cancel(ref historyRequest);
            tip.RemoveAll();
            while (Controls.Count != 0)
                Controls[0].Dispose();
            Strings.SetLanguage(newLanguage);
            Palette.SetTheme(newTheme);
            BuildUi();
            search.Text = query;
            ready = true;
            LoadOptions();
            folderPath.Text = draftPath;
            proxyMode.SelectedIndex = draftProxyMode;
            proxyAddress.Text = draftProxy;
            market.SelectedIndex = draftMarket;
            settingsPage.AutoScrollPosition = new Point(0, scroll);
            RebuildTrayMenu();
            RefreshTodayLabels();
            UpdateQueuePreview();
            PaintRotationStatus();
            if (focusLanguage) language.Focus();
            if (focusTheme) theme.Focus();
        }
        finally { ResumeLayout(true); }
        Invalidate(true);
    }

    private void ApplyTheme(string newTheme)
    {
        var previous = Palette.ThemeColors();
        Palette.SetTheme(newTheme);
        var colors = previous.Zip(Palette.ThemeColors(), (old, current) => (old, current))
            .ToDictionary(pair => pair.old, pair => pair.current);
        Color Recolor(Color color) => colors.TryGetValue(color, out var current) ? current : color;
        Color? RecolorOptional(Color? color) => color.HasValue ? Recolor(color.Value) : (Color?)null;

        void RecolorControls(Control control)
        {
            bool overPhoto = control.Parent == hero;
            if (control is UI.Input input)
            {
                input.BackColor = RecolorOptional(input.BackColor);
                input.ForeColor = RecolorOptional(input.ForeColor);
                input.BorderColor = RecolorOptional(input.BorderColor);
            }
            else
            {
                if (colors.TryGetValue(control.BackColor, out var background))
                    control.BackColor = background;
                if (!overPhoto && colors.TryGetValue(control.ForeColor, out var foreground))
                    control.ForeColor = foreground;
            }
            if (control is FramePanel panel)
            {
                panel.Fill = Recolor(panel.Fill);
                panel.Border = RecolorOptional(panel.Border);
            }
            else if (control is FrameButton button)
            {
                if (!overPhoto)
                    button.GlyphColor = RecolorOptional(button.GlyphColor);
                button.SoftFill = RecolorOptional(button.SoftFill);
            }
            foreach (Control child in control.Controls)
                RecolorControls(child);
        }

        RecolorControls(this);
        BorderColor = Palette.Line;
        Dark = !Palette.IsLight;
        searchIcon.Image = Palette.Icon("search", 18, Palette.Muted);
        Invalidate(true);
    }

    private void SavePreferences()
    {
        if (changing || refreshing)
            return;
        Attempt(() =>
        {
            string path = Environment.ExpandEnvironmentVariables(folderPath.Text.Trim());
            if (path.Length < 3 || !Path.IsPathRooted(path) || path[0] == '\\' && path[1] != '\\' ||
                path[1] == ':' && path[2] != '\\' && path[2] != '/')
                throw new ArgumentException(Strings.Get("Enter an absolute folder path, for example D:\\Bing Wallpapers."));
            var updated = settings.Copy();
            updated.LibraryDirectory = Path.GetFullPath(path);
            updated.Market = market.SelectedIndex == 1 ? "zh-CN" : "en-US";
            updated.ProxyMode = new[] { "system", "direct", "custom" }[proxyMode.SelectedIndex];
            updated.ProxyAddress = proxyAddress.Text.Trim();
            Validate(updated);
            bool changeFolder = library == null || !string.Equals(updated.LibraryDirectory, library.DirectoryPath, StringComparison.OrdinalIgnoreCase);
            bool changeMarket = updated.Market != settings.Market;
            bool changeProxy = updated.ProxyMode != settings.ProxyMode || updated.ProxyAddress != settings.ProxyAddress;
            var opened = changeFolder ? new WallpaperLibrary(updated.LibraryDirectory) : library;
            if (changeFolder) Directory.CreateDirectory(updated.LibraryDirectory);
            updated.Save();
            settings = updated;
            library = opened;
            if (changeFolder)
                savedEntry = null;
            if (changeProxy || changeMarket)
            {
                Cancel(ref heroRequest); ClearRecentCards(); Cancel(ref historyRequest);
                Cancel(ref desktopRequest); Cancel(ref queueRequest);
                bing.Dispose();
                bing = new BingClient(settings);
                recent = GetCached();
                selected = null;
                _ = RefreshAsync(true);
            }
            LoadOptions();
            ResetSchedule();
        });
    }

    private void CommitSchedule()
    {
        if (!ready || loadingControls || quitting)
            return;
        int fixedValue = (int)interval.Value, low = (int)minInterval.Value, high = (int)maxInterval.Value;
        if (settings.RotationMode == scheduleMode && settings.Interval == fixedValue &&
            settings.RandomMinimum == low && settings.RandomMaximum == high)
            return;
        UpdateSettings(() =>
        {
            settings.RotationMode = scheduleMode;
            settings.Interval = fixedValue;
            settings.RandomMinimum = low;
            settings.RandomMaximum = high;
        }, true);
    }

    private void UpdateSettings(Action change, bool reschedule)
    {
        if (quitting) return;
        var old = settings.Copy();
        try
        {
            change();
            Validate(settings);
            settings.Save();
        }
        catch (Exception error) when (Operational(error))
        {
            settings = old;
            LoadOptions();
            Report(error);
            return;
        }
        loadingControls = true;
        master.Checked = settings.RotationEnabled == true;
        loadingControls = false;
        ConfigureRotationUi();
        if (reschedule) ResetSchedule();
        else { queued = null; PickQueue(); }
        PaintRotationStatus();
    }

    private static void Validate(Settings value)
    {
        if (string.IsNullOrWhiteSpace(value.LibraryDirectory) || !Path.IsPathRooted(value.LibraryDirectory) ||
            value.LibraryDirectory.Length > 218)
            throw new ArgumentException(Strings.Get("Choose a shorter absolute path for the library."));
        if (!new[] { "system", "direct", "custom" }.Contains(value.ProxyMode))
            throw new ArgumentException(Strings.Get("Invalid proxy mode."));
        if (value.ProxyMode == "custom")
            BingClient.ParseProxy(value.ProxyAddress ?? "");
        if (!new[] { "fixed", "random", "daily" }.Contains(value.RotationMode) ||
            !new[] { "library", "favorites", "bing" }.Contains(value.Pool) ||
            !new[] { "en-US", "zh-CN" }.Contains(value.Market) ||
            !new[] { "Fill", "Fit", "Stretch", "Center", "Tile" }.Contains(value.Fit))
            throw new ArgumentException(Strings.Get("Invalid Dayframe settings."));
        if (value.Interval < 1 || value.Interval > 10080 || value.RandomMinimum < 1 ||
            value.RandomMaximum < value.RandomMinimum || value.RandomMaximum > 10080)
            throw new ArgumentException(Strings.Get("Use a valid interval and keep the shortest time at or below the longest."));
    }
}
