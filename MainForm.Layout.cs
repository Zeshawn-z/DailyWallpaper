using System;
using System.Drawing;
using System.Windows.Forms;
using UI = AntdUI;

namespace BingWallpapers;

internal sealed partial class MainForm
{
    private Panel rail, explore, collection, preferences, archiveCard;
    private UI.PageHeader chrome;
    private UI.Menu navigation;
    private Label explorerTitle, explorerSub, recentTitle, recentCount, footer, schedule,
        libraryTitle, librarySub, savedTitle, savedDetail, settingsTitle, settingsSub,
        folderTitle, folderHelp, proxyTitle, proxyHelp, rotationTitle, rotationHelp, intervalLabel, rangeLabel, maxLabel;
    private UI.Panel folderCard, proxyCard, rotationCard, savedCard, libraryEmpty, scheduleCard;
    private UI.Button toTray, refresh, apply, revealLibrary, applySaved, revealSaved, removeSaved, browse, saveSettings;
    private PhotoPanel hero, savedPhoto;
    private TableLayoutPanel recentCards;
    private UI.Table savedGrid;
    private UI.Input search, folderPath, proxyAddress;
    private UI.Select proxyMode;
    private UI.Segmented rotationMode;
    private UI.InputNumber interval, randomMinimum, randomMaximum;
    private PictureBox logo;
    private Label product, productSub, scheduleTitle, emptyTitle, emptyHelp;
    private UI.Tag savedSize;
    private Panel railEdge, statusDot;
    private readonly ToolTip tip = new ToolTip { AutomaticDelay = 450, AutoPopDelay = 8000 };
    private float scale;
    private string page = "explore";
    private int S(int value) => (int)Math.Round(value * scale);

