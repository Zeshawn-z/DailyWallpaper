using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace BingWallpapers;

internal sealed class ThemedScrollBar : Control
{
    private int offset, dragOffset;
    private bool dragging, hover;
    public static int RailWidth => Palette.Px(14);
    public int MaximumOffset { get; private set; }
    public int ViewportHeight { get; private set; }
    public int LineStep { get; private set; }
    public event EventHandler ValueChanged;

    public ThemedScrollBar()
    {
        AccessibleName = Strings.Get("Vertical scroll bar");
        AccessibleRole = AccessibleRole.ScrollBar;
        TabStop = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public int Value
    {
        get => offset;
        set
        {
            int next = Math.Max(0, Math.Min(MaximumOffset, value));
            if (next == offset)
                return;
            offset = next;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetRange(int contentHeight, int viewportHeight, int lineStep)
    {
        ViewportHeight = viewportHeight;
        MaximumOffset = Math.Max(0, contentHeight - viewportHeight);
        LineStep = Math.Max(1, lineStep);
        Value = offset;
        bool needed = MaximumOffset > 0 && viewportHeight > 0;
        if (!needed && Capture)
            Capture = false;
        Visible = needed;
        Invalidate();
    }

    private Rectangle Track
    {
        get
        {
            int inset = Palette.Px(6), width = Math.Max(1, Palette.Px(4));
            return new Rectangle((Width - width) / 2, inset, width, Math.Max(0, Height - 2 * inset));
        }
    }

    private Rectangle Thumb
    {
        get
        {
            var track = Track;
            int total = ViewportHeight + MaximumOffset;
            if (total == 0 || track.Height == 0)
                return Rectangle.Empty;
            int length = Math.Min(track.Height, Math.Max(Palette.Px(24),
                (int)Math.Round(track.Height * (double)ViewportHeight / total)));
            int y = MaximumOffset == 0 ? 0 :
                (int)Math.Round((track.Height - length) * (double)Value / MaximumOffset);
            return new Rectangle(track.Left, track.Top + y, track.Width, length);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent.BackColor);
        var track = Track;
        if (MaximumOffset == 0 || track.Height == 0)
            return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Palette.Box(e.Graphics, track, Palette.Line, 2);
        Palette.Box(e.Graphics, Thumb, dragging ? Palette.Accent : hover ? Palette.Subtle : Palette.Muted, 2);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || MaximumOffset == 0)
            return;
        var thumb = Thumb;
        if (e.Y < thumb.Top || e.Y >= thumb.Bottom)
        {
            Value += e.Y < thumb.Top ? -ViewportHeight : ViewportHeight;
            return;
        }
        dragOffset = e.Y - thumb.Top;
        dragging = true;
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging)
            return;
        var track = Track;
        int travel = track.Height - Thumb.Height;
        if (travel > 0)
            Value = (int)Math.Round((e.Y - track.Top - dragOffset) * (double)MaximumOffset / travel);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && dragging)
            Capture = false;
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (Capture || !dragging)
            return;
        dragging = false;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hover = false;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollWheel(e);
        base.OnMouseWheel(e);
    }

    public void ScrollWheel(MouseEventArgs e)
    {
        if (MaximumOffset == 0 || e is HandledMouseEventArgs alreadyHandled && alreadyHandled.Handled)
            return;
        int lines = SystemInformation.MouseWheelScrollLines;
        int step = lines < 0 ? ViewportHeight : lines * LineStep;
        if (step == 0)
            return;
        Value -= (int)Math.Round(e.Delta / 120d * step);
        if (e is HandledMouseEventArgs handled)
            handled.Handled = true;
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new ScrollAccessibility(this);

    private sealed class ScrollAccessibility : ControlAccessibleObject
    {
        private readonly ThemedScrollBar bar;
        public ScrollAccessibility(ThemedScrollBar bar) : base(bar) => this.bar = bar;

        public override string Value
        {
            get => bar.Value.ToString(CultureInfo.InvariantCulture);
            set
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int next))
                    bar.Value = next;
            }
        }
    }
}
