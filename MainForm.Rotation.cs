using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using UI = AntdUI;

namespace BingWallpapers;

internal sealed partial class MainForm
{
    private readonly RotationPlan plan = new RotationPlan();
    private FramePanel masterCard, schedulePanel, poolPanel, nextPanel, historyPanel, backgroundPanel, segments;
    private InkLabel autoTitle, autoSub, masterTitle, masterHint, scheduleTitle, scheduleSub, intervalIntro,
        minUnit, andLabel, maxUnit, timesTitle, timesHint, poolTitle, avoidTitle, nextCaption, nextTime,
        nextDate, nextItemCaption, nextItemTitle, historyTitle, backgroundTitle;
    private ToggleControl master, avoid, startWindows, keepTray, onBattery;
    private FrameButton[] modes, presets, pools;
    private readonly int[] minutes = { 5, 15, 30, 60, 180, 360 };
    private UI.InputNumber interval, minInterval, maxInterval;
    private FrameButton shuffle, changeNow, pauseFor;
    private PhotoView nextPreview;
    private InkLabel[] historyTimes, historyNames, backgroundLabels;
    private PhotoView[] historyPhotos;
    private RangeTrack range;
    private ScheduleTimeline timeline;
    private Countdown countdown;
    private Wallpaper queued;
    private CancellationTokenSource queueRequest, historyRequest;
    private string scheduleMode = "fixed";

