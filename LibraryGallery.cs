using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;
using ThumbnailSource = System.ValueTuple<string, long, long>;

namespace BingWallpapers;

internal sealed class LibraryGallery : Control
{
    internal sealed class Card
    {
        private string caption;
        public SavedWallpaper Entry { get; }
        public ThumbnailSource Source { get; }
        public bool Missing => Source.Item1 == null;
        public string Caption => caption ??= Strings.Date(Entry.Wallpaper.Day, "d MMM yyyy", "yyyy年M月d日");
        public Card(SavedWallpaper entry, FileInfo photo)
        {
            Entry = entry;
            if (photo != null)
                Source = (photo.FullName, photo.LastWriteTimeUtc.Ticks, photo.Length);
        }
    }

    private const int CacheLimit = 64;
    private readonly VScrollBar scrollbar = new VScrollBar { Visible = false, TabStop = false };
    private readonly Timer thumbnailDelay = new Timer { Interval = 65 };
    private readonly LinkedList<(ThumbnailSource Source, Bitmap Image)> recent = new LinkedList<(ThumbnailSource, Bitmap)>();
    private readonly Dictionary<ThumbnailSource, LinkedListNode<(ThumbnailSource Source, Bitmap Image)>> thumbnails =
        new Dictionary<ThumbnailSource, LinkedListNode<(ThumbnailSource, Bitmap)>>();
    private readonly Func<Wallpaper, string> title;
    private readonly Action<SavedWallpaper> select;
    private IReadOnlyList<Card> cards = Array.Empty<Card>();
    private Dictionary<string, int> byId = new Dictionary<string, int>(StringComparer.Ordinal);
    private CancellationTokenSource request;
    private (int Start, int End) wanted;
    private int columns = 1, cardWidth = 1, rowHeight = 1, selected = -1;
    private string currentId, emptyText;

    public LibraryGallery(Func<Wallpaper, string> title, Action<SavedWallpaper> select)
    {
        this.title = title;
        this.select = select;
        AccessibleName = Strings.Get("Library");
        TabStop = true;
        SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Controls.Add(scrollbar);
        scrollbar.ValueChanged += (_, __) => ViewportChanged();
        thumbnailDelay.Tick += async (_, __) => await LoadVisibleAsync();
    }

    public void SetItems(IReadOnlyList<Card> next, string wallpaperOnDesktop, string emptyMessage)
    {
        bool sameOrder = cards.Count == next.Count && !cards.Where((card, i) =>
            card.Entry.Wallpaper.Id != next[i].Entry.Wallpaper.Id).Any();
        cards = next;
        byId = new Dictionary<string, int>(cards.Count, StringComparer.Ordinal);
        for (int i = 0; i < cards.Count; i++)
            byId.Add(cards[i].Entry.Wallpaper.Id, i);
        currentId = wallpaperOnDesktop;
        emptyText = emptyMessage;
        selected = -1;
        if (!sameOrder)
            scrollbar.Value = 0;
        UpdateGeometry();
        ViewportChanged(true);
    }

    public void SelectEntry(SavedWallpaper entry)
    {
        int next = entry != null && byId.TryGetValue(entry.Wallpaper.Id, out int index) ? index : -1;
        if (selected == next)
            return;
        if (selected >= 0)
            Invalidate(CardBounds(selected));
        selected = next;
        if (selected >= 0)
        {
            Invalidate(CardBounds(selected));
            int child = selected - Range(0, Height).Start + 1;
            if (child >= 1 && selected < Range(0, Height).End)
                AccessibilityNotifyClients(AccessibleEvents.Selection, child);
        }
    }

    public Bitmap CopySelectedThumbnail() =>
        selected >= 0 && TryGetThumbnail(cards[selected].Source, out var image) && image != null ? new Bitmap(image) : null;

