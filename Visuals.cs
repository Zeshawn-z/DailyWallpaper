using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BingWallpapers;

internal static class Theme
{
    public static readonly Color Paper = Color.FromArgb(245, 246, 241);
    public static readonly Color Rail = Color.FromArgb(234, 238, 232);
    public static readonly Color Ink = Color.FromArgb(24, 39, 32);
    public static readonly Color Muted = Color.FromArgb(86, 105, 95);
    public static readonly Color Line = Color.FromArgb(218, 226, 219);
    public static readonly Color Accent = Color.FromArgb(28, 86, 69);
    public static readonly Color Tint = Color.FromArgb(214, 232, 220);
    public static readonly Color White = Color.FromArgb(252, 253, 249);
    public static readonly Color Alert = Color.FromArgb(151, 55, 40);

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

internal sealed class ActionButton : Button
{
    public string Glyph { get; set; }
    public Color Fill { get; set; } = Theme.White;
    public Color HoverFill { get; set; } = Theme.Tint;
    public Color Stroke { get; set; } = Theme.Line;
    public bool AlignLeft { get; set; }
    public int Unit { get; set; } = 1;
    private bool hovered;

    public ActionButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        ForeColor = Theme.Ink;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (BackColor != Color.Transparent)
            e.Graphics.Clear(Parent?.BackColor ?? Theme.Paper);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var shape = Theme.Rounded(bounds, 8 * Unit))
        using (var fill = new SolidBrush(Enabled && (hovered || Focused) ? HoverFill : Fill))
        using (var border = new Pen(Focused ? Theme.Accent : Stroke))
        {
            e.Graphics.FillPath(fill, shape);
            e.Graphics.DrawPath(border, shape);
        }

