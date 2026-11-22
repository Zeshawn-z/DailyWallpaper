using System;
using System.Drawing;
using System.Windows.Forms;

namespace BingWallpapers;

internal sealed partial class MainForm
{
    private Panel rail, explore, collection, preferences;
    private Label explorerTitle, explorerSub, recentTitle, recentCount, footer, schedule,
        libraryTitle, librarySub, searchLabel, savedTitle, savedDetail, settingsTitle, settingsSub,
        folderTitle, folderHelp, proxyTitle, proxyHelp, rotationTitle, rotationHelp, intervalLabel, rangeLabel, maxLabel;
    private Panel folderCard, proxyCard, rotationCard, savedCard, libraryEmpty;
    private ActionButton exploreNav, libraryNav, settingsNav, toTray, refresh, apply, revealLibrary,
        applySaved, revealSaved, removeSaved, browse, saveSettings;
    private PhotoPanel hero, savedPhoto;
    private TableLayoutPanel recentCards;
    private DataGridView savedGrid;
    private TextBox search, folderPath, proxyAddress;
    private ComboBox proxyMode, rotationMode;
    private NumericUpDown interval, randomMinimum, randomMaximum;
    private PictureBox logo;
    private Label product, productSub;
    private Panel rule;
    private readonly ToolTip tip = new ToolTip { AutomaticDelay = 450, AutoPopDelay = 8000 };
    private float scale;
    private string page = "explore";
    private int S(int value) => (int)Math.Round(value * scale);