    private void BuildUi()
    {
        Palette.Configure();
        AutoScaleMode = AutoScaleMode.None;
        using (var canvas = CreateGraphics())
            scale = canvas.DpiX / 96f;
        Text = "Bing Wallpapers · 每日壁纸";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        Font = Palette.Font(10);
        BackColor = Palette.Canvas;
        ForeColor = Palette.Ink;
        BorderColor = Palette.Line;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        var workArea = Screen.PrimaryScreen.WorkingArea;
        MinimumSize = new Size(Math.Min(S(890), workArea.Width), Math.Min(S(650), workArea.Height));
        Size = new Size(Math.Min(S(1230), workArea.Width), Math.Min(S(822), workArea.Height));

        chrome = new UI.PageHeader
        {
            ShowButton = true, EnableButtonTooltip = true, EnableDoubleClickMaximize = true,
            BackColor = Palette.Canvas, ForeColor = Palette.Ink, UseForeColorDrawIcons = true,
            UseSystemStyleColor = false
        };
        Controls.Add(chrome);
        rail = AddPanel(this, Palette.Rail);
        railEdge = AddPanel(this, Palette.Line);
        logo = new PictureBox { Image = Icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Palette.Rail };
        rail.Controls.Add(logo);
        product = AddLabel(rail, "Bing Wallpapers", 11.5f, true);
        product.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
        productSub = AddLabel(rail, "每日壁纸", 9, false, Palette.Muted);
        navigation = new UI.Menu
        {
            AccessibleName = "主导航", BackColor = Palette.Rail, BackActive = Palette.Tint,
            BackHover = Palette.Hover, ForeColor = Palette.Muted, ForeActive = Palette.Accent,
            Radius = 9, Gap = 9, IconRatio = .98f, FocusMode = UI.TFocusMode.Line,
            FocusModeAlign = UI.TAlignMini.Left, FocusModeColor = Palette.Accent
        };
        navigation.Items.Add(new UI.MenuItem { ID = "explore", Text = "探索壁纸", IconSvg = "PictureOutlined", Select = true });
        navigation.Items.Add(new UI.MenuItem { ID = "library", Text = "本地收藏", IconSvg = "FolderOutlined" });
        navigation.Items.Add(new UI.MenuItem { ID = "settings", Text = "设置", IconSvg = "SettingOutlined" });
        navigation.SelectChanged += (_, e) => ShowPage(e.Value.ID);
        rail.Controls.Add(navigation);
        scheduleCard = CardOn(rail);
        scheduleTitle = AddLabel(scheduleCard, "自动换图", 9.5f, true);
        schedule = AddLabel(scheduleCard, "", 9, false, Palette.Muted);
        toTray = ButtonOn(rail, "转到托盘", "DownloadOutlined", (_, __) => HideToTray());
        toTray.Ghost = true;
        toTray.BorderWidth = 0;
        toTray.ForeColor = Palette.Muted;

        explore = AddPanel(this, Palette.Canvas);
        explorerTitle = AddLabel(explore, "探索壁纸", 28, true);
        explorerSub = AddLabel(explore, "Bing 中国区每日图片", 10, false, Palette.Muted);
        refresh = ButtonOn(explore, "刷新", "ReloadOutlined", async (_, __) => await RefreshFeedAsync(true));
        hero = new PhotoPanel(true) { S = S };
        explore.Controls.Add(hero);
        apply = ButtonOn(explore, "设为壁纸", "DesktopOutlined", async (_, __) => await ChangeWallpaperAsync(selected), true);
        archiveCard = AddPanel(explore, Palette.Canvas);
        recentTitle = AddLabel(archiveCard, "最近发布", 13, true);
        recentCount = AddLabel(archiveCard, "", 9, false, Palette.Muted);
        recentCount.TextAlign = ContentAlignment.MiddleRight;
        recentCards = new TableLayoutPanel { RowCount = 1, BackColor = Palette.Canvas, Margin = Padding.Empty };
        recentCards.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        archiveCard.Controls.Add(recentCards);

        collection = AddPanel(this, Palette.Canvas);
        libraryTitle = AddLabel(collection, "本地收藏", 28, true);
        librarySub = AddLabel(collection, "", 10, false, Palette.Muted);
        revealLibrary = ButtonOn(collection, "打开目录", "FolderOpenOutlined", (_, __) => OpenLibrary());
        search = InputOn(collection, "搜索标题或日期", "搜索标题或日期", "SearchOutlined");
        search.AllowClear = true;
        search.TextChanged += (_, __) => RenderLibrary();
        savedGrid = new UI.Table
        {
            AccessibleName = "已保存的壁纸", Back = Palette.Surface, ForeColor = Palette.Ink,
            ColumnBack = Palette.Field, ColumnFore = Palette.Muted, BorderColor = Palette.Line,
            RowHeight = 47, RowHeightHeader = 40, RowHoverBg = Palette.Hover, RowSelectedBg = Palette.Tint,
            RowSelectedFore = Palette.Accent, Radius = 13, BorderWidth = 1, MultipleRows = false
        };
        savedGrid.Columns = new UI.ColumnCollection
        {
            new UI.Column(nameof(LibraryRow.Title), "壁纸").SetWidthFill().SetEllipsis(),
            new UI.Column(nameof(LibraryRow.Date), "发布日期").SetWidth(112)
        };
        savedGrid.SelectIndexChanged += (_, __) => UpdateSavedPreview();
        collection.Controls.Add(savedGrid);
        libraryEmpty = CardOn(collection);
        emptyTitle = AddLabel(libraryEmpty, "这里还没有壁纸", 14, true);
        emptyTitle.TextAlign = ContentAlignment.MiddleCenter;
        emptyHelp = AddLabel(libraryEmpty, "设为壁纸后，图片会出现在这里。", 9, false, Palette.Muted);
        emptyHelp.TextAlign = ContentAlignment.MiddleCenter;
        savedCard = CardOn(collection);
        savedPhoto = new PhotoPanel(false) { S = S, BackColor = Palette.Surface };
        savedCard.Controls.Add(savedPhoto);
        savedTitle = AddLabel(savedCard, "", 15, true);
        savedSize = new UI.Tag
        {
            Radius = 6, BackColor = Palette.Tint, ForeColor = Palette.Accent, BorderWidth = 0, Font = Palette.Font(8.5f)
        };
        savedCard.Controls.Add(savedSize);
        savedDetail = AddLabel(savedCard, "", 9, false, Palette.Muted);
        savedDetail.TextAlign = ContentAlignment.TopLeft;
        applySaved = ButtonOn(savedCard, "设为壁纸", "DesktopOutlined", async (_, __) => await ChangeWallpaperAsync(SelectedSaved?.Wallpaper), true);
        revealSaved = ButtonOn(savedCard, "定位文件", "FolderOpenOutlined", (_, __) => RevealSaved());
        removeSaved = ButtonOn(savedCard, "删除", null, (_, __) => DeleteSaved());
        removeSaved.ForeColor = Palette.Alert;

        preferences = AddPanel(this, Palette.Canvas);
        settingsTitle = AddLabel(preferences, "设置", 28, true);
        settingsSub = AddLabel(preferences, "保存位置、网络连接与后台换图", 10, false, Palette.Muted);
        saveSettings = ButtonOn(preferences, "保存设置", "CheckOutlined", (_, __) => SaveSettings(), true);
        folderCard = CardOn(preferences);
        folderTitle = AddLabel(folderCard, "保存目录", 14, true);
        folderHelp = AddLabel(folderCard, "切换目录后，原文件仍在原目录。", 9, false, Palette.Muted);
        folderPath = InputOn(folderCard, "壁纸保存目录", "选择壁纸保存目录", "FolderOutlined");
        browse = ButtonOn(folderCard, "浏览…", null, (_, __) => BrowseForDirectory());
        proxyCard = CardOn(preferences);
        proxyTitle = AddLabel(proxyCard, "网络代理", 14, true);
        proxyHelp = AddLabel(proxyCard, "应用于 Bing 列表、预览和原图下载。", 9, false, Palette.Muted);
        proxyMode = new UI.Select
        {
            AccessibleName = "代理模式", List = true, Radius = 8, BackColor = Palette.Field, BorderColor = Palette.Line
        };
        proxyMode.Items.AddRange(new[] { "跟随系统", "直接连接", "自定义 HTTP 代理" });
        proxyCard.Controls.Add(proxyMode);
        proxyAddress = InputOn(proxyCard, "HTTP 代理地址", "例如 127.0.0.1:7890", "LinkOutlined");
        proxyMode.SelectedIndexChanged += (_, __) => proxyAddress.Enabled = proxyMode.SelectedIndex == 2;
        rotationCard = CardOn(preferences);
        rotationTitle = AddLabel(rotationCard, "自动换图", 14, true);
        rotationHelp = AddLabel(rotationCard, "", 9, false, Palette.Muted);
        rotationMode = new UI.Segmented
        {
            AccessibleName = "自动换图模式", Full = true, Radius = 8,
            BackColor = Palette.Field, BackActive = Palette.Tint, BackHover = Palette.Hover,
            ForeColor = Palette.Muted, ForeActive = Palette.Accent, ForeHover = Palette.Ink,
            Padding = new Padding(S(3))
        };
        rotationMode.Items.Add(new UI.SegmentedItem { Text = "关闭" });
        rotationMode.Items.Add(new UI.SegmentedItem { Text = "固定间隔" });
        rotationMode.Items.Add(new UI.SegmentedItem { Text = "随机换图" });
        rotationMode.SelectIndexChanged += (_, __) => UpdateRotationInputs();
        rotationCard.Controls.Add(rotationMode);
        intervalLabel = AddLabel(rotationCard, "固定间隔（分钟）", 9, false, Palette.Muted);
        interval = MinutesOn(rotationCard, "固定间隔分钟数");
        rangeLabel = AddLabel(rotationCard, "随机间隔（分钟）", 9, false, Palette.Muted);
        randomMinimum = MinutesOn(rotationCard, "随机间隔最短分钟数");
        maxLabel = AddLabel(rotationCard, "至", 9, false, Palette.Muted);
        randomMaximum = MinutesOn(rotationCard, "随机间隔最长分钟数");

        statusDot = AddPanel(this, Palette.Accent);
        footer = AddLabel(this, "", 8.5f, false, Palette.Muted);
        Resize += (_, __) => PlaceUi();
        PlaceUi();
        ShowPage("explore");
    }

