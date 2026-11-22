using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using UI = AntdUI;

namespace BingWallpapers;

internal static class Palette
{
    public static readonly Color Canvas = Color.FromArgb(14, 17, 24);
    public static readonly Color Rail = Color.FromArgb(19, 23, 34);
    public static readonly Color Surface = Color.FromArgb(24, 29, 43);
    public static readonly Color Field = Color.FromArgb(18, 22, 33);
    public static readonly Color Ink = Color.FromArgb(242, 244, 251);
    public static readonly Color Muted = Color.FromArgb(169, 178, 201);
    public static readonly Color Line = Color.FromArgb(48, 56, 78);
    public static readonly Color Hover = Color.FromArgb(36, 44, 65);
    public static readonly Color Accent = Color.FromArgb(184, 177, 255);
    public static readonly Color AccentHover = Color.FromArgb(207, 202, 255);
    public static readonly Color OnAccent = Color.FromArgb(24, 20, 53);
    public static readonly Color Tint = Color.FromArgb(47, 45, 76);
    public static readonly Color Alert = Color.FromArgb(255, 158, 169);

    public static void Configure()
    {
        UI.Config.Mode = UI.TMode.Dark;
        UI.Style.SetPrimary(Accent);
        UI.Style.Set(UI.Colour.PrimaryColor, OnAccent);
        UI.Style.Set(UI.Colour.Text, Ink);
        UI.Style.Set(UI.Colour.TextSecondary, Muted);
        UI.Style.Set(UI.Colour.BgBase, Canvas);
        UI.Style.Set(UI.Colour.BgContainer, Surface);
        UI.Style.Set(UI.Colour.BgElevated, Surface);
        UI.Style.Set(UI.Colour.BgLayout, Canvas);
        UI.Style.Set(UI.Colour.HoverBg, Hover);
        UI.Style.Set(UI.Colour.BorderColor, Line);
        UI.Style.Set(UI.Colour.DefaultBorder, Line);
    }

    public static Font Font(float size, FontStyle style = FontStyle.Regular) =>
        new Font("Microsoft YaHei UI", size, style);

    public static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= radius * 2 || bounds.Height <= radius * 2)
        {
            path.AddRectangle(bounds);
            return path;
        }
        int diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Cover(Graphics graphics, Image image, Rectangle bounds)
    {
        var state = graphics.Save();
        graphics.SetClip(bounds, CombineMode.Intersect);
        float scale = Math.Max((float)bounds.Width / image.Width, (float)bounds.Height / image.Height);
        float width = image.Width * scale;
        float height = image.Height * scale;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(image, bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);
        graphics.Restore(state);
    }

    public static void Text(Graphics graphics, string value, Font font, Rectangle bounds, Color color,
        TextFormatFlags additional = TextFormatFlags.Left)
    {
        TextRenderer.DrawText(graphics, value, font, bounds, color,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | additional);
    }
}

internal sealed class PhotoPanel : Panel
{
    private Bitmap photo;
    private string title = "";
    private string detail = "";
    private string caption = "";
    private readonly Font titleFont;
    private readonly Font bodyFont = Palette.Font(9.5f);
    private readonly Font captionFont = Palette.Font(9, FontStyle.Bold);
    public Func<int, int> S { get; set; } = value => value;
    public bool Large { get; }

    public PhotoPanel(bool large)
    {
        Large = large;
        titleFont = Palette.Font(large ? 30 : 11, FontStyle.Bold);
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Palette.Canvas;
    }

    public void Describe(string caption, string title, string detail)
    {
        this.caption = caption;
        this.title = title;
        this.detail = detail;
        AccessibleName = title;
        AccessibleDescription = detail;
        Invalidate();
    }