        Color ink = Enabled ? ForeColor : Fill == Theme.Accent ? Color.FromArgb(194, 214, 206) : Theme.Muted;
        int inset = 12 * Unit;
        var text = new Rectangle(inset, 0, Width - 2 * inset, Height);
        if (Glyph != null)
        {
            var size = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            int iconX = AlignLeft ? inset : Math.Max(inset, (Width - size.Width - 25 * Unit) / 2);
            DrawGlyph(e.Graphics, Glyph, new Rectangle(iconX, (Height - 16 * Unit) / 2, 16 * Unit, 16 * Unit), ink);
            text.X = iconX + 25 * Unit;
            text.Width = Width - text.X - 7 * Unit;
        }
        Theme.Text(e.Graphics, Text, Font, text, ink, AlignLeft || Glyph != null ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    private static void DrawGlyph(Graphics graphics, string glyph, Rectangle b, Color color)
    {
        var state = graphics.Save();
        graphics.TranslateTransform(b.X, b.Y);
        graphics.ScaleTransform(b.Width / 16f, b.Height / 16f);
        using (var pen = new Pen(color, 1.25f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            switch (glyph)
            {
                case "image":
                    graphics.DrawRectangle(pen, 1.5f, 2.5f, 13, 11);
                    graphics.DrawLines(pen, new[] { new Point(2, 11), new Point(5, 8), new Point(8, 11), new Point(11, 7), new Point(14, 10) });
                    graphics.DrawEllipse(pen, 10, 4, 1.5f, 1.5f);
                    break;
                case "folder":
                    graphics.DrawPolygon(pen, new[] { new Point(1, 4), new Point(6, 4), new Point(8, 6), new Point(15, 6), new Point(15, 13), new Point(1, 13) });
                    break;
                case "settings":
                    graphics.DrawLine(pen, 1, 4, 15, 4);
                    graphics.DrawLine(pen, 1, 8, 15, 8);
                    graphics.DrawLine(pen, 1, 12, 15, 12);
                    graphics.DrawLine(pen, 5, 2, 5, 6);
                    graphics.DrawLine(pen, 11, 6, 11, 10);
                    graphics.DrawLine(pen, 6, 10, 6, 14);
                    break;
                case "refresh":
                    graphics.DrawArc(pen, 2, 2, 12, 12, -41, 304);
                    graphics.DrawLines(pen, new[] { new Point(15, 2), new Point(15, 7), new Point(10, 7) });
                    break;
                case "desktop":
                    graphics.DrawRectangle(pen, 1, 2, 14, 9);
                    graphics.DrawLine(pen, 8, 11, 8, 14);
                    graphics.DrawLine(pen, 5, 14, 11, 14);
                    break;
                case "tray":
                    graphics.DrawLines(pen, new[] { new Point(1, 8), new Point(1, 14), new Point(15, 14), new Point(15, 8) });
                    graphics.DrawLines(pen, new[] { new Point(5, 6), new Point(8, 9), new Point(11, 6) });
                    graphics.DrawLine(pen, 8, 2, 8, 9);
                    break;
            }
        }
        graphics.Restore(state);
    }
}

internal sealed class PhotoPanel : Panel
{
    private Bitmap photo;
    private string title = "";
    private string detail = "";
    private string caption = "";
    private readonly Font titleFont;
    private readonly Font bodyFont;
    private readonly Font captionFont;
    public Func<int, int> S { get; set; } = value => value;
    public bool Large { get; }

    public PhotoPanel(bool large)
    {
        Large = large;
        titleFont = Theme.Font(large ? 28 : 13, FontStyle.Bold);
        bodyFont = Theme.Font(10);
        captionFont = Theme.Font(10, FontStyle.Bold);
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Paper;
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
        using (var shape = Theme.Rounded(bounds, S(Large ? 19 : 12)))
        using (var gradient = new LinearGradientBrush(bounds, Color.FromArgb(27, 63, 54), Color.FromArgb(78, 119, 93), 18f))
        {
            g.SetClip(shape);
            if (photo != null)
                Theme.Cover(g, photo, bounds);
            else
            {
                g.FillRectangle(gradient, bounds);
                using (var line = new Pen(Color.FromArgb(24, 231, 245, 203), 1))
                {
                    int horizon = Height * 2 / 3;
                    for (int row = 0; row < 9; row++)
                    {
                        int y = horizon + row * S(16);
                        g.DrawBezier(line, 0, y, Width * .50f, y - Height * .82f,
                            Width * .67f, y + Height * .30f, Width, y - Height * .48f);
                    }
                }
            }
            if (Large)
            {
                var shade = new Rectangle(0, Height / 4, Width, Height - Height / 4);
                using (var shadow = new LinearGradientBrush(shade, Color.Transparent, Color.FromArgb(230, 8, 20, 15), 90f))
                    g.FillRectangle(shadow, shade);

                int left = S(27);
                int textWidth = Math.Max(1, Width - S(55));
                Theme.Text(g, caption, captionFont, new Rectangle(left, Height - S(132), textWidth, S(24)), Color.FromArgb(216, 241, 210));
                Theme.Text(g, title, titleFont, new Rectangle(left, Height - S(105), textWidth, S(50)), Color.White);
                Theme.Text(g, detail, bodyFont, new Rectangle(left, Height - S(47), Math.Max(1, textWidth - S(154)), S(24)), Color.WhiteSmoke);
            }
            else if (photo == null)
                Theme.Text(g, title, titleFont, bounds, Color.WhiteSmoke, TextFormatFlags.HorizontalCenter);
            g.ResetClip();
        }
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
    private readonly Font dateFont = Theme.Font(9, FontStyle.Bold);

    public Thumbnail(Wallpaper item)
    {
        Item = item;
        Text = item.DisplayDate;
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
        graphic.Clear(Theme.Paper);
        graphic.SmoothingMode = SmoothingMode.AntiAlias;
        var frame = new Rectangle(1, 1, Width - 3, Height - 3);
        int textHeight = dateFont.Height + 10;
        using (var shape = Theme.Rounded(frame, 8))
        using (var fill = new SolidBrush(Selected ? Theme.Tint : Theme.White))
        using (var placeholder = new SolidBrush(Theme.Rail))
        using (var line = new Pen(Selected || Focused ? Theme.Accent : Theme.Line, Selected ? 2 : 1))
        {
            graphic.FillPath(fill, shape);
            graphic.SetClip(shape);
            var photoArea = new Rectangle(frame.X, frame.Y, frame.Width, Math.Max(1, frame.Height - textHeight));
            if (photo != null)
                Theme.Cover(graphic, photo, photoArea);
            else
                graphic.FillRectangle(placeholder, photoArea);
            graphic.ResetClip();
            graphic.DrawPath(line, shape);
            Theme.Text(graphic, Text, dateFont,
                new Rectangle(frame.X, frame.Bottom - textHeight, frame.Width, textHeight),
                Selected ? Theme.Accent : Theme.Muted, TextFormatFlags.HorizontalCenter);
        }
    }

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