    private Rectangle View => new Rectangle(0, 0, Math.Max(0, Width - SystemInformation.VerticalScrollBarWidth), Height);
    private int Gap => Palette.Px(12);
    private int MaxOffset => Math.Max(0, scrollbar.Maximum - scrollbar.LargeChange + 1);

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateGeometry();
        ViewportChanged();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        ViewportChanged(true);
    }

    private void UpdateGeometry()
    {
        int width = View.Width;
        columns = Math.Max(1, Math.Min(3, width / (Palette.Px(100) + Gap)));
        cardWidth = Math.Max(1, width / columns - Gap);
        rowHeight = cardWidth * 9 / 16 + Palette.Px(54);
        int content = ((cards.Count + columns - 1) / columns) * rowHeight;
        int position = scrollbar.Value;
        scrollbar.SetBounds(width, 0, SystemInformation.VerticalScrollBarWidth, Height);
        scrollbar.LargeChange = Math.Max(1, Height);
        scrollbar.Maximum = Math.Max(0, content - 1);
        scrollbar.SmallChange = Math.Max(1, rowHeight / 3);
        scrollbar.Value = Math.Min(position, MaxOffset);
        scrollbar.Visible = content > Height;
    }

    private (int Start, int End) Range(int top, int bottom)
    {
        int first = ((scrollbar.Value + top) / rowHeight) * columns;
        int end = ((scrollbar.Value + bottom + rowHeight - 1) / rowHeight) * columns;
        return (Math.Min(first, cards.Count), Math.Min(end, cards.Count));
    }

    private Rectangle CardBounds(int index) => new Rectangle(index % columns * (cardWidth + Gap),
        index / columns * rowHeight - scrollbar.Value, cardWidth, rowHeight - Palette.Px(10));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Palette.Canvas);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (cards.Count == 0)
        {
            Palette.Write(g, emptyText, Palette.Font(14), new Rectangle(2, 0, View.Width, Palette.Px(65)), Palette.Muted);
            return;
        }
        g.SetClip(View, CombineMode.Intersect);
        var range = Range(e.ClipRectangle.Top, e.ClipRectangle.Bottom);
        for (int i = range.Start; i < range.End; i++)
        {
            Rectangle bounds = CardBounds(i);
            if (!e.ClipRectangle.IntersectsWith(bounds))
                continue;
            Card card = cards[i];
            int pad = Palette.Px(4);
            var photo = new Rectangle(bounds.X + pad, bounds.Y + pad, Math.Max(1, bounds.Width - 2 * pad),
                Math.Max(1, bounds.Height - Palette.Px(44) - pad));
            TryGetThumbnail(card.Source, out Bitmap preview);
            Palette.Photo(g, preview, photo, 13, resizing: true);
            if (i == selected)
            {
                using (var frame = Palette.Round(Rectangle.Inflate(photo, Palette.Px(3), Palette.Px(3)), Palette.Px(15)))
                using (var pen = new Pen(Palette.Accent, Palette.Px(2)))
                    g.DrawPath(pen, frame);
            }
            string glyph = card.Missing ? "warning" : card.Entry.Wallpaper.Id == currentId ? "desktop" :
                card.Entry.Favorite ? "heart" : null;
            if (glyph != null)
            {
                var marker = new Rectangle(photo.Right - Palette.Px(25), photo.Top + Palette.Px(6), Palette.Px(20), Palette.Px(20));
                Palette.Box(g, marker, Color.FromArgb(206, Palette.PhotoGlass), 10);
                var icon = Palette.Icon(glyph, 12, Palette.Amber);
                g.DrawImageUnscaled(icon, marker.Left + (marker.Width - icon.Width) / 2, marker.Top + (marker.Height - icon.Height) / 2);
            }
            Palette.Write(g, title(card.Entry.Wallpaper), Palette.Font(13, true),
                new Rectangle(photo.Left, photo.Bottom + Palette.Px(6), photo.Width, Palette.Px(18)), Palette.Text);
            Palette.Write(g, card.Caption, Palette.Font(12),
                new Rectangle(photo.Left, photo.Bottom + Palette.Px(23), photo.Width, Palette.Px(17)), Palette.Muted);
        }
    }

    private int HitCard(Point point)
    {
        if (!View.Contains(point))
            return -1;
        int column = point.X / (cardWidth + Gap);
        int index = (point.Y + scrollbar.Value) / rowHeight * columns + column;
        return column < columns && index < cards.Count && CardBounds(index).Contains(point) ? index : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = HitCard(e.Location) >= 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;
        Focus();
        int index = HitCard(e.Location);
        if (index >= 0)
            select(cards[index].Entry);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int lines = SystemInformation.MouseWheelScrollLines;
        int step = lines < 0 ? Height : scrollbar.SmallChange * lines;
        ScrollTo(scrollbar.Value - (int)Math.Round(e.Delta / 120d * step));
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys key)
    {
        if (cards.Count == 0 || !Focused)
            return base.ProcessCmdKey(ref msg, key);
        int page = columns * Math.Max(1, Height / rowHeight);
        int index = Math.Max(0, selected);
        switch (key)
        {
            case Keys.Left: index--; break;
            case Keys.Right: index++; break;
            case Keys.Up: index -= columns; break;
            case Keys.Down: index += columns; break;
            case Keys.PageUp: index -= page; break;
            case Keys.PageDown: index += page; break;
            case Keys.Home: index = 0; break;
            case Keys.End: index = cards.Count - 1; break;
            default: return base.ProcessCmdKey(ref msg, key);
        }
        index = Math.Max(0, Math.Min(cards.Count - 1, index));
        var bounds = CardBounds(index);
        if (bounds.Top < 0)
            ScrollTo(scrollbar.Value + bounds.Top);
        else if (bounds.Bottom > Height)
            ScrollTo(scrollbar.Value + bounds.Bottom - Height);
        if (index != selected)
            select(cards[index].Entry);
        return true;
    }

    private void ScrollTo(int position) => scrollbar.Value = Math.Max(0, Math.Min(MaxOffset, position));

    private void ViewportChanged(bool reset = false)
    {
        if (Disposing || IsDisposed)
            return;
        Invalidate();
        var range = Visible && Height > 0 ? Range(0, Height) : (0, 0);
        if (!reset && wanted == range)
            return;
        wanted = range;
        request?.Cancel();
        thumbnailDelay.Stop();
        if (range.Item1 < range.Item2)
            thumbnailDelay.Start();
    }

    private bool TryGetThumbnail(ThumbnailSource source, out Bitmap image)
    {
        if (source.Item1 != null && thumbnails.TryGetValue(source, out var node))
        {
            recent.Remove(node);
            recent.AddFirst(node);
            image = node.Value.Image;
            return true;
        }
        image = null;
        return false;
    }

    private async Task LoadVisibleAsync()
    {
        if (request != null)
            return;
        thumbnailDelay.Stop();
        var (start, end) = wanted;
        var nearby = Enumerable.Range(start, end - start)
            .Concat(Enumerable.Range(end, Math.Min(columns, cards.Count - end)))
            .Concat(Enumerable.Range(Math.Max(0, start - columns), Math.Min(columns, start)));
        var pending = nearby.Select(i => (Index: i, Source: cards[i].Source))
            .Where(item => item.Source.Item1 != null && !thumbnails.ContainsKey(item.Source)).ToArray();
        if (pending.Length == 0)
            return;
        using (var source = new CancellationTokenSource())
        {
            request = source;
            try { await Task.WhenAll(LoadBatchAsync(pending, 0, source.Token), LoadBatchAsync(pending, 1, source.Token)); }
            finally { request = null; }
        }
    }

    private async Task LoadBatchAsync((int Index, ThumbnailSource Source)[] pending, int start, CancellationToken token)
    {
        for (int n = start; n < pending.Length && !token.IsCancellationRequested; n += 2)
        {
            var (index, source) = pending[n];
            Bitmap thumbnail = null;
            try { thumbnail = await Task.Run(() => LibraryThumbnails.Read(source, token), token); }
            catch (OperationCanceledException) { return; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                error is ArgumentException || error is ExternalException)
            {
                Debug.WriteLine(error.Message);
            }
            if (token.IsCancellationRequested)
            {
                thumbnail?.Dispose();
                return;
            }
            thumbnails.Add(source, recent.AddFirst((source, thumbnail)));
            int capacity = Math.Max(CacheLimit, wanted.End - wanted.Start + columns * 2);
            while (recent.Count > capacity)
            {
                var oldest = recent.Last.Value;
                oldest.Image?.Dispose();
                thumbnails.Remove(oldest.Source);
                recent.RemoveLast();
            }
            Invalidate(CardBounds(index));
        }
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new GalleryAccessibility(this);

    private sealed class GalleryAccessibility : ControlAccessibleObject
    {
        private readonly LibraryGallery gallery;
        public GalleryAccessibility(LibraryGallery gallery) : base(gallery) => this.gallery = gallery;
        public override AccessibleRole Role => AccessibleRole.List;
        public override int GetChildCount()
        {
            var range = gallery.Range(0, gallery.Height);
            return range.End - range.Start + (gallery.scrollbar.Visible ? 1 : 0);
        }
        public override AccessibleObject GetChild(int index)
        {
            var range = gallery.Range(0, gallery.Height);
            if (index < 0 || index >= GetChildCount())
                return null;
            return index < range.End - range.Start ? new CardAccessibility(this, gallery, range.Start + index) :
                gallery.scrollbar.AccessibilityObject;
        }
        public override AccessibleObject GetSelected() => gallery.selected >= 0 ?
            new CardAccessibility(this, gallery, gallery.selected) : null;
    }

    private sealed class CardAccessibility : AccessibleObject
    {
        private readonly AccessibleObject parent;
        private readonly LibraryGallery gallery;
        private readonly Card card;
        public CardAccessibility(AccessibleObject parent, LibraryGallery gallery, int index)
        {
            this.parent = parent;
            this.gallery = gallery;
            card = gallery.cards[index];
        }
        public override AccessibleObject Parent => parent;
        public override AccessibleRole Role => AccessibleRole.ListItem;
        public override string Name { get => gallery.title(card.Entry.Wallpaper) + ", " + card.Caption; set { } }
        public override string DefaultAction => Strings.IsChinese ? "选择" : "Select";
        public override Rectangle Bounds =>
            gallery.byId.TryGetValue(card.Entry.Wallpaper.Id, out int index) && gallery.Visible ?
                gallery.RectangleToScreen(Rectangle.Intersect(gallery.View, gallery.CardBounds(index))) : Rectangle.Empty;
        public override AccessibleStates State => AccessibleStates.Selectable |
            (Bounds.Width == 0 || Bounds.Height == 0 ? AccessibleStates.Offscreen : 0) |
            (gallery.selected >= 0 && gallery.cards[gallery.selected].Entry.Wallpaper.Id == card.Entry.Wallpaper.Id
                ? AccessibleStates.Selected : 0);
        public override void DoDefaultAction()
        {
            if (gallery.byId.TryGetValue(card.Entry.Wallpaper.Id, out int index))
                gallery.select(gallery.cards[index].Entry);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            request?.Cancel();
            thumbnailDelay.Dispose();
            foreach (var item in recent)
                item.Image?.Dispose();
            thumbnails.Clear();
            recent.Clear();
        }
        base.Dispose(disposing);
    }
}