    private void BuildUi()
    {
        AutoScaleMode = AutoScaleMode.None;
        using (var canvas = CreateGraphics())
            scale = canvas.DpiX / 96f;
        Text = "Bing Wallpapers · 每日壁纸";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        Font = Theme.Font(10);
        BackColor = Theme.Paper;
        ForeColor = Theme.Ink;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        var workArea = Screen.PrimaryScreen.WorkingArea;
        MinimumSize = new Size(Math.Min(S(890), workArea.Width), Math.Min(S(670), workArea.Height));
        Size = new Size(Math.Min(S(1200), workArea.Width), Math.Min(S(823), workArea.Height));

        rail = AddPanel(this, Theme.Rail);
        logo = new PictureBox { Image = Icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Rail };
        rail.Controls.Add(logo);
        product = AddLabel(rail, "Bing Wallpapers", 11, true);
        productSub = AddLabel(rail, "每日壁纸", 9, false, Theme.Muted);
        exploreNav = ButtonOn(rail, "探索壁纸", "image", (_, __) => ShowPage("explore"));
        libraryNav = ButtonOn(rail, "本地收藏", "folder", (_, __) => ShowPage("library"));
        settingsNav = ButtonOn(rail, "设置", "settings", (_, __) => ShowPage("settings"));
        toTray = ButtonOn(rail, "转到托盘", "tray", (_, __) => HideToTray());
        foreach (var nav in new[] { exploreNav, libraryNav, settingsNav, toTray })
            nav.AlignLeft = true;
        schedule = AddLabel(rail, "", 8.5f, false, Theme.Muted);
        rule = AddPanel(rail, Theme.Line);

        explore = AddPanel(this, Theme.Paper);
        explorerTitle = AddLabel(explore, "探索壁纸", 25, true);
        explorerSub = AddLabel(explore, "浏览 Bing 中国区每日壁纸", 10, false, Theme.Muted);
        refresh = ButtonOn(explore, "刷新", "refresh", async (_, __) => await RefreshFeedAsync(true));
        hero = new PhotoPanel(true) { S = S };
        explore.Controls.Add(hero);
        apply = ButtonOn(hero, "设为壁纸", "desktop", async (_, __) => await ChangeWallpaperAsync(selected));
        apply.BackColor = Color.Transparent;
        PaintPrimary(apply);
        recentTitle = AddLabel(explore, "最近发布", 14, true);
        recentCount = AddLabel(explore, "", 9, false, Theme.Muted);
        recentCount.TextAlign = ContentAlignment.MiddleRight;
        recentCards = new TableLayoutPanel { RowCount = 1, BackColor = Theme.Paper, Margin = Padding.Empty };
        recentCards.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        explore.Controls.Add(recentCards);

        collection = AddPanel(this, Theme.Paper);
        libraryTitle = AddLabel(collection, "本地收藏", 25, true);
        librarySub = AddLabel(collection, "", 10, false, Theme.Muted);
        revealLibrary = ButtonOn(collection, "打开目录", "folder", (_, __) => OpenLibrary());
        searchLabel = AddLabel(collection, "搜索标题或日期", 9, false, Theme.Muted);
        search = AddTextBox(collection);
        search.AccessibleName = searchLabel.Text;
        search.TextChanged += (_, __) => RenderLibrary();
        savedGrid = new DataGridView
        {
            BackgroundColor = Theme.White, BorderStyle = BorderStyle.None,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            GridColor = Theme.Line, RowHeadersVisible = false, ReadOnly = true, MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, AllowUserToResizeRows = false, EnableHeadersVisualStyles = false,
            ColumnHeadersHeight = S(39), ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            AccessibleName = "已保存的壁纸"
        };
        savedGrid.RowTemplate.Height = S(39);
        savedGrid.DefaultCellStyle.Padding = new Padding(S(8), 0, S(4), 0);
        savedGrid.DefaultCellStyle.BackColor = Theme.White;
        savedGrid.DefaultCellStyle.ForeColor = Theme.Ink;
        savedGrid.DefaultCellStyle.SelectionBackColor = Theme.Tint;
        savedGrid.DefaultCellStyle.SelectionForeColor = Theme.Accent;
        savedGrid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Rail;
        savedGrid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
        savedGrid.ColumnHeadersDefaultCellStyle.Padding = new Padding(S(8), 0, S(4), 0);
        savedGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "壁纸", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
        savedGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Bing 日期", Width = S(108), SortMode = DataGridViewColumnSortMode.NotSortable });
        savedGrid.SelectionChanged += (_, __) => UpdateSavedPreview();
        collection.Controls.Add(savedGrid);
        libraryEmpty = AddPanel(collection, Theme.White);
        var empty = AddLabel(libraryEmpty, "这里还没有壁纸", 13, true);
        empty.TextAlign = ContentAlignment.MiddleCenter;
        empty.Dock = DockStyle.Fill;
        savedCard = AddPanel(collection, Theme.White);
        savedPhoto = new PhotoPanel(false) { S = S, BackColor = Theme.White };
        savedCard.Controls.Add(savedPhoto);
        savedTitle = AddLabel(savedCard, "", 14, true);
        savedDetail = AddLabel(savedCard, "", 9, false, Theme.Muted);
        applySaved = ButtonOn(savedCard, "设为壁纸", "desktop", async (_, __) => await ChangeWallpaperAsync(SelectedSaved?.Wallpaper));
        PaintPrimary(applySaved);
        revealSaved = ButtonOn(savedCard, "定位文件", null, (_, __) => RevealSaved());
        removeSaved = ButtonOn(savedCard, "删除", null, (_, __) => DeleteSaved());
        removeSaved.ForeColor = Theme.Alert;

        preferences = AddPanel(this, Theme.Paper);
        settingsTitle = AddLabel(preferences, "设置", 25, true);
        settingsSub = AddLabel(preferences, "管理保存位置、网络连接和自动换图", 10, false, Theme.Muted);
        saveSettings = ButtonOn(preferences, "保存设置", null, (_, __) => SaveSettings());
        PaintPrimary(saveSettings);
        folderCard = AddPanel(preferences, Theme.White);
        folderTitle = AddLabel(folderCard, "保存目录", 14, true);
        folderHelp = AddLabel(folderCard, "更改目录会切换壁纸库，原文件会保留。", 9, false, Theme.Muted);
        folderPath = AddTextBox(folderCard);
        folderPath.AccessibleName = "壁纸保存目录";
        browse = ButtonOn(folderCard, "浏览…", null, (_, __) => BrowseForDirectory());
        proxyCard = AddPanel(preferences, Theme.White);
        proxyTitle = AddLabel(proxyCard, "网络代理", 14, true);
        proxyHelp = AddLabel(proxyCard, "应用于 Bing 列表、预览和原图下载。", 9, false, Theme.Muted);
        proxyMode = AddChoices(proxyCard, "代理模式", "跟随系统", "直接连接", "自定义 HTTP 代理");
        proxyMode.SelectedIndexChanged += (_, __) => proxyAddress.Enabled = proxyMode.SelectedIndex == 2;
        proxyAddress = AddTextBox(proxyCard);
        proxyAddress.AccessibleName = "HTTP 代理地址";
        tip.SetToolTip(proxyAddress, "例如 127.0.0.1:7890");
        rotationCard = AddPanel(preferences, Theme.White);
        rotationTitle = AddLabel(rotationCard, "自动换图", 14, true);
        rotationHelp = AddLabel(rotationCard, "转到托盘后继续运行，退出程序后停止。", 9, false, Theme.Muted);
        rotationMode = AddChoices(rotationCard, "自动换图模式", "关闭", "固定间隔 · 顺序轮换", "随机间隔 · 随机壁纸");
        rotationMode.SelectedIndexChanged += (_, __) => UpdateRotationInputs();
        intervalLabel = AddLabel(rotationCard, "固定间隔（分钟）", 9, false, Theme.Muted);
        interval = AddMinutes(rotationCard, "固定间隔分钟数");
        rangeLabel = AddLabel(rotationCard, "随机间隔（分钟）", 9, false, Theme.Muted);
        randomMinimum = AddMinutes(rotationCard, "随机间隔最短分钟数");
        maxLabel = AddLabel(rotationCard, "至", 9, false, Theme.Muted);
        randomMaximum = AddMinutes(rotationCard, "随机间隔最长分钟数");

        footer = AddLabel(this, "", 9, false, Theme.Muted);
        Resize += (_, __) => PlaceUi();
        PlaceUi();
        ShowPage("explore");
    }