    private void MakeRotation()
    {
        autoTitle = LabelOn(rotationPage, "Auto-change", 42, false, Palette.Text, Palette.SerifFamily);
        autoSub = LabelOn(rotationPage, "Runs in the background. Closing this window does not stop it.", 13, false, Palette.Muted);
        masterCard = PanelOn(rotationPage, Palette.Surface, 15, true);
        masterTitle = LabelOn(masterCard, "Auto-change is off", 14, true);
        masterHint = LabelOn(masterCard, "", 12, false, Palette.Muted);
        master = ToggleOn(masterCard, "Auto-change", value =>
        {
            if (ready && !loadingControls) UpdateSettings(() => settings.RotationEnabled = value, true);
        });

        schedulePanel = PanelOn(rotationPage, Palette.Surface, 17, true);
        scheduleTitle = LabelOn(schedulePanel, "Schedule", 22, false, Palette.Text, Palette.SerifFamily);
        scheduleSub = LabelOn(schedulePanel, "Applies to the wallpaper, not the screen saver", 12, false, Palette.Muted);
        scheduleSub.Align = StringAlignment.Far;
        segments = PanelOn(schedulePanel, Palette.Canvas, 12, true);
        string[] modeTitles = { "Every X minutes", "Random interval", "New Bing image daily" };
        string[] modeValues = { "fixed", "random", "daily" };
        modes = modeTitles.Select((title, index) => ButtonOn(segments, title, null, 0, ButtonLook.Plain, () =>
        {
            scheduleMode = modeValues[index];
            CommitSchedule();
        })).ToArray();
        intervalIntro = LabelOn(schedulePanel, "Change every", 15, false, Palette.Subtle);
        interval = NumberOn(schedulePanel, "Interval in minutes", 24);
        minInterval = NumberOn(schedulePanel, "Shortest interval in minutes", 18);
        maxInterval = NumberOn(schedulePanel, "Longest interval in minutes", 18);
        minUnit = LabelOn(schedulePanel, "min", 14, false, Palette.Subtle);
        andLabel = LabelOn(schedulePanel, "and", 14, false, Palette.Subtle);
        maxUnit = LabelOn(schedulePanel, "min", 14, false, Palette.Subtle);
        string[] presetLabels = { "5 min", "15 min", "30 min", "1 h", "3 h", "6 h", "Custom" };
        presets = presetLabels.Select((label, index) => ButtonOn(schedulePanel, label, null, 0, ButtonLook.Chip, () =>
        {
            if (index == minutes.Length) { interval.Focus(); interval.SelectAll(); return; }
            interval.Value = minutes[index];
            CommitSchedule();
        })).ToArray();
        foreach (var button in presets) { button.Radius = 15; button.Font = Palette.Font(13); }
        range = Add(schedulePanel, new RangeTrack());
        range.Changed += (_, __) =>
        {
            minInterval.Value = range.Low;
            maxInterval.Value = range.High;
            CommitSchedule();
        };
        timesTitle = LabelOn(schedulePanel, "Next changes", 12, false, Palette.Subtle);
        timesHint = LabelOn(schedulePanel, "", 12, false, Palette.Muted);
        timeline = Add(schedulePanel, new ScheduleTimeline());

        poolPanel = PanelOn(rotationPage, Palette.Surface, 17, true);
        poolTitle = LabelOn(poolPanel, "Pick from", 22, false, Palette.Text, Palette.SerifFamily);
        string[] poolTitles = { "Everything in Library", "Favorites only", "The last 8 days from Bing" };
        string[] poolValues = { "library", "favorites", "bing" };
        pools = poolTitles.Select((title, index) => ButtonOn(poolPanel, title, null, 0, ButtonLook.Choice,
            () => UpdateSettings(() => settings.Pool = poolValues[index], false))).ToArray();
        foreach (var pool in pools)
            pool.Font = Palette.Font(14, true);
        avoidTitle = LabelOn(poolPanel, "Don't repeat the last 10 wallpapers", 14, true);
        avoid = ToggleOn(poolPanel, "Don't repeat recent wallpapers", value =>
        {
            if (ready && !loadingControls) UpdateSettings(() => settings.AvoidRecent = value, false);
        });

        nextPanel = PanelOn(rotationPage, Palette.Surface, 17, true);
        countdown = Add(nextPanel, new Countdown());
        nextCaption = LabelOn(nextPanel, "Next change", 12, false, Palette.Muted);
        nextTime = LabelOn(nextPanel, "—", 40, false, Palette.Text, Palette.SerifFamily);
        nextDate = LabelOn(nextPanel, "", 12, false, Palette.Muted);
        nextPreview = Add(nextPanel, new PhotoView { Radius = 8 });
        nextItemCaption = LabelOn(nextPanel, "Up next", 11, false, Palette.Muted);
        nextItemTitle = LabelOn(nextPanel, "", 13, true);
        shuffle = ButtonOn(nextPanel, "", "shuffle", 16, ButtonLook.Soft, () => PickQueue(true), "Pick another");
        shuffle.Radius = 16;
        changeNow = ButtonOn(nextPanel, "Change now", "next", 16, ButtonLook.Soft,
            async () => await ChangeNowAsync());
        pauseFor = ButtonOn(nextPanel, "Pause for…", null, 0, ButtonLook.Plain, ShowPauseChoices);
        pauseFor.EndGlyph = "down";
        changeNow.Font = Palette.Font(13, true);
        pauseFor.Font = Palette.Font(13);

        historyPanel = PanelOn(rotationPage, Palette.Surface, 17, true);
        historyTitle = LabelOn(historyPanel, "Recent changes", 22, false, Palette.Text, Palette.SerifFamily);
        historyTimes = Enumerable.Range(0, 3).Select(_ => LabelOn(historyPanel, "", 11, false, Palette.Muted, "JetBrains Mono")).ToArray();
        historyNames = Enumerable.Range(0, 3).Select(_ => LabelOn(historyPanel, "", 13, true)).ToArray();
        historyPhotos = Enumerable.Range(0, 3).Select(_ => Add(historyPanel, new PhotoView { Radius = 6 })).ToArray();
        backgroundPanel = PanelOn(rotationPage, Palette.Surface, 17, true);
        backgroundTitle = LabelOn(backgroundPanel, "Background", 22, false, Palette.Text, Palette.SerifFamily);
        string[] backgrounds = { "Start with Windows", "Keep running in the tray when closed", "Pause on battery power" };
        backgroundLabels = backgrounds.Select(item => LabelOn(backgroundPanel, item)).ToArray();
        startWindows = ToggleOn(backgroundPanel, backgrounds[0], value =>
        {
            if (!ready || loadingControls)
                return;
            try { StartWithWindows.Set(value); }
            catch (Exception error) when (Operational(error)) { Report(error); LoadOptions(); }
        });
        keepTray = ToggleOn(backgroundPanel, backgrounds[1], value =>
        {
            if (ready && !loadingControls) UpdateSettings(() => settings.KeepInTray = value, false);
        });
        onBattery = ToggleOn(backgroundPanel, backgrounds[2], value =>
        {
            if (ready && !loadingControls) UpdateSettings(() => settings.PauseOnBattery = value, true);
        });
    }

