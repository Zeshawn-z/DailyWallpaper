using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using UI = AntdUI;

namespace BingWallpapers;

internal static class Palette
{
    public static readonly Color Window = Color.FromArgb(21, 20, 18);
    public static readonly Color Canvas = Color.FromArgb(16, 15, 13);
    public static readonly Color Surface = Color.FromArgb(27, 25, 22);
    public static readonly Color Hover = Color.FromArgb(36, 33, 29);
    public static readonly Color Raised = Color.FromArgb(46, 42, 37);
    public static readonly Color Line = Color.FromArgb(53, 48, 42);
    public static readonly Color Text = Color.FromArgb(242, 237, 229);
    public static readonly Color Subtle = Color.FromArgb(182, 174, 162);
    public static readonly Color Muted = Color.FromArgb(154, 146, 135);
    public static readonly Color Amber = Color.FromArgb(240, 176, 74);
    public static readonly Color AmberHover = Color.FromArgb(255, 193, 99);
    public static readonly Color AmberTint = Color.FromArgb(52, 41, 23);
    public static readonly Color OnAmber = Color.FromArgb(27, 18, 6);
    public static readonly Color Blue = Color.FromArgb(124, 198, 212);
    public static readonly Color Error = Color.FromArgb(232, 118, 108);

    private static readonly PrivateFontCollection fonts = new PrivateFontCollection();
    private static readonly Dictionary<(string Face, int Pixels, bool Bold), Font> type =
        new Dictionary<(string, int, bool), Font>();
    private static readonly Dictionary<(string Name, int Size, int Color), Bitmap> icons =
        new Dictionary<(string, int, int), Bitmap>();
    public static float Scale { get; private set; } = 1;
    public static int Px(int logical) => (int)Math.Round(logical * Scale);

    public static void Configure(float scale)
    {
        Scale = scale;
        if (fonts.Families.Length == 0)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Fonts");
            foreach (string file in new[] { "Inter.ttf", "Inter-Bold.ttf", "InstrumentSerif.ttf", "JetBrainsMono.ttf" })
            {
                string font = Path.Combine(path, file);
                if (File.Exists(font))
                    fonts.AddFontFile(font);
            }
        }
        UI.Config.Mode = UI.TMode.Dark;
        UI.Style.SetPrimary(Amber);
        UI.Style.Set(UI.Colour.PrimaryColor, OnAmber);
        UI.Style.Set(UI.Colour.Text, Text);
        UI.Style.Set(UI.Colour.TextSecondary, Subtle);
        UI.Style.Set(UI.Colour.TextTertiary, Muted);
        UI.Style.Set(UI.Colour.BgBase, Window);
        UI.Style.Set(UI.Colour.BgContainer, Surface);
        UI.Style.Set(UI.Colour.BgElevated, Raised);
        UI.Style.Set(UI.Colour.BgLayout, Canvas);
        UI.Style.Set(UI.Colour.HoverBg, Hover);
        UI.Style.Set(UI.Colour.BorderColor, Line);
        UI.Style.Set(UI.Colour.DefaultBorder, Line);
    }

    public static Font Font(int pixels = 14, bool bold = false, string face = "Inter")
    {
        var key = (face, Px(pixels), bold);
        if (type.TryGetValue(key, out var font))
            return font;
        string fallback = face == "Instrument Serif" ? "Georgia" : face == "JetBrains Mono" ? "Consolas" : "Segoe UI";
        var family = fonts.Families.FirstOrDefault(value => value.Name == face) ?? new FontFamily(fallback);
        var style = bold && family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
        return type[key] = new Font(family, key.Item2, style, GraphicsUnit.Pixel);
    }

    public static Bitmap Icon(string name, int logicalSize, Color color)
    {
        if (name == null)
            return null;
        int size = Px(logicalSize);
        var key = (name, size, color.ToArgb());
        if (icons.TryGetValue(key, out var image))
            return image;
        string svg = DesignIcons.Svg[name].Replace("currentColor", $"#{color.R:X2}{color.G:X2}{color.B:X2}");
        var source = UI.Svg.SvgDocument.FromSvg<UI.Svg.SvgDocument>(svg);
        return icons[key] = source.Draw(size, size);
    }

    public static GraphicsPath Round(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        radius = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f);
        if (radius < 1)
        {
            path.AddRectangle(bounds);
            return path;
        }
        float d = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Box(Graphics g, RectangleF bounds, Color fill, int radius, Color? border = null)
    {
        using (var path = Round(bounds, Px(radius)))
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);
            if (border.HasValue)
                using (var pen = new Pen(border.Value)) g.DrawPath(pen, path);
        }
    }

    public static void Write(Graphics g, string text, Font font, RectangleF bounds, Color color,
        StringAlignment alignment = StringAlignment.Near)
    {
        if (string.IsNullOrEmpty(text) || bounds.Width <= 0)
            return;
        using (var brush = new SolidBrush(color))
        using (var format = new StringFormat(StringFormat.GenericTypographic))
        {
            format.FormatFlags |= StringFormatFlags.NoWrap;
            format.LineAlignment = StringAlignment.Center;
            format.Alignment = alignment;
            format.Trimming = StringTrimming.EllipsisCharacter;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.DrawString(text, font, brush, bounds, format);
        }
    }

    public static void Photo(Graphics g, Image image, Rectangle area, int radius, bool contain = false)
    {
        var state = g.Save();
        using (var path = Round(area, Px(radius)))
        {
            g.SetClip(path, CombineMode.Intersect);
            if (image == null)
            {
                using (var gradient = new LinearGradientBrush(area, Raised, Surface, 75))
                    g.FillRectangle(gradient, area);
            }
            else
            {
                if (contain)
                    g.FillRectangle(Brushes.Black, area);
                float ratio = contain ? Math.Min((float)area.Width / image.Width, (float)area.Height / image.Height)
                    : Math.Max((float)area.Width / image.Width, (float)area.Height / image.Height);
                float width = image.Width * ratio, height = image.Height * ratio;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(image, area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
            }
        }
        g.Restore(state);
    }
}

