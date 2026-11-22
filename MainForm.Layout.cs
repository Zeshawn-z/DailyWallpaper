using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using UI = AntdUI;

namespace BingWallpapers;

internal sealed partial class MainForm
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private readonly ToolTip tip = new ToolTip { AutomaticDelay = 350, AutoPopDelay = 9000 };
    private UI.PageHeader chrome;
    private FramePanel shell, nowCard, searchBox, inspector, metadataBar;
    private Panel todayPage, libraryPage, rotationPage, settingsPage, inspectorDetails;
    private PhotoView hero, desktopPreview, libraryPreview;
    private FrameButton[] navigation, windowButtons, libraryFilters;
    private FrameButton previous, pause, next, todayDate, todayState, resolution, fit, about, favorite,
        more, apply, legendSaved, legendDesktop, older, metadata, copyName, applySaved, revealSaved, removeSaved;
    private InkLabel brandName, nowCaption, nowTitle, nowNext, heroTitle, heroCredit, fileHint, recentTitle,
        marketName, libraryTitle, libraryStats, indexStatus, indexCount, galleryEmpty,
        detailDate, detailSize, detailCredit, detailApplied, nameLabel, tagsLabel, favoriteLabel, rotationLabel;
    private FramePanel nameBox;
    private UI.Input search, editTitle;
    private ToggleControl savedFavorite, savedRotation;
    private FlowLayoutPanel gallery, tags;
    private TableLayoutPanel recentCards;
    private FrameButton sort;
    private PictureBox brandIcon;
    private InkLabel footer;
    private string page = "today";

    private static int S(int n) => Palette.Px(n);
    private static int Logic(int n) => (int)Math.Round(n / Palette.Scale);
    private static void Put(Control c, int x, int y, int w, int h) =>
        c.SetBounds(S(x), S(y), Math.Max(1, S(w)), Math.Max(1, S(h)));

    private void BuildUi()
    {
        AutoScaleMode = AutoScaleMode.None;
        AutoHandDpi = false;
        var work = Screen.PrimaryScreen.WorkingArea;
        using (var canvas = CreateGraphics())
            Palette.Configure(Math.Min(canvas.DpiX / 96f, Math.Min(work.Width / 1120f, work.Height / 760f)));
        Text = "Dayframe";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        BackColor = Palette.Window;
        BorderColor = Palette.Line;
        Font = Palette.Font();
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        MinimumSize = new Size(S(956), S(620));
        Size = new Size(Math.Min(work.Width, S(1280)), Math.Min(work.Height, S(860)));

        chrome = new UI.PageHeader { BackColor = Palette.Window, ShowButton = false, EnableDoubleClickMaximize = true };
        Controls.Add(chrome);
        brandIcon = new PictureBox { Image = Palette.Icon("brand", 20, Palette.Amber), SizeMode = PictureBoxSizeMode.CenterImage };
        chrome.Controls.Add(brandIcon);
        brandName = LabelOn(chrome, "Dayframe", 13, true, Palette.Subtle);
        windowButtons = new[]
        {
            ButtonOn(chrome, "", "minimize", 15, ButtonLook.Plain, () => WindowState = FormWindowState.Minimized, "Minimize"),
            ButtonOn(chrome, "", "maximize", 15, ButtonLook.Plain, () => WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal : FormWindowState.Maximized, "Maximize or restore"),
            ButtonOn(chrome, "", "close", 15, ButtonLook.Plain, Close, "Close")
        };
        foreach (var control in windowButtons)
            control.ForeColor = Palette.Subtle;

        navigation = new[]
        {
            ButtonOn(this, "Today", "today", 20, ButtonLook.Nav, () => ShowPage("today")),
            ButtonOn(this, "Library", "library", 20, ButtonLook.Nav, () => ShowPage("library")),
            ButtonOn(this, "Auto-change", "rotation", 20, ButtonLook.Nav, () => ShowPage("rotation")),
            ButtonOn(this, "Settings", "settings", 20, ButtonLook.Nav, () => ShowPage("settings"))
        };
        nowCard = PanelOn(this, Palette.Surface, 15, true);
        desktopPreview = Add(nowCard, new PhotoView { Radius = 8 });
        nowCaption = LabelOn(nowCard, "Now showing", 11, false, Palette.Muted);
        nowTitle = LabelOn(nowCard, "No wallpaper set", 13, true);
        nowNext = LabelOn(nowCard, "Auto-change is off", 10, false, Palette.Subtle, "JetBrains Mono");
        previous = ButtonOn(nowCard, "", "previous", 16, ButtonLook.Soft, async () => await StepAsync(-1), "Previous wallpaper");
        pause = ButtonOn(nowCard, "", "pause", 16, ButtonLook.Soft, TogglePaused, "Pause auto-change");
        next = ButtonOn(nowCard, "", "next", 16, ButtonLook.Soft, async () => await StepAsync(1), "Next wallpaper");
        foreach (var control in new[] { previous, pause, next })
        {
            control.Radius = 16;
            control.SoftFill = Palette.Hover;
        }

        shell = PanelOn(this, Palette.Canvas, 16, true);
        todayPage = Add(shell, new Panel { BackColor = Palette.Canvas });
        libraryPage = Add(shell, new Panel { BackColor = Palette.Canvas });
        rotationPage = Add(shell, new Panel { BackColor = Palette.Canvas, AutoScroll = true });
        settingsPage = Add(shell, new Panel { BackColor = Palette.Canvas, AutoScroll = true });
        MakeToday();
        MakeLibrary();
        MakeRotation();
        MakeSettings();
        footer = LabelOn(this, "", 11, false, Palette.Muted);
        footer.Align = StringAlignment.Far;
        Resize += (_, __) => PlaceUi();
        PlaceUi();
        ShowPage("today");
    }

    private void MakeToday()
    {
        hero = Add(todayPage, new PhotoView { Radius = 21, Hero = true });
        todayDate = ButtonOn(hero, "", "today", 16, ButtonLook.Glass, null);
        todayState = ButtonOn(hero, "", null, 0, ButtonLook.Glass, null);
        resolution = ButtonOn(hero, "", null, 0, ButtonLook.Glass, null);
        foreach (var item in new[] { todayDate, todayState, resolution })
        {
            item.Cursor = Cursors.Default;
            item.TabStop = false;
            item.Radius = 16;
        }
        resolution.Font = Palette.Font(12, true, "JetBrains Mono");
        fit = ButtonOn(hero, "Fill", "desktop", 16, ButtonLook.Glass, ShowFitChoices);
        fit.EndGlyph = "down";
        fit.EndGlyphSize = 15;
        heroTitle = LabelOn(hero, "Getting today’s wallpaper…", 56, false, Palette.Text, "Instrument Serif");
        heroCredit = LabelOn(hero, "", 12, false, Palette.Text, "JetBrains Mono");
        about = ButtonOn(hero, "About this image", "external", 16, ButtonLook.Plain, OpenAbout);
        favorite = ButtonOn(hero, "", "heart", 20, ButtonLook.Glass, async () => await ToggleTodayFavoriteAsync(), "Add to favorites");
        more = ButtonOn(hero, "", "more", 20, ButtonLook.Glass, ShowTodayActions, "More actions");
        apply = ButtonOn(hero, "Set as wallpaper", "desktop", 18, ButtonLook.Primary, async () => await ApplyAsync(selected));
        favorite.Radius = more.Radius = apply.Radius = 14;
        fileHint = LabelOn(hero, "", 11, false, Palette.Text, "JetBrains Mono");
        fileHint.Align = StringAlignment.Far;

        recentTitle = LabelOn(todayPage, "Last 8 days", 23, false, Palette.Text, "Instrument Serif");
        marketName = LabelOn(todayPage, "Bing · United States", 12, false, Palette.Muted);
        legendSaved = ButtonOn(todayPage, "Saved", "check", 14, ButtonLook.Plain, null);
        legendDesktop = ButtonOn(todayPage, "On desktop", "desktop", 14, ButtonLook.Plain, null);
        legendSaved.GlyphColor = Palette.Blue;
        legendDesktop.GlyphColor = Palette.Amber;
        foreach (var item in new[] { legendSaved, legendDesktop })
        {
            item.ForeColor = Palette.Muted;
            item.Font = Palette.Font(12);
            item.TabStop = false;
            item.Cursor = Cursors.Default;
        }
        older = ButtonOn(todayPage, "Older ones are in Library", null, 0, ButtonLook.Plain, () => ShowPage("library"));
        older.EndGlyph = "right";
        older.Font = Palette.Font(13);
        recentCards = Add(todayPage, new TableLayoutPanel { BackColor = Palette.Canvas, RowCount = 1 });
        recentCards.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    }

    private void MakeLibrary()
    {
        libraryTitle = LabelOn(libraryPage, "Library", 42, false, Palette.Text, "Instrument Serif");
        libraryStats = LabelOn(libraryPage, "", 13, false, Palette.Muted);
        searchBox = PanelOn(libraryPage, Palette.Canvas, 10, true);
        var glass = Add(searchBox, new PictureBox { Image = Palette.Icon("search", 18, Palette.Muted), SizeMode = PictureBoxSizeMode.CenterImage });
        Put(glass, 10, 10, 22, 20);
        search = InputOn(searchBox, "Search title or tag");
        search.BorderWidth = 0;
        search.TextChanged += (_, __) => RenderLibrary();
        sort = ButtonOn(libraryPage, "Newest first", null, 0, ButtonLook.Outline, ShowSortChoices);
        sort.EndGlyph = "down";
        string[] filters = { "All", "Favorites", "In rotation", "Missing file" };
        libraryFilters = filters.Select((value, index) => ButtonOn(libraryPage, value, index == 3 ? "warning" : null,
            14, ButtonLook.Chip, () => { libraryFilter = index; RenderLibrary(); })).ToArray();
        foreach (var item in libraryFilters)
        {
            item.Font = Palette.Font(13);
            item.Radius = 15;
        }

        gallery = Add(libraryPage, new FlowLayoutPanel
        {
            AutoScroll = true, WrapContents = true, BackColor = Palette.Canvas, Margin = Padding.Empty
        });
        gallery.Scroll += (_, __) => QueueGalleryPreviews();
        gallery.MouseWheel += (_, __) => BeginInvoke(new Action(QueueGalleryPreviews));
        galleryEmpty = LabelOn(gallery, "", 14, false, Palette.Muted);

        metadataBar = PanelOn(libraryPage, Palette.Surface, 12, true);
        indexStatus = LabelOn(metadataBar, "", 13, true);
        indexCount = LabelOn(metadataBar, "", 11, false, Palette.Muted, "JetBrains Mono");
        metadata = ButtonOn(metadataBar, "Manage metadata", "json", 16, ButtonLook.Soft, OpenMetadata);

        inspector = PanelOn(libraryPage, Palette.Surface, 17, true);
        inspectorDetails = Add(inspector, new Panel { AutoScroll = true, BackColor = Palette.Surface });
        libraryPreview = Add(inspectorDetails, new PhotoView());
        editTitle = InputOn(inspectorDetails, "Title");
        editTitle.Font = Palette.Font(29, false, "Instrument Serif");
        editTitle.BackColor = Palette.Surface;
        editTitle.BorderWidth = 0;
        editTitle.LostFocus += (_, __) => CommitTitle();
        editTitle.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { CommitTitle(); e.SuppressKeyPress = true; }
        };
        string[] labels = { "Date", "Size", "Credit", "Applied" };
        metadataFields = labels.Select(label => LabelOn(inspectorDetails, label, 12, false, Palette.Muted)).ToArray();
        detailDate = LabelOn(inspectorDetails, "", 13);
        detailSize = LabelOn(inspectorDetails, "", 13);
        detailCredit = LabelOn(inspectorDetails, "", 13);
        detailApplied = LabelOn(inspectorDetails, "", 13);
        detailFields = new[] { detailDate, detailSize, detailCredit, detailApplied };
        nameLabel = LabelOn(inspectorDetails, "File", 12, false, Palette.Muted);
        nameBox = PanelOn(inspectorDetails, Palette.Canvas, 10, true);
        savedName = LabelOn(nameBox, "", 12, false, Palette.Text, "JetBrains Mono");
        copyName = ButtonOn(nameBox, "", "copy", 16, ButtonLook.Plain, () => Copy(savedEntry?.FileName), "Copy file name");
        tagsLabel = LabelOn(inspectorDetails, "Tags", 12, false, Palette.Muted);
        tags = Add(inspectorDetails, new FlowLayoutPanel { WrapContents = true, BackColor = Palette.Surface });
        favoriteLabel = LabelOn(inspectorDetails, "Favorite");
        rotationLabel = LabelOn(inspectorDetails, "Include in auto-change");
        savedFavorite = ToggleOn(inspectorDetails, "Favorite", value => ChangeEntry(entry => entry.With(favorite: value)));
        savedRotation = ToggleOn(inspectorDetails, "Include in auto-change", value => ChangeEntry(entry => entry.With(rotation: value)));
        applySaved = ButtonOn(inspector, "Set as wallpaper", "desktop", 18, ButtonLook.Primary,
            async () => await ApplyAsync(savedEntry?.Wallpaper));
        revealSaved = ButtonOn(inspector, "Reveal in folder", "folder", 18, ButtonLook.Soft,
            () => { if (savedEntry != null) Reveal(library.PathOf(savedEntry)); });
        removeSaved = ButtonOn(inspector, "", "trash", 18, ButtonLook.Soft, DeleteSelected, "Remove from library");
        removeSaved.GlyphColor = Palette.Error;
    }

    private InkLabel[] metadataFields, detailFields;
    private InkLabel savedName;

    private void PlaceUi()
    {
        if (footer == null || ClientSize.Width < 200)
            return;
        int width = Logic(ClientSize.Width), height = Logic(ClientSize.Height);
        Put(chrome, 0, 0, width, 40);
        Put(brandIcon, 16, 10, 20, 20);
        Put(brandName, 46, 0, 110, 40);
        for (int i = 0; i < 3; i++)
            Put(windowButtons[i], width - 138 + i * 46, 1, 46, 39);
        for (int i = 0; i < navigation.Length; i++)
            Put(navigation[i], 12, 48 + i * 44, 176, 40);
        Put(nowCard, 12, height - 223, 176, 211);
        Put(desktopPreview, 11, 11, 154, 87);
        Put(nowCaption, 11, 106, 154, 18);
        Put(nowTitle, 11, 123, 154, 20);
        Put(nowNext, 11, 143, 154, 18);
        Put(previous, 11, 168, 32, 32);
        Put(pause, 72, 168, 32, 32);
        Put(next, 133, 168, 32, 32);
        Put(shell, 200, 40, width - 208, height - 48);
        int w = Logic(shell.Width), h = Logic(shell.Height);
        foreach (var view in new[] { todayPage, libraryPage, rotationPage, settingsPage })
            Put(view, 1, 1, w - 2, h - 2);
        Put(footer, 225, height - 29, width - 257, 20);
        footer.BringToFront();
        PlaceToday(w - 2, h - 2);
        PlaceLibrary(w - 2, h - 2);
        PlaceRotation(w - 2, h - 2);
        PlaceSettings(w - 2, h - 2);
    }

    private void PlaceToday(int w, int h)
    {
        int x = 24, width = w - 48;
        int height = Math.Max(250, Math.Min(width * 9 / 16, h - 190));
        Put(hero, x, 24, width, height);
        Put(todayDate, 20, 20, 178, 32);
        Put(todayState, 206, 20, settings?.CurrentId != null && settings.CurrentId == selected?.Id ? 148 : 112, 32);
        Put(resolution, width - 225, 20, 104, 32);
        Put(fit, width - 112, 20, 92, 32);
        Put(heroTitle, 32, height - 137, width - 379, 73);
        int creditWidth = Math.Max(0, Math.Min(heroCredit.Text.Length * 7 + 7, Math.Min(265, width - 550)));
        Put(heroCredit, 32, height - 57, creditWidth, 26);
        Put(about, 41 + creditWidth, height - 59, 164, 30);
        Put(favorite, width - 342, height - 102, 48, 48);
        Put(more, width - 284, height - 102, 48, 48);
        Put(apply, width - 226, height - 102, 194, 48);
        Put(fileHint, width - 394, height - 48, 362, 24);
        int stripTop = 24 + height + 17;
        Put(recentTitle, x, stripTop, 104, 37);
        Put(marketName, x + 101, stripTop + 3, 175, 34);
        Put(legendSaved, w - 413, stripTop + 4, 76, 30);
        Put(legendDesktop, w - 337, stripTop + 4, 105, 30);
        Put(older, w - 232, stripTop + 4, 208, 30);
        Put(recentCards, x - 3, stripTop + 43, width + 6, 96);
    }

    private void PlaceLibrary(int w, int h)
    {
        int side = Math.Max(252, Math.Min(320, (int)(w * .30)));
        int start = w - side - 24;
        int body = start - 48;
        Put(libraryTitle, 24, 20, Math.Max(146, body - 370), 49);
        Put(libraryStats, 24, 68, body - 355, 24);
        int sortWidth = Math.Min(150, body / 4);
        int searchWidth = Math.Min(210, body / 3);
        Put(sort, start - 24 - sortWidth, 50, sortWidth, 40);
        Put(searchBox, Logic(sort.Left) - searchWidth - 8, 50, searchWidth, 40);
        Put(search, 35, 3, searchWidth - 39, 34);
        int filterX = 24;
        int[] filterWidths = { 71, 104, 116, 139 };
        for (int i = 0; i < libraryFilters.Length; i++)
        {
            Put(libraryFilters[i], filterX, 104, filterWidths[i], 30);
            filterX += filterWidths[i] + 7;
        }
        Put(gallery, 24, 147, body, h - 226);
        Put(metadataBar, 24, h - 73, body, 48);
        Put(indexStatus, 16, 10, Math.Max(112, body / 4), 28);
        Put(indexCount, Math.Max(145, body / 4 + 15), 10, Math.Max(20, body - 332), 28);
        Put(metadata, body - 169, 8, 159, 32);
        Put(inspector, start, 24, side, h - 48);
        int actionWidth = side - 34, panelHeight = h - 48;
        int detailsHeight = panelHeight - 126;
        int inner = detailsHeight < 540 ? actionWidth - 16 : actionWidth;
        int photoHeight = Math.Min(inner * 9 / 16, Math.Max(78, detailsHeight - 405));
        Put(inspectorDetails, 1, 12, side - 2, detailsHeight);
        int scrollY = Logic(inspectorDetails.AutoScrollPosition.Y);
        Put(libraryPreview, 15, scrollY + 5, inner, photoHeight);
        int y = scrollY + 5 + photoHeight + 9;
        Put(editTitle, 14, y, inner + 4, 40);
        for (int i = 0; i < 4; i++)
        {
            Put(metadataFields[i], 16, y + 47 + i * 27, 87, 22);
            Put(detailFields[i], 106, y + 47 + i * 27, inner - 90, 22);
        }
        Put(nameLabel, 16, y + 165, inner, 22);
        Put(nameBox, 16, y + 189, inner, 46);
        Put(savedName, 12, 3, inner - 51, 40);
        Put(copyName, inner - 40, 8, 32, 30);
        Put(tagsLabel, 16, y + 241, inner, 20);
        Put(tags, 16, y + 266, inner, 39);
        Put(favoriteLabel, 16, y + 312, inner - 45, 27);
        Put(savedFavorite, inner - 25, y + 313, 40, 24);
        Put(rotationLabel, 16, y + 351, inner - 45, 28);
        Put(savedRotation, inner - 25, y + 352, 40, 24);
        inspectorDetails.AutoScrollMinSize = new Size(0, S(y - scrollY + 383));
        Put(applySaved, 16, panelHeight - 105, actionWidth, 40);
        Put(revealSaved, 16, panelHeight - 57, actionWidth - 48, 40);
        Put(removeSaved, side - 57, panelHeight - 57, 40, 40);
        SizeGallery();
    }

    internal static T Add<T>(Control parent, T child) where T : Control
    {
        parent.Controls.Add(child);
        return child;
    }

    internal static FramePanel PanelOn(Control parent, Color fill, int radius, bool border = false) =>
        Add(parent, new FramePanel(fill, radius, border ? Palette.Line : (Color?)null));

    internal static InkLabel LabelOn(Control parent, string text, int size = 14, bool bold = false,
        Color? color = null, string face = "Inter") =>
        Add(parent, new InkLabel { Text = text, Font = Palette.Font(size, bold, face), ForeColor = color ?? Palette.Text });

    internal static FrameButton ButtonOn(Control parent, string text, string glyph, int glyphSize,
        ButtonLook look, Action action, string description = null)
    {
        var control = Add(parent, new FrameButton
        {
            Text = text, Glyph = glyph, GlyphSize = glyphSize, Look = look, AccessibleName = description ?? text,
            Font = Palette.Font(14, look == ButtonLook.Primary || look == ButtonLook.Soft)
        });
        if (action != null)
            control.Click += (_, __) => action();
        return control;
    }

    internal static UI.Input InputOn(Control parent, string name) =>
        Add(parent, new UI.Input
        {
            AccessibleName = name, PlaceholderText = name, ForeColor = Palette.Text, BackColor = Palette.Canvas,
            BorderColor = Palette.Line, Radius = 10, Font = Palette.Font()
        });

    private static ToggleControl ToggleOn(Control parent, string label, Action<bool> action)
    {
        var control = Add(parent, new ToggleControl { AccessibleName = label });
        control.CheckedChanged += (_, __) => action(control.Checked);
        return control;
    }
}