    private UI.InputNumber NumberOn(Control parent, string description, int fontSize)
    {
        var number = Add(parent, new UI.InputNumber
        {
            Minimum = 1, Maximum = 10080, ShowControl = false, TextAlign = HorizontalAlignment.Center,
            BackColor = Palette.Canvas, BorderColor = Palette.Line, ForeColor = Palette.Text, Radius = 12,
            PaddGap = .15f, AccessibleName = Strings.Get(description), Font = Palette.Font(fontSize, false, "Segoe UI")
        });
        number.LostFocus += (_, __) => CommitSchedule();
        number.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { CommitSchedule(); e.SuppressKeyPress = true; }
        };
        return number;
    }

    private void PlaceRotation(int w, int h)
    {
        var scroll = rotationPage.AutoScrollPosition;
        int sx = Logic(scroll.X), sy = Logic(scroll.Y);
        int usable = w - 48 - (h < 767 ? 15 : 0), side = Math.Min(340, Math.Max(264, usable / 3));
        int main = usable - side - 24, right = 24 + main + 24;
        bool compactRandom = main < 530 && scheduleMode == "random";
        int scheduleHeight = scheduleMode == "random" ? compactRandom ? 356 : 342 : 296;
        Put(autoTitle, sx + 24, sy + 20, main, 50);
        Put(autoSub, sx + 24, sy + 68, main, 23);
        Put(masterCard, sx + right + side - 213, sy + 27, 213, 58);
        Put(masterTitle, 17, 8, 131, 23);
        Put(masterHint, 17, 29, 131, 23);
        Put(master, 156, 17, 40, 24);
        Put(schedulePanel, sx + 24, sy + 104, main, scheduleHeight);
        Put(scheduleTitle, 20, 16, 130, 37);
        Put(scheduleSub, 151, 16, main - 171, 37);
        Put(segments, 20, 65, main - 40, 45);
        int segment = (main - 50) / 3;
        for (int i = 0; i < modes.Length; i++) Put(modes[i], 5 + i * segment, 5, segment, 35);
        bool random = scheduleMode == "random";
        Put(intervalIntro, 20, 141, random ? 217 : scheduleMode == "daily" ? main - 40 : 107, 31);
        Put(interval, 133, 126, 88, 56);
        Put(minInterval, 241, 126, 72, 56);
        Put(minUnit, random ? 324 : 233, 141, random ? 35 : 81, 31);
        Put(andLabel, 359, 141, 34, 31);
        Put(maxInterval, 397, 126, 72, 56);
        Put(maxUnit, 480, 141, 33, 31);
        if (compactRandom)
        {
            Put(intervalIntro, 20, 119, main - 40, 26);
            Put(minInterval, 20, 147, 72, 48);
            Put(minUnit, 103, 153, 35, 31);
            Put(andLabel, 147, 153, 34, 31);
            Put(maxInterval, 186, 147, 72, 48);
            Put(maxUnit, 269, 153, 35, 31);
        }
        int presetX = 20;
        for (int i = 0; i < presets.Length; i++)
        {
            int span = main < 515 ? 57 : i == presets.Length - 1 ? 70 : 66;
            Put(presets[i], presetX, 193, span, 30);
            presetX += span + 5;
        }
        int randomOffset = compactRandom ? 14 : 0;
        Put(range, 24, 186 + randomOffset, main - 48, 57);
        Put(timesTitle, 20, random ? 245 + randomOffset : 228, 88, 28);
        Put(timesHint, 110, random ? 245 + randomOffset : 228, main - 131, 28);
        Put(timeline, 12, random ? 270 + randomOffset : 252, main - 24, random ? 51 : 39);
        Put(poolPanel, sx + 24, sy + 120 + scheduleHeight, main, 304);
        Put(poolTitle, 20, 14, main - 40, 36);
        for (int i = 0; i < 3; i++)
            Put(pools[i], 20, 58 + i * 55, main - 40, 52);
        Put(avoidTitle, 20, 247, main - 88, 34);
        Put(avoid, main - 60, 253, 40, 24);

        Put(nextPanel, sx + right, sy + 104, side, 248);
        Put(countdown, 18, 18, 96, 96);
        Put(nextCaption, 132, 25, side - 149, 20);
        Put(nextTime, 130, 44, side - 149, 42);
        Put(nextDate, 132, 85, side - 151, 21);
        Put(nextPreview, 18, 128, 96, 54);
        Put(nextItemCaption, 126, 133, side - 175, 19);
        Put(nextItemTitle, 126, 152, side - 175, 24);
        Put(shuffle, side - 50, 139, 32, 32);
        Put(changeNow, 18, 195, side - 160, 32);
        Put(pauseFor, side - 142, 195, 124, 32);
        Put(historyPanel, sx + right, sy + 364, side, 181);
        Put(historyTitle, 16, 11, side - 32, 36);
        for (int i = 0; i < historyTimes.Length; i++)
        {
            Put(historyTimes[i], 16, 54 + i * 38, 47, 30);
            Put(historyPhotos[i], 68, 55 + i * 38, 48, 27);
            Put(historyNames[i], 128, 54 + i * 38, side - 146, 30);
        }
        Put(backgroundPanel, sx + right, sy + 557, side, 187);
        Put(backgroundTitle, 16, 12, side - 32, 36);
        var toggles = new[] { startWindows, keepTray, onBattery };
        for (int i = 0; i < toggles.Length; i++)
        {
            Put(backgroundLabels[i], 16, 55 + i * 40, side - 74, 28);
            Put(toggles[i], side - 56, 58 + i * 40, 40, 24);
        }
        rotationPage.AutoScrollMinSize = new Size(0, S(Math.Max(759, 443 + scheduleHeight)));
    }

    private void ConfigureRotationUi()
    {
        bool random = settings.RotationMode == "random", fixedMode = settings.RotationMode == "fixed";
        scheduleMode = settings.RotationMode;
        intervalIntro.Text = Strings.Get(random ? "Change at random, between" : fixedMode ? "Change every" :
            "Apply the new Bing image when it becomes available in the selected market.");
        interval.Visible = fixedMode;
        minInterval.Visible = maxInterval.Visible = andLabel.Visible = maxUnit.Visible = range.Visible = random;
        minUnit.Visible = random || fixedMode;
        minUnit.Text = Strings.Get(random ? "min" : "minutes");
        for (int i = 0; i < presets.Length; i++)
        {
            presets[i].Visible = fixedMode;
            presets[i].Selected = i < minutes.Length ? settings.Interval == minutes[i] : !minutes.Contains(settings.Interval);
            presets[i].Invalidate();
        }
        int modeIndex = random ? 1 : fixedMode ? 0 : 2;
        for (int i = 0; i < modes.Length; i++)
        {
            modes[i].Look = i == modeIndex ? ButtonLook.Soft : ButtonLook.Plain;
            modes[i].ForeColor = i == modeIndex ? Palette.Text : Palette.Subtle;
            modes[i].Invalidate();
        }
        range.Low = settings.RandomMinimum;
        range.High = settings.RandomMaximum;
        range.Invalidate();
        timesHint.Text = random ? Strings.Format("random, averages about {0} min",
            (settings.RandomMinimum + settings.RandomMaximum + 1) / 2)
            : fixedMode ? Strings.Format("every {0} min on the dot", settings.Interval)
            : Strings.Get("every day in the selected Bing market");
        var all = library?.Entries.Count(entry => entry.Rotation && library.Exists(entry)) ?? 0;
        var hearts = library?.Entries.Count(entry => entry.Favorite && entry.Rotation && library.Exists(entry)) ?? 0;
        int selectedPool = settings.Pool == "bing" ? 2 : settings.Pool == "favorites" ? 1 : 0;
        for (int i = 0; i < pools.Length; i++)
        {
            pools[i].Selected = i == selectedPool;
            pools[i].Glyph = i == selectedPool ? "radio-on" : "radio";
            pools[i].GlyphSize = 20;
            pools[i].GlyphColor = i == selectedPool ? Palette.Accent : Palette.Muted;
            pools[i].Enabled = settings.RotationMode != "daily";
            pools[i].Invalidate();
        }
        pools[0].Subtitle = Strings.Format(all == 1 ? "{0} wallpaper" : "{0} wallpapers", all);
        pools[1].Subtitle = Strings.Format(hearts == 1 ? "{0} wallpaper" : "{0} wallpapers", hearts);
        pools[2].Subtitle = Strings.Get("New ones are downloaded when needed");
        if (page == "rotation")
            PlaceActivePage();
    }

    private void PaintRotationStatus()
    {
        if (!ready)
            return;
        var now = DateTimeOffset.Now;
        DateTimeOffset? due = plan.Times.Count > 0 ? plan.Times[0] : (DateTimeOffset?)null;
        bool pausedNow = IsPaused(now);
        string hint = settings.RotationEnabled != true ? Strings.Get("Auto-change is off") : pausedNow
            ? Strings.Get("Auto-change is paused") : WaitingForPower() ? Strings.Get("Paused on battery power") :
            due.HasValue ? Strings.Format("Next change in {0} min", Math.Max(1, (int)Math.Ceiling((due.Value - now).TotalMinutes)))
                : Strings.Get("Waiting for schedule");
        nowNext.Text = hint;
        masterTitle.Text = Strings.Get(settings.RotationEnabled == true ? "Auto-change is on" : "Auto-change is off");
        masterHint.Text = settings.RotationEnabled == true ? hint : Strings.Get("Enable to start a schedule");
        var remaining = due.HasValue && !pausedNow ? due.Value - now : TimeSpan.Zero;
        countdown.Text = remaining.TotalHours >= 1 ? $"{(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : remaining.TotalSeconds > 0 ? $"{remaining.Minutes:00}:{remaining.Seconds:00}" : "—";
        countdown.Progress = due.HasValue && !pausedNow
            ? Math.Min(1, Math.Max(0, remaining.TotalSeconds / Math.Max(1, (due.Value - plan.Started).TotalSeconds))) : 0;
        countdown.Invalidate();
        nextTime.Text = due.HasValue && !pausedNow ? due.Value.LocalDateTime.ToString("HH:mm", Strings.Culture) : "—";
        nextDate.Text = due.HasValue && !pausedNow ? Strings.Date(due.Value.LocalDateTime, "ddd d MMM", "M月d日 ddd") : "";
        nextItemTitle.Text = TitleFor(queued) ?? Strings.Get("No wallpaper queued");
        bool canResume = pausedNow || settings.RotationEnabled != true;
        pause.Glyph = canResume ? "play" : "pause";
        pause.AccessibleName = Strings.Get(canResume ? "Resume auto-change" : "Pause auto-change");
        pause.Invalidate();
        timeline.Times = plan.Times;
        timeline.Last = settings.History.FirstOrDefault()?.At;
        timeline.Invalidate();
    }

    private void ShowRecentChanges()
    {
        Cancel(ref historyRequest);
        historyRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        UpdateHistoryTitles();
        for (int i = 0; i < historyNames.Length; i++)
        {
            var entry = settings.History.ElementAtOrDefault(i);
            historyTimes[i].Text = DateTimeOffset.TryParse(entry?.At, out var time) ?
                time.LocalDateTime.ToString("HH:mm", Strings.Culture) : "";
            historyPhotos[i].Visible = entry != null;
            historyPhotos[i].SetImage(null);
            var item = FindWallpaper(entry?.Id);
            if (item != null)
                _ = LoadPhotoAsync(historyPhotos[i], item, true, historyRequest.Token);
        }
    }

    private void UpdateHistoryTitles()
    {
        for (int i = 0; i < historyNames.Length; i++)
            historyNames[i].Text = TitleFor(settings.History.ElementAtOrDefault(i)) ??
                (i == 0 ? Strings.Get("No changes yet") : "");
    }

    private sealed class Countdown : Control
    {
        public double Progress { get; set; }
        public Countdown()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            Palette.PaintControlBackground(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int inset = S(6);
            int stroke = S(6);
            var circle = new Rectangle(inset, inset, Width - 2 * inset, Height - 2 * inset);
            using (var track = new Pen(Palette.Raised, stroke))
            using (var active = new Pen(Palette.Accent, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                e.Graphics.DrawEllipse(track, circle);
                if (Progress > 0)
                    e.Graphics.DrawArc(active, circle, -90, (float)(Progress * 360));
            }
            var textBounds = Rectangle.Inflate(circle, -(stroke / 2 + S(4)), 0);
            e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var format = StringFormat.GenericTypographic)
            {
                int size = 20;
                Font font = Palette.Font(size, false, "JetBrains Mono");
                while (size > 8 && e.Graphics.MeasureString(Text, font, int.MaxValue, format).Width > textBounds.Width)
                    font = Palette.Font(--size, false, "JetBrains Mono");
                Palette.Write(e.Graphics, Text, font, textBounds, Palette.Text, StringAlignment.Center);
            }
        }
    }

    private sealed class ScheduleTimeline : Control
    {
        public IReadOnlyList<DateTimeOffset> Times { get; set; } = Array.Empty<DateTimeOffset>();
        public string Last { get; set; }
        public ScheduleTimeline()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Palette.PaintControlBackground(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int edge = S(14), y = S(13);
            using (var pen = new Pen(Palette.Raised, S(2))) g.DrawLine(pen, edge, y, Width - edge, y);
            if (Times.Count == 0)
                return;
            DateTimeOffset first = DateTimeOffset.TryParse(Last, out var last) && last < Times[0] ? last : DateTimeOffset.Now;
            if (first > Times[0])
                first = Times[0].AddMinutes(-1);
            var dates = new[] { first }.Concat(Times).ToList();
            double duration = Math.Max(1, (dates.Last() - first).TotalMinutes);
            for (int i = 0; i < dates.Count; i++)
            {
                int x = edge + (int)((Width - edge * 2) * (dates[i] - first).TotalMinutes / duration);
                int radius = S(6);
                using (var fill = new SolidBrush(i == 1 ? Palette.Amber : Palette.Surface))
                using (var edgePen = new Pen(i == 1 ? Palette.AmberTint : Palette.Muted, S(2)))
                {
                    g.FillEllipse(fill, x - radius, y - radius, radius * 2, radius * 2);
                    g.DrawEllipse(edgePen, x - radius, y - radius, radius * 2, radius * 2);
                }
                Palette.Write(g, dates[i].LocalDateTime.ToString("HH:mm", Strings.Culture), Palette.Font(10, i == 1, "JetBrains Mono"),
                    new Rectangle(Math.Max(0, Math.Min(Width - S(39), x - S(20))), y + S(10), S(39), S(15)),
                    i == 1 ? Palette.Accent : Palette.Muted, StringAlignment.Center);
            }
        }
    }

    private sealed class RangeTrack : Control
    {
        public int Low { get; set; }
        public int High { get; set; }
        public event EventHandler Changed;
        private int dragging;
        private int dragLimit;
        private int Limit => dragging != 0 ? dragLimit : Math.Max(60, ((High + 59) / 60) * 60);
        private int X(int value) => S(10) + value * (Width - S(20)) / Limit;

        public RangeTrack()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            AccessibleName = Strings.Get("Random interval range");
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Palette.PaintControlBackground(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int y = S(20);
            using (var track = new Pen(Palette.Raised, S(4)))
            using (var active = new Pen(Palette.Accent, S(4)))
            {
                g.DrawLine(track, X(0), y, X(Limit), y);
                g.DrawLine(active, X(Low), y, X(High), y);
            }
            foreach (int value in new[] { Low, High })
            {
                int r = S(8);
                using (var edge = new Pen(Palette.Accent, S(3)))
                using (var fill = new SolidBrush(Palette.Text))
                {
                    g.FillEllipse(fill, X(value) - r, y - r, 2 * r, 2 * r);
                    g.DrawEllipse(edge, X(value) - r, y - r, 2 * r, 2 * r);
                }
            }
            for (int i = 0; i < 5; i++)
                Palette.Write(g, i == 4 ? Strings.Format("{0} min", Limit) : (i * Limit / 4).ToString(Strings.Culture),
                    Palette.Font(10, false, Strings.IsChinese ? "Inter" : "JetBrains Mono"),
                    new Rectangle(Math.Max(0, X(i * Limit / 4) - S(i == 4 ? 39 : 9)), S(33), S(44), S(21)), Palette.Muted);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;
            dragLimit = Limit;
            dragging = Math.Abs(e.X - X(Low)) <= Math.Abs(e.X - X(High)) ? 1 : 2;
            Capture = true;
            MoveThumb(e.X);
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging > 0) MoveThumb(e.X); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging == 0) return;
            MoveThumb(e.X);
            dragging = 0;
            Capture = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        private void MoveThumb(int x)
        {
            var value = Math.Max(1, Math.Min(Limit, (int)Math.Round((x - S(10)) * (double)Limit / Math.Max(1, Width - S(20)))));
            if (dragging == 1) Low = Math.Min(High, value);
            else High = Math.Min(10080, Math.Max(Low, value));
            Invalidate();
        }
    }
}
