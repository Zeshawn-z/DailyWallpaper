using System;
using System.Drawing;
using System.Windows.Forms;

namespace BingWallpapers;

internal sealed class ThemedScrollPanel : FramePanel
{
    private readonly ThemedScrollBar scrollbar = new ThemedScrollBar();
    private int contentHeight;
    public FramePanel Content { get; }

    public ThemedScrollPanel(Color fill) : base(fill, 0)
    {
        Content = new FramePanel(fill, 0);
        Controls.Add(Content);
        Controls.Add(scrollbar);
        scrollbar.ValueChanged += (_, __) => Content.Top = -scrollbar.Value;
        Watch(Content, true);
    }

    public int ContentHeight
    {
        get => contentHeight;
        set
        {
            if (contentHeight == value)
                return;
            contentHeight = value;
            LayoutContent();
        }
    }

    public int ScrollOffset
    {
        get => scrollbar.Value;
        set => scrollbar.Value = value;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Content != null)
            LayoutContent();
    }

    private void LayoutContent()
    {
        int width = Width, height = Height;
        int railWidth = ThemedScrollBar.RailWidth;
        scrollbar.SetRange(contentHeight, height, Palette.Px(24));
        var contentBounds = new Rectangle(0, -scrollbar.Value,
            Math.Max(0, width - railWidth), Math.Max(contentHeight, height));
        if (Content.Bounds != contentBounds)
            Content.Bounds = contentBounds;
        var railBounds = new Rectangle(Math.Max(0, width - railWidth), 0, railWidth, height);
        if (scrollbar.Bounds != railBounds)
            scrollbar.Bounds = railBounds;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        scrollbar.ScrollWheel(e);
        base.OnMouseWheel(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (scrollbar.MaximumOffset > 0)
        {
            switch (keyData)
            {
                case Keys.PageUp: ScrollOffset -= Height; return true;
                case Keys.PageDown: ScrollOffset += Height; return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Watch(Control control, bool add)
    {
        if (add)
        {
            control.Enter += ShowFocusedControl;
            control.ControlAdded += WatchAdded;
            control.ControlRemoved += WatchRemoved;
        }
        else
        {
            control.Enter -= ShowFocusedControl;
            control.ControlAdded -= WatchAdded;
            control.ControlRemoved -= WatchRemoved;
        }
        foreach (Control child in control.Controls)
            Watch(child, add);
    }

    private void WatchAdded(object sender, ControlEventArgs e) => Watch(e.Control, true);
    private void WatchRemoved(object sender, ControlEventArgs e) => Watch(e.Control, false);

    private void ShowFocusedControl(object sender, EventArgs e)
    {
        var control = (Control)sender;
        if (!control.TabStop || scrollbar.MaximumOffset == 0)
            return;
        var bounds = RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
        if (bounds.Top < 0)
            ScrollOffset += bounds.Top;
        else if (bounds.Bottom > Height)
            ScrollOffset += bounds.Bottom - Height;
    }
}