    private void PlaceUi()
    {
        if (ClientSize.Width == 0 || ClientSize.Height == 0 || footer == null)
            return;
        int railWidth = S(198);
        int gutter = S(27);
        int top = S(20);
        int w = Math.Max(S(405), ClientSize.Width - railWidth - 2 * gutter);
        int h = Math.Max(S(440), ClientSize.Height - top - S(36));
        rail.SetBounds(0, 0, railWidth, ClientSize.Height);
        logo.SetBounds(S(19), S(27), S(39), S(39));
        product.SetBounds(S(66), S(24), S(128), S(24));
        productSub.SetBounds(S(66), S(48), S(120), S(18));
        exploreNav.SetBounds(S(16), S(103), railWidth - S(32), S(42));
        libraryNav.SetBounds(S(16), S(154), railWidth - S(32), S(42));
        settingsNav.SetBounds(S(16), S(205), railWidth - S(32), S(42));
        schedule.SetBounds(S(19), ClientSize.Height - S(104), railWidth - S(33), S(22));
        rule.SetBounds(S(20), ClientSize.Height - S(75), railWidth - S(40), 1);
        toTray.SetBounds(S(16), ClientSize.Height - S(61), railWidth - S(32), S(42));
        foreach (var view in new[] { explore, collection, preferences })
            view.SetBounds(railWidth + gutter, top, w, h);
        footer.SetBounds(railWidth + gutter, ClientSize.Height - S(30), w, S(25));

        PageHeader(explorerTitle, explorerSub, refresh, w, S(100));
        int heroHeight = h - S(229);
        hero.SetBounds(0, S(88), w, heroHeight);
        apply.SetBounds(w - S(160), heroHeight - S(62), S(136), S(41));
        recentTitle.SetBounds(0, hero.Bottom + S(12), S(155), S(31));
        recentCount.SetBounds(w - S(124), recentTitle.Top, S(124), S(31));
        recentCards.SetBounds(0, recentTitle.Bottom + S(4), w, S(89));

        PageHeader(libraryTitle, librarySub, revealLibrary, w, S(122));
        int gridWidth = (int)(w * .58f);
        searchLabel.SetBounds(0, S(87), S(126), S(26));
        search.SetBounds(S(126), S(86), Math.Max(S(110), gridWidth - S(126)), S(29));
        savedGrid.SetBounds(0, S(125), gridWidth, h - S(125));
        libraryEmpty.SetBounds(0, S(125), gridWidth, h - S(125));
        int detailWidth = w - gridWidth - S(15);
        savedCard.SetBounds(gridWidth + S(15), S(86), detailWidth, h - S(86));
        int inner = detailWidth - S(28);
        int imageHeight = Math.Min(S(220), inner * 9 / 16);
        savedPhoto.SetBounds(S(14), S(14), inner, imageHeight);
        int infoTop = savedPhoto.Bottom + S(6);
        savedTitle.SetBounds(S(14), infoTop, inner, S(46));
        savedDetail.SetBounds(S(14), infoTop + S(50), inner, S(62));
        applySaved.SetBounds(S(14), infoTop + S(120), inner, S(40));
        revealSaved.SetBounds(S(14), infoTop + S(170), inner * 2 / 3 - S(4), S(35));
        removeSaved.SetBounds(revealSaved.Right + S(8), revealSaved.Top, inner - revealSaved.Width - S(8), S(35));

        PageHeader(settingsTitle, settingsSub, saveSettings, w, S(114));
        folderCard.SetBounds(0, S(88), w, S(132));
        folderTitle.SetBounds(S(18), S(11), w - S(36), S(33));
        folderHelp.SetBounds(S(18), S(44), w - S(36), S(24));
        folderPath.SetBounds(S(18), S(79), w - S(131), S(28));
        browse.SetBounds(w - S(104), S(75), S(86), S(33));
        proxyCard.SetBounds(0, S(230), w, S(132));
        proxyTitle.SetBounds(S(18), S(11), w - S(36), S(33));
        proxyHelp.SetBounds(S(18), S(44), w - S(36), S(24));
        proxyMode.SetBounds(S(18), S(79), S(188), S(28));
        proxyAddress.SetBounds(S(220), S(79), w - S(238), S(28));
        rotationCard.SetBounds(0, S(372), w, S(163));
        rotationTitle.SetBounds(S(18), S(11), w - S(36), S(33));
        rotationHelp.SetBounds(S(18), S(44), w - S(36), S(24));
        rotationMode.SetBounds(S(18), S(82), S(188), S(28));
        intervalLabel.SetBounds(S(220), S(81), S(132), S(29));
        interval.SetBounds(S(351), S(83), S(79), S(30));
        rangeLabel.SetBounds(S(220), S(81), S(132), S(29));
        randomMinimum.SetBounds(S(351), S(83), S(79), S(30));
        maxLabel.SetBounds(S(438), S(81), S(20), S(29));
        randomMaximum.SetBounds(S(463), S(83), S(79), S(30));
    }