internal sealed class InkLabel : Control
{
    public StringAlignment Align { get; set; }
    public InkLabel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e) =>
        Palette.Write(e.Graphics, Text, Font, ClientRectangle, ForeColor, Align);
}

internal sealed class FramePanel : Panel
{
    public Color Fill { get; set; }
    public Color? Border { get; set; }
    public int Radius { get; set; }

    public FramePanel(Color fill, int radius, Color? border = null)
    {
        Fill = fill;
        BackColor = fill;
        Radius = radius;
        Border = border;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? Fill);
        if (Width > 1 && Height > 1)
            Palette.Box(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), Fill, Radius, Border);
    }
}

internal enum ButtonLook { Plain, Soft, Outline, Primary, Chip, Nav, Glass, Choice }

internal sealed class FrameButton : Button
{
    public ButtonLook Look { get; set; }
    public string Glyph { get; set; }
    public string EndGlyph { get; set; }
    public int EndGlyphSize { get; set; } = 16;
    public string Subtitle { get; set; }
    public int GlyphSize { get; set; } = 16;
    public bool Selected { get; set; }
    public int Radius { get; set; } = 10;
    public Color? GlyphColor { get; set; }
    public Color? SoftFill { get; set; }
    private bool hover;

    public FrameButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        ForeColor = Palette.Text;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color foreground = Enabled ? ForeColor : Palette.Muted;
        Color fill = Color.Transparent;
        Color? border = null;
        switch (Look)
        {
            case ButtonLook.Primary:
                fill = !Enabled ? Palette.Raised : hover ? Palette.AmberHover : Palette.Amber;
                foreground = Enabled ? Palette.OnAmber : Palette.Subtle;
                break;
            case ButtonLook.Soft:
                fill = hover ? Color.FromArgb(62, 55, 46) : SoftFill ?? Palette.Raised;
                break;
            case ButtonLook.Outline:
                fill = hover ? Palette.Hover : Palette.Canvas;
                border = Palette.Line;
                break;
            case ButtonLook.Chip:
                fill = Selected ? Palette.AmberTint : Palette.Hover;
                border = Selected ? Color.FromArgb(121, 88, 43) : Palette.Line;
                foreground = Selected ? Palette.Amber : Palette.Subtle;
                break;
            case ButtonLook.Nav:
                fill = Selected || hover ? Palette.Hover : Color.Transparent;
                foreground = Selected ? Palette.Text : Palette.Subtle;
                break;
            case ButtonLook.Glass:
                fill = Color.FromArgb(hover ? 218 : 155, Palette.Canvas);
                border = Color.FromArgb(103, Palette.Subtle);
                break;
            case ButtonLook.Choice:
                fill = Selected || hover ? Palette.Hover : Color.Transparent;
                border = Selected ? Color.FromArgb(121, 88, 43) : (Color?)null;
                break;
            default:
                if (hover)
                    fill = Palette.Hover;
                break;
        }
        if (Focused && ShowFocusCues)
            border = Palette.Amber;
        if (fill.A > 0 || border.HasValue)
            Palette.Box(g, new Rectangle(0, 0, Width - 1, Height - 1), fill, Radius, border);