    private void PlaceUi()
    {
        if (ClientSize.Width == 0 || ClientSize.Height == 0 || footer == null)
            return;

        int railWidth = S(211);
        int gutter = S(28);
        int top = S(34);
        int w = ClientSize.Width - railWidth - 2 * gutter;
        int h = ClientSize.Height - top - S(37);
        if (w < S(380) || h < S(410))
            return;

        chrome.SetBounds(railWidth, 0, ClientSize.Width - railWidth, S(34));
        rail.SetBounds(0, 0, railWidth - 1, ClientSize.Height);
        railEdge.SetBounds(railWidth - 1, 0, 1, ClientSize.Height);
        logo.SetBounds(S(19), S(26), S(42), S(42));
        product.SetBounds(S(69), S(25), railWidth - S(73), S(24));
        productSub.SetBounds(S(70), S(49), railWidth - S(74), S(20));
        navigation.SetBounds(S(14), S(105), railWidth - S(28), S(168));
        scheduleCard.SetBounds(S(16), ClientSize.Height - S(137), railWidth - S(32), S(69));
        scheduleTitle.SetBounds(S(13), S(11), scheduleCard.Width - S(24), S(22));
        schedule.SetBounds(S(13), S(36), scheduleCard.Width - S(24), S(21));
        toTray.SetBounds(S(16), ClientSize.Height - S(55), railWidth - S(32), S(39));
        foreach (var view in new[] { explore, collection, preferences })
            view.SetBounds(railWidth + gutter, top, w, h);
        statusDot.SetBounds(railWidth + gutter + S(1), ClientSize.Height - S(22), S(5), S(5));
        footer.SetBounds(railWidth + gutter + S(14), ClientSize.Height - S(33), w - S(14), S(27));

        PlaceHeading(explorerTitle, explorerSub, refresh, w - S(149), S(104));
        apply.SetBounds(w - S(140), S(13), S(143), S(44));
        int heroHeight = h - S(239);
        hero.SetBounds(0, S(92), w, heroHeight);


        archiveCard.SetBounds(0, hero.Bottom + S(15), w, S(133));
        recentTitle.SetBounds(0, 0, S(158), S(28));
        recentCount.SetBounds(w - S(96), 0, S(96), S(28));
        recentCards.SetBounds(0, S(34), w, S(97));

        PlaceHeading(libraryTitle, librarySub, revealLibrary, w, S(126));
        int gridWidth = (int)(w * .60f);
        search.SetBounds(0, S(90), gridWidth, S(42));
        savedGrid.SetBounds(0, S(145), gridWidth, h - S(145));
        libraryEmpty.SetBounds(0, S(145), w, h - S(145));
        emptyTitle.SetBounds(0, libraryEmpty.Height / 2 - S(32), w, S(30));
        emptyHelp.SetBounds(S(8), libraryEmpty.Height / 2 + S(2), w - S(16), S(24));
        int detailWidth = w - gridWidth - S(16);
        savedCard.SetBounds(gridWidth + S(16), S(90), detailWidth, h - S(90));
        int inner = detailWidth - S(32);
        int imageHeight = Math.Min(Math.Min(S(232), inner * 9 / 16), h - S(330));
        savedPhoto.SetBounds(S(16), S(16), inner, imageHeight);
        int infoTop = savedPhoto.Bottom + S(9);
        savedTitle.SetBounds(S(16), infoTop, inner, S(43));
        savedSize.SetBounds(S(16), infoTop + S(49), S(114), S(29));
        savedDetail.SetBounds(S(17), infoTop + S(88), inner - S(1), S(48));
        int buttonTop = savedCard.Height - S(113);
        applySaved.SetBounds(S(14), buttonTop, inner + S(4), S(46));
        revealSaved.SetBounds(S(14), buttonTop + S(48), inner * 2 / 3, S(40));
        removeSaved.SetBounds(revealSaved.Right + S(5), revealSaved.Top, inner - revealSaved.Width, S(40));

        PlaceHeading(settingsTitle, settingsSub, saveSettings, w, S(126));
        int settingWidth = (w - S(16)) / 2;
        folderCard.SetBounds(0, S(92), settingWidth, S(193));
        folderTitle.SetBounds(S(18), S(14), settingWidth - S(36), S(32));
        folderHelp.SetBounds(S(18), S(50), settingWidth - S(36), S(29));
        folderPath.SetBounds(S(15), S(89), settingWidth - S(30), S(43));
        browse.SetBounds(settingWidth - S(103), S(137), S(89), S(40));
        proxyCard.SetBounds(settingWidth + S(16), S(92), w - settingWidth - S(16), S(193));
        proxyTitle.SetBounds(S(18), S(14), proxyCard.Width - S(36), S(32));
        proxyHelp.SetBounds(S(18), S(50), proxyCard.Width - S(36), S(29));
        proxyMode.SetBounds(S(15), S(89), proxyCard.Width - S(30), S(43));
        proxyAddress.SetBounds(S(15), S(135), proxyCard.Width - S(30), S(43));
        rotationCard.SetBounds(0, S(301), w, S(221));
        rotationTitle.SetBounds(S(18), S(13), w - S(36), S(32));
        rotationHelp.SetBounds(S(18), S(48), w - S(36), S(29));
        rotationMode.SetBounds(S(17), S(82), Math.Min(w - S(34), S(426)), S(42));
        intervalLabel.SetBounds(S(19), S(149), S(134), S(31));
        interval.SetBounds(S(155), S(143), S(101), S(43));
        rangeLabel.Bounds = intervalLabel.Bounds;
        randomMinimum.Bounds = interval.Bounds;
        maxLabel.SetBounds(S(262), S(149), S(21), S(31));
        randomMaximum.SetBounds(S(285), S(143), S(101), S(43));
    }