    private void PageHeader(Label title, Label subtitle, ActionButton action, int width, int buttonWidth)
    {
        title.SetBounds(0, 0, width - buttonWidth - S(12), S(52));
        subtitle.SetBounds(1, S(53), width - buttonWidth - S(12), S(23));
        action.SetBounds(width - buttonWidth, S(21), buttonWidth, S(38));
    }

    private static Panel AddPanel(Control parent, Color color)
    {
        var panel = new Panel { BackColor = color };
        parent.Controls.Add(panel);
        return panel;
    }

    private Label AddLabel(Control parent, string text, float size, bool bold, Color? color = null)
    {
        var label = new Label
        {
            Text = text, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
            Font = Theme.Font(size, bold ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = color ?? Theme.Ink, BackColor = Color.Transparent, UseMnemonic = false
        };
        parent.Controls.Add(label);
        return label;
    }

    private ActionButton ButtonOn(Control parent, string text, string glyph, EventHandler action)
    {
        var button = new ActionButton { Text = text, Glyph = glyph, Unit = Math.Max(1, (int)Math.Round(scale)) };
        button.Click += action;
        parent.Controls.Add(button);
        return button;
    }

    private static void PaintPrimary(ActionButton button)
    {
        button.Fill = Theme.Accent;
        button.Stroke = Theme.Accent;
        button.ForeColor = Color.White;
        button.HoverFill = Color.FromArgb(40, 111, 88);
    }

    private static TextBox AddTextBox(Control parent)
    {
        var control = new TextBox { BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
        parent.Controls.Add(control);
        return control;
    }

    private static ComboBox AddChoices(Control parent, string name, params string[] choices)
    {
        var control = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = name };
        control.Items.AddRange(choices);
        parent.Controls.Add(control);
        return control;
    }

    private static NumericUpDown AddMinutes(Control parent, string name)
    {
        var control = new NumericUpDown
        {
            Minimum = 1, Maximum = 10080, TextAlign = HorizontalAlignment.Right,
            AccessibleName = name, BorderStyle = BorderStyle.FixedSingle
        };
        parent.Controls.Add(control);
        return control;
    }
}