        Color iconColor = Enabled ? GlyphColor ?? (Selected && (Look == ButtonLook.Nav || Look == ButtonLook.Choice)
            ? Palette.Amber : foreground) : Palette.Muted;
        var icon = Palette.Icon(Glyph, GlyphSize, iconColor);
        var end = Palette.Icon(EndGlyph, EndGlyphSize, iconColor);
        if (icon == null && end == null)
        {
            Palette.Write(g, Text, Font, ClientRectangle, foreground, StringAlignment.Center);
            return;
        }
        bool hasText = Text.Length > 0;
        int gap = Palette.Px(Look == ButtonLook.Nav || Look == ButtonLook.Choice ? 12 : 7);
        int lead = icon == null ? 0 : icon.Width + (hasText ? gap : 0);
        int tail = end == null ? 0 : end.Width + (hasText ? gap : 0);
        int natural = hasText ? (int)Math.Ceiling(g.MeasureString(Text, Font, int.MaxValue, StringFormat.GenericTypographic).Width) : 0;
        int start = Look == ButtonLook.Nav || Look == ButtonLook.Choice ? Palette.Px(13) :
            Math.Max(Palette.Px(3), (Width - lead - natural - tail) / 2);
        int textWidth = Math.Max(0, Math.Min(natural + Palette.Px(2), Width - start - lead - tail - Palette.Px(3)));
        if (Look == ButtonLook.Choice)
            textWidth = Math.Max(0, Width - start - lead - Palette.Px(10));
        if (icon != null)
            g.DrawImageUnscaled(icon, start, (Height - icon.Height) / 2);
        if (end != null)
            g.DrawImageUnscaled(end, start + lead + textWidth + (hasText ? gap : 0), (Height - end.Height) / 2);
        if (hasText)
        {
            int textHeight = Subtitle == null ? Height : Height / 2 + Palette.Px(5);
            Palette.Write(g, Text, Font, new Rectangle(start + lead, Subtitle == null ? 0 : Palette.Px(3),
                textWidth, textHeight), foreground);
            if (Subtitle != null)
                Palette.Write(g, Subtitle, Palette.Font(12), new Rectangle(start + lead, Height / 2,
                    textWidth, Height / 2 - Palette.Px(3)), Palette.Muted);
        }
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}

internal sealed class PhotoView : Panel
{
    private Bitmap image;
    public int Radius { get; set; } = 12;
    public bool Hero { get; set; }
    public bool Contain { get; set; }
    public Size ImageSize => image?.Size ?? Size.Empty;
    public bool Loaded => image != null;
    public PhotoView()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Palette.Canvas;
    }
    public void SetImage(Bitmap value)
    {
        image?.Dispose();
        image = value;
        Invalidate(true);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Palette.Canvas);
        if (Width < 2 || Height < 2)
            return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Palette.Photo(g, image, ClientRectangle, Radius, Contain);
        if (Hero)
        {
            using (var mask = Palette.Round(ClientRectangle, Palette.Px(Radius)))
            using (var shadow = new LinearGradientBrush(ClientRectangle, Color.FromArgb(139, 10, 8, 6),
                Color.FromArgb(243, 15, 11, 7), 90))
            {
                var state = g.Save();
                g.SetClip(mask, CombineMode.Intersect);
                shadow.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { Color.FromArgb(115, 10, 8, 6), Color.FromArgb(0, 10, 8, 6),
                        Color.FromArgb(12, 14, 10, 6), Color.FromArgb(233, 14, 10, 6) },
                    Positions = new[] { 0f, .27f, .49f, 1f }
                };
                g.FillRectangle(shadow, ClientRectangle);
                g.Restore(state);
            }
        }
    }
    protected override void Dispose(bool disposing) { if (disposing) image?.Dispose(); base.Dispose(disposing); }
}