    private void PlaceHeading(Label title, Label subtitle, UI.Button action, int width, int buttonWidth)
    {
        title.SetBounds(0, 0, width - buttonWidth - S(10), S(53));
        subtitle.SetBounds(S(2), S(57), width - buttonWidth - S(12), S(24));
        action.SetBounds(width - buttonWidth + S(3), S(13), buttonWidth, S(44));
    }

    private static Panel AddPanel(Control parent, Color color)
    {
        var panel = new Panel { BackColor = color };
        parent.Controls.Add(panel);
        return panel;
    }

    private static UI.Panel CardOn(Control parent)
    {
        var card = new UI.Panel { Back = Palette.Surface, Radius = 13, BorderWidth = 1, BorderColor = Palette.Line };
        parent.Controls.Add(card);
        return card;
    }

    private static Label AddLabel(Control parent, string text, float size, bool bold, Color? color = null)
    {
        var label = new Label
        {
            Text = text, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
            Font = Palette.Font(size, bold ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = color ?? Palette.Ink, BackColor = Color.Transparent, UseMnemonic = false
        };
        parent.Controls.Add(label);
        return label;
    }

    private static UI.Button ButtonOn(Control parent, string text, string icon, EventHandler click, bool primary = false)
    {
        var button = new UI.Button
        {
            Text = text, AccessibleName = text, IconSvg = icon, IconRatio = .84f,
            Radius = 9, WaveSize = 4, Cursor = Cursors.Hand, BorderWidth = primary ? 0 : 1,
            Type = primary ? UI.TTypeMini.Primary : UI.TTypeMini.Default,
            BackColor = primary ? Palette.Accent : null, DefaultBack = Palette.Surface, DefaultBorderColor = Palette.Line,
            BackHover = primary ? Palette.AccentHover : Palette.Hover,
            ForeColor = primary ? Palette.OnAccent : Palette.Ink
        };
        button.Click += click;
        parent.Controls.Add(button);
        return button;
    }

    private static UI.Input InputOn(Control parent, string name, string placeholder, string icon)
    {
        var input = new UI.Input
        {
            AccessibleName = name, PlaceholderText = placeholder, PrefixSvg = icon, Radius = 8,
            BackColor = Palette.Field, BorderColor = Palette.Line, ForeColor = Palette.Ink
        };
        parent.Controls.Add(input);
        return input;
    }

    private static UI.InputNumber MinutesOn(Control parent, string name)
    {
        var input = new UI.InputNumber
        {
            Minimum = 1, Maximum = 10080, AccessibleName = name, ShowControl = true, Radius = 8,
            BackColor = Palette.Field, BorderColor = Palette.Line, ForeColor = Palette.Ink
        };
        parent.Controls.Add(input);
        return input;
    }
}
