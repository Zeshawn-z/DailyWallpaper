using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using UI = AntdUI;

namespace BingWallpapers;

internal sealed partial class MainForm
{
    private InkLabel settingsTitle, settingsSub, folderTitle, folderHelp, networkTitle, networkHelp, marketTitle, marketHelp;
    private FramePanel folderCard, networkCard, marketCard;
    private FrameButton browse, saveSettings, openLibrary;
    private UI.Input folderPath, proxyAddress;
    private UI.Select proxyMode, market;

    private void MakeSettings()
    {
        settingsTitle = LabelOn(settingsPage, "Settings", 42, false, Palette.Text, "Instrument Serif");
        settingsSub = LabelOn(settingsPage, "Manage your library location and connection to Bing.", 13, false, Palette.Muted);
        saveSettings = ButtonOn(settingsPage, "Save settings", "check", 16, ButtonLook.Primary, SavePreferences);
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
            Font = Palette.Font(), AccessibleName = name
        });
        control.Items.AddRange(labels);
        return control;
    }

    private void PlaceSettings(int w, int h)
    {
        int target = w - 48 - (h < 600 ? 14 : 0);
        int y = Logic(settingsPage.AutoScrollPosition.Y);
        Put(settingsTitle, 24, y + 21, target - 155, 50);
        Put(settingsSub, 24, y + 68, target - 155, 24);
        Put(saveSettings, target - 115, y + 31, 139, 40);
        Put(folderCard, 24, y + 104, target, 156);
        Put(folderTitle, 20, 13, target - 40, 38);
        Put(folderHelp, 20, 50, target - 40, 26);
        Put(folderPath, 17, 84, target - 151, 43);
        Put(browse, target - 125, 85, 105, 40);
        Put(openLibrary, target - 207, 15, 187, 32);
        Put(networkCard, 24, y + 276, target, 145);
        Put(networkTitle, 20, 13, target - 40, 38);
        Put(networkHelp, 20, 50, target - 40, 26);
        Put(proxyMode, 17, 84, 218, 42);
        Put(proxyAddress, 244, 84, target - 263, 42);
        Put(marketCard, 24, y + 437, target, 145);
        Put(marketTitle, 20, 13, target - 40, 38);
        Put(marketHelp, 20, 50, target - 40, 26);
        Put(market, 17, 84, 218, 42);
        settingsPage.AutoScrollMinSize = new Size(0, S(607));
    }

    private void LoadOptions()
    {
        loadingControls = true;
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
            Description = "Choose Dayframe’s wallpaper folder", SelectedPath = folderPath.Text
        })
            if (dialog.ShowDialog(this) == DialogResult.OK)
                folderPath.Text = dialog.SelectedPath;
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
                throw new ArgumentException("Enter an absolute folder path, for example D:\\Bing Wallpapers.");
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
            RenderLibrary();
            ResetSchedule();
            Status("Settings saved");
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
            throw new ArgumentException("Choose a shorter absolute path for the library.");
        if (!new[] { "system", "direct", "custom" }.Contains(value.ProxyMode))
            throw new ArgumentException("Invalid proxy mode.");
        if (value.ProxyMode == "custom")
            BingClient.ParseProxy(value.ProxyAddress ?? "");
        if (!new[] { "fixed", "random", "daily" }.Contains(value.RotationMode) ||
            !new[] { "library", "favorites", "bing" }.Contains(value.Pool) ||
            !new[] { "en-US", "zh-CN" }.Contains(value.Market) ||
            !new[] { "Fill", "Fit", "Stretch", "Center", "Tile" }.Contains(value.Fit))
            throw new ArgumentException("Invalid Dayframe settings.");
        if (value.Interval < 1 || value.Interval > 10080 || value.RandomMinimum < 1 ||
            value.RandomMaximum < value.RandomMinimum || value.RandomMaximum > 10080)
            throw new ArgumentException("Use a valid interval and keep the shortest time at or below the longest.");
    }
}