internal sealed class PhotoTile : Button
{
    public Wallpaper Wallpaper { get; }
    public SavedWallpaper Entry { get; }
    public bool Selected { get; set; }
    public bool Saved { get; set; }
    public bool OnDesktop { get; set; }
    public bool Missing { get; set; }
    public bool Requested { get; set; }
    public bool Strip { get; }
    public string Caption { get; }
    private Bitmap image;

    public PhotoTile(Wallpaper wallpaper, SavedWallpaper entry, string caption, bool strip)
    {
        Wallpaper = wallpaper;
        Entry = entry;
        Strip = strip;
        Caption = caption;
        AccessibleName = wallpaper.Title + ", " + caption;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public void SetImage(Bitmap value) { image?.Dispose(); image = value; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Palette.Canvas);
        if (Width < 8 || Height < 8)
            return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int px = Palette.Px(4);
        var photo = new Rectangle(px, px, Width - 2 * px,
            Math.Max(1, Height - Palette.Px(Strip ? 26 : 44) - px));
        Palette.Photo(g, image, photo, Strip ? 10 : 13);
        if (Selected || Focused && ShowFocusCues)
        {
            var edge = Rectangle.Inflate(photo, Palette.Px(3), Palette.Px(3));
            using (var frame = Palette.Round(edge, Palette.Px(Strip ? 13 : 15)))
            using (var pen = new Pen(Palette.Amber, Palette.Px(2))) g.DrawPath(pen, frame);
        }

        string glyph = Missing ? "warning" : OnDesktop ? "desktop" : Entry?.Favorite == true ? "heart" : Strip && Saved ? "check" : null;
        if (glyph != null)
        {
            var marker = new Rectangle(photo.Right - Palette.Px(25), photo.Top + Palette.Px(6), Palette.Px(20), Palette.Px(20));
            Palette.Box(g, marker, Color.FromArgb(206, Palette.Canvas), 10);
            var badge = Palette.Icon(glyph, 12, glyph == "check" ? Palette.Blue : Palette.Amber);
            g.DrawImageUnscaled(badge, marker.Left + (marker.Width - badge.Width) / 2, marker.Top + (marker.Height - badge.Height) / 2);
        }
        if (!Strip)
            Palette.Write(g, Wallpaper.Title, Palette.Font(13, true), new Rectangle(px, photo.Bottom + Palette.Px(6),
                photo.Width, Palette.Px(18)), Palette.Text);
        Palette.Write(g, Caption, Strip ? Palette.Font(11, Selected, "JetBrains Mono") : Palette.Font(12),
            new Rectangle(px, photo.Bottom + Palette.Px(Strip ? 7 : 23), photo.Width, Palette.Px(17)),
            Strip && Selected ? Palette.Amber : Palette.Muted);
    }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void Dispose(bool disposing) { if (disposing) image?.Dispose(); base.Dispose(disposing); }
}

internal sealed class ToggleControl : CheckBox
{
    public ToggleControl()
    {
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Palette.Box(g, new Rectangle(0, 0, Width - 1, Height - 1), Checked ? Palette.Amber : Palette.Raised,
            12, Focused && ShowFocusCues ? Palette.Blue : (Color?)null);
        int inset = Palette.Px(4), knob = Height - inset * 2;
        using (var fill = new SolidBrush(Checked ? Palette.OnAmber : Palette.Subtle))
            g.FillEllipse(fill, Checked ? Width - inset - knob : inset, inset, knob, knob);
    }
}