    public void SetPhoto(Bitmap image)
    {
        photo?.Dispose();
        photo = image;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 4 || Height < 4)
            return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        var state = g.Save();
        using (var shape = Palette.Rounded(bounds, S(Large ? 18 : 10)))
        {
            g.SetClip(shape, CombineMode.Intersect);
            if (photo != null)
                Palette.Cover(g, photo, bounds);
            else
            {
                using (var gradient = new LinearGradientBrush(bounds, Palette.Surface, Palette.Field, 32f))
                    g.FillRectangle(gradient, bounds);
            }
            if (Large)
                PaintInformation(g);
            else if (photo == null)
                Palette.Text(g, title, titleFont, bounds, Palette.Muted, TextFormatFlags.HorizontalCenter);
            g.Restore(state);
            using (var edge = new Pen(Color.FromArgb(106, Palette.Line)))
                g.DrawPath(edge, shape);
        }
    }

    private void PaintInformation(Graphics g)
    {
        var shade = ClientRectangle;
        var clear = Color.FromArgb(0, 10, 12, 18);
        var dark = Color.FromArgb(245, 10, 12, 18);
        using (var shadow = new LinearGradientBrush(shade, clear, dark, 90f))
        {
            shadow.InterpolationColors = new ColorBlend
            {
                Colors = new[] { clear, clear, Color.FromArgb(105, 10, 12, 18), dark },
                Positions = new[] { 0f, .29f, .66f, 1f }
            };
            g.FillRectangle(shadow, shade);
        }

        int left = S(26);
        if (!string.IsNullOrEmpty(caption))
        {
            int size = TextRenderer.MeasureText(caption, captionFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            var badge = new Rectangle(left, S(22), size + S(23), S(29));
            using (var shape = Palette.Rounded(badge, S(8)))
            using (var fill = new SolidBrush(Color.FromArgb(219, 22, 25, 40)))
            using (var border = new Pen(Color.FromArgb(120, 212, 214, 240)))
            {
                g.FillPath(fill, shape);
                g.DrawPath(border, shape);
            }
            Palette.Text(g, caption, captionFont, badge, Color.White, TextFormatFlags.HorizontalCenter);
        }

        Palette.Text(g, title, titleFont, new Rectangle(left, Height - S(112), Width - left * 2, S(57)), Color.White);
        Palette.Text(g, detail, bodyFont, new Rectangle(left, Height - S(48), Width - left * 2, S(23)), Color.FromArgb(220, 226, 241));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            photo?.Dispose();
            titleFont.Dispose();
            bodyFont.Dispose();
            captionFont.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class Thumbnail : Button
{
    public Wallpaper Item { get; }
    public bool Selected { get; set; }
    private Bitmap photo;
    private bool hovered;
    private readonly Font dateFont = new Font("Segoe UI", 9f, FontStyle.Bold);

    public Thumbnail(Wallpaper item)
    {
        Item = item;
        Text = item.DisplayDate.Substring(5);
        AccessibleName = item.Title + " " + item.DisplayDate;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public void SetPhoto(Bitmap image)
    {
        photo?.Dispose();
        photo = image;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 5 || Height < 5)
            return;
        var graphic = e.Graphics;
        graphic.Clear(Palette.Canvas);
        graphic.SmoothingMode = SmoothingMode.AntiAlias;
        var frame = new Rectangle(1, 1, Width - 3, Height - 3);
        int textHeight = dateFont.Height + 9;
        bool focused = Focused || Selected;
        using (var shape = Palette.Rounded(frame, 9))
        using (var fill = new SolidBrush(Selected ? Palette.Tint : hovered ? Palette.Hover : Palette.Surface))
        using (var line = new Pen(focused ? Palette.Accent : Palette.Line, focused ? 2 : 1))
        {
            graphic.FillPath(fill, shape);
            if (photo != null)
            {
                var state = graphic.Save();
                graphic.SetClip(shape, CombineMode.Intersect);
                var area = new Rectangle(frame.X, frame.Y, frame.Width, Math.Max(1, frame.Height - textHeight));
                Palette.Cover(graphic, photo, area);
                graphic.Restore(state);
            }
            graphic.DrawPath(line, shape);
            Palette.Text(graphic, Text, dateFont,
                new Rectangle(frame.X, frame.Bottom - textHeight, frame.Width, textHeight),
                Selected ? Palette.Accent : Palette.Muted, TextFormatFlags.HorizontalCenter);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            photo?.Dispose();
            dateFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
