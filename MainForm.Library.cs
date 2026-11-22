using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace BingWallpapers;

internal sealed partial class MainForm
{
    private void RenderLibrary(string preferredId = null)
    {
        if (!ready || quitting || page != "library")
            return;
        preferredId = preferredId ?? savedEntry?.Wallpaper.Id;
        var all = library?.Entries ?? Array.Empty<SavedWallpaper>();
        var missing = new HashSet<SavedWallpaper>(all.Where(entry => !library.Exists(entry)));
        var present = all.Where(entry => !missing.Contains(entry)).ToList();
        string query = search.Text.Trim();
        var hits = (libraryFilter == 1 ? all.Where(entry => entry.Favorite) :
            libraryFilter == 2 ? present.Where(entry => entry.Rotation) :
            libraryFilter == 3 ? all.Where(missing.Contains) : all).Where(entry => query.Length == 0 ||
                TitleFor(entry.Wallpaper).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                entry.Wallpaper.Title.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                entry.Tags.Any(tag => tag.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0) ||
                entry.Wallpaper.Date.Contains(query.Replace(".", "").Replace("-", "")));
        var ordered = order == 1 ? hits.OrderBy(entry => entry.Wallpaper.Date, StringComparer.Ordinal)
            : order == 2 ? hits.OrderBy(entry => TitleFor(entry.Wallpaper), StringComparer.Create(Strings.Culture, true))
            : hits.OrderByDescending(entry => entry.Wallpaper.Date, StringComparer.Ordinal);
        var visible = ordered.ToList();
        libraryStats.Text = Strings.Format(all.Count == 1 ? "{0} wallpaper" : "{0} wallpapers", all.Count) +
            " · " + FormatBytes(present.Sum(entry => library.Bytes(entry)));
        int[] counts = { all.Count, all.Count(item => item.Favorite), present.Count(item => item.Rotation), missing.Count };
        string[] categories = { "All", "Favorites", "In rotation", "Missing file" };
        for (int i = 0; i < libraryFilters.Length; i++)
        {
            libraryFilters[i].Text = $"{Strings.Get(categories[i])} · {counts[i]}";
            libraryFilters[i].Selected = i == libraryFilter;
            libraryFilters[i].Invalidate();
        }
        int issues = missing.Count + (library?.UntrackedFiles().Count ?? 0);
        indexStatus.Text = issues == 0 ? Strings.Get("Metadata is up to date") :
            Strings.Format(issues == 1 ? "{0} metadata issue" : "{0} metadata issues", issues);
        indexStatus.ForeColor = issues == 0 ? Palette.Blue : Palette.Accent;
        indexCount.Text = Strings.Format("wallpapers.json · {0} entries", all.Count);
        metadata.Enabled = library != null;

        galleryEmpty.Text = Strings.Get(library == null ? "Library unavailable. Check the folder in Settings." :
            query.Length > 0 ? "No matching wallpapers" : libraryFilter == 0 ?
                "Set a Bing wallpaper to start your library." : "No wallpapers in this filter");
        galleryEmpty.Visible = visible.Count == 0;
        var cards = gallery.Controls.OfType<PhotoTile>().ToArray();
        bool rebuild = galleryRequest == null || cards.Length != visible.Count || cards.Where((card, i) =>
            card.Entry != visible[i] || card.Missing != missing.Contains(visible[i])).Any();
        if (rebuild)
        {
            Cancel(ref galleryRequest);
            galleryRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            gallery.SuspendLayout();
            try
            {
                foreach (var tile in cards)
                    tile.Dispose();
                gallery.AutoScrollPosition = Point.Empty;
                foreach (var entry in visible)
                {
                    var tile = new PhotoTile(entry.Wallpaper, entry, Strings.Date(entry.Wallpaper.Day, "d MMM yyyy", "yyyy年M月d日"),
                        false, TitleFor(entry.Wallpaper))
                    {
                        OnDesktop = entry.Wallpaper.Id == settings.CurrentId,
                        Missing = missing.Contains(entry), Margin = new Padding(0, 0, S(12), S(10))
                    };
                    tile.Click += (_, __) => SelectSaved(library.Find(entry.Wallpaper.Id));
                    gallery.Controls.Add(tile);
                }
                SizeGallery();
            }
            finally { gallery.ResumeLayout(true); }
        }
        else
            foreach (var tile in cards)
            {
                bool onDesktop = tile.Wallpaper.Id == settings.CurrentId;
                if (tile.OnDesktop != onDesktop)
                {
                    tile.OnDesktop = onDesktop;
                    tile.Invalidate();
                }
                tile.SetTitle(TitleFor(tile.Wallpaper));
            }
        var selection = visible.Find(item => item.Wallpaper.Id == preferredId) ?? visible.FirstOrDefault();
        if (rebuild || selection != savedEntry)
            SelectSaved(selection);
        else if (selection == null)
            UpdateButtons();
        QueueGalleryPreviews();
    }

    private void SizeGallery()
    {
        if (gallery == null || gallery.Width < S(100))
            return;
        int width = Math.Max(S(100), (gallery.Width - SystemInformation.VerticalScrollBarWidth - 3 * S(12)) / 3);
        int height = width * 9 / 16 + S(44);
        var tileSize = new Size(width, height);
        foreach (PhotoTile tile in gallery.Controls.OfType<PhotoTile>())
            if (tile.Size != tileSize)
                tile.Size = tileSize;
        galleryEmpty.Size = new Size(gallery.Width - SystemInformation.VerticalScrollBarWidth - S(10), S(65));
    }

    private void QueueGalleryPreviews()
    {
        if (!ready || page != "library" || galleryRequest == null || quitting || IsResizing)
            return;
        var viewport = Rectangle.Inflate(gallery.ClientRectangle, 0, S(160));
        foreach (PhotoTile tile in gallery.Controls.OfType<PhotoTile>())
        {
            if (tile.Requested || tile.Missing || !tile.Bounds.IntersectsWith(viewport))
                continue;
            tile.Requested = true;
            _ = LoadTileAsync(tile, galleryRequest.Token);
        }
    }

    private void SelectSaved(SavedWallpaper entry)
    {
        savedEntry = entry;
        PhotoTile selectedCard = null;
        foreach (PhotoTile tile in gallery.Controls.OfType<PhotoTile>())
        {
            bool selectedTile = tile.Wallpaper.Id == entry?.Wallpaper.Id;
            if (selectedTile)
                selectedCard = tile;
            if (tile.Selected != selectedTile)
            {
                tile.Selected = selectedTile;
                tile.Invalidate();
            }
        }
        Cancel(ref inspectorRequest);
        libraryPreview.SetImage(selectedCard?.CopyThumbnail());
        editingDetails = true;
        editTitle.Text = TitleFor(entry?.Wallpaper) ?? "";
        detailDate.Text = entry == null ? "—" : Strings.Date(entry.Wallpaper.Day, "ddd, d MMM yyyy", "yyyy年M月d日");
        detailSize.Text = entry == null ? "—" : $"{entry.Width} × {entry.Height} · {FormatBytes(library.Bytes(entry))}";
        detailCredit.Text = string.IsNullOrWhiteSpace(entry?.Wallpaper.Copyright) ? "—" : Attribution(entry.Wallpaper);
        tip.SetToolTip(detailCredit, entry?.Wallpaper.Copyright ?? "");
        detailApplied.Text = DateTimeOffset.TryParse(entry?.AppliedAt, out var applied)
            ? Strings.Date(applied.LocalDateTime, "ddd d MMM, HH:mm", "M月d日 HH:mm") : "—";
        savedName.Text = entry?.FileName ?? "—";
        tip.SetToolTip(savedName, savedName.Text);
        savedFavorite.Checked = entry?.Favorite == true;
        savedRotation.Checked = entry?.Rotation == true;
        editingDetails = false;
        FillTags();
        if (entry != null && library.Exists(entry) && page == "library")
        {
            inspectorRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            _ = LoadPhotoAsync(libraryPreview, entry.Wallpaper, false, inspectorRequest.Token);
        }
        UpdateButtons();
    }

    private void FillTags()
    {
        while (tags.Controls.Count > 0) tags.Controls[0].Dispose();
        if (savedEntry == null)
            return;
        foreach (var name in savedEntry.Tags)
        {
            var chip = ButtonOn(tags, "", null, 0, ButtonLook.Chip, null);
            chip.Text = name;
            chip.AccessibleName = name;
            chip.Click += (_, __) => Popup(chip, ("Remove tag", () => ChangeEntry(item => item.With(
                tags: item.Tags.Where(tag => tag != name).ToList()))));
            chip.Size = new Size(S(Math.Min(130, Math.Max(46, 22 + name.Length * 7))), S(30));
            chip.Margin = new Padding(0, 0, S(6), S(4));
            chip.Font = Palette.Font(13);
            chip.Radius = 15;
        }
        var add = ButtonOn(tags, "Add tag", "plus", 14, ButtonLook.Outline, () =>
        {
            string name = AskText("Add tag", "Tag name", "");
            if (!string.IsNullOrEmpty(name) && savedEntry != null &&
                !savedEntry.Tags.Contains(name, StringComparer.CurrentCultureIgnoreCase))
                ChangeEntry(item => item.With(tags: item.Tags.Concat(new[] { name }).ToList()));
        });
        add.Size = new Size(S(90), S(30));
        add.Margin = Padding.Empty;
        add.Font = Palette.Font(13);
        add.Radius = 15;
    }

    private void CommitTitle()
    {
        if (!ready || savedEntry == null || editingDetails || quitting)
            return;
        string title = editTitle.Text.Trim();
        if (title.Length == 0)
        {
            editTitle.Text = TitleFor(savedEntry.Wallpaper);
            return;
        }
        if (title != TitleFor(savedEntry.Wallpaper))
            ChangeEntry(entry => entry.With(title: title));
    }

    private void ChangeEntry(Func<SavedWallpaper, SavedWallpaper> change)
    {
        if (editingDetails || changing || savedEntry == null || quitting)
            return;
        Attempt(() =>
        {
            var updated = change(savedEntry);
            library.Update(updated);
            savedEntry = updated;
            RenderLibrary(updated.Wallpaper.Id);
            RefreshTodayLabels();
            UpdateDesktopCard();
            queued = null;
            PickQueue();
            ConfigureRotationUi();
        });
    }

    private void DeleteSelected()
    {
        var entry = savedEntry;
        if (entry == null || changing)
            return;
        if (MessageBox.Show(this, Strings.Format("Remove \"{0}\" from Library and delete its local file?", TitleFor(entry.Wallpaper)),
            Strings.Get("Remove wallpaper"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) != DialogResult.OK || changing)
            return;
        Attempt(() =>
        {
            library.Remove(entry);
            savedEntry = null;
            RenderLibrary();
            RefreshTodayLabels();
            PickQueue();
        });
    }

    private static string SortLabel(int index) => new[] { "Newest first", "Oldest first", "Title A–Z" }[index];

    private void ShowSortChoices() => Popup(sort, Enumerable.Range(0, 3)
        .Select(index => (SortLabel(index), (Action)(() =>
        {
            order = index;
            sort.Text = Strings.Get(SortLabel(index));
            RenderLibrary(savedEntry?.Wallpaper.Id);
        }))).ToArray());

    private void OpenMetadata()
    {
        if (library == null) return;
        Attempt(() =>
        {
            library.Reload();
            using (var dialog = new MetadataDialog(this, library))
            {
                dialog.ShowDialog(this);
                if (dialog.ReviewMissing)
                    libraryFilter = 3;
            }
        });
        RenderLibrary();
        RefreshTodayLabels();
        PickQueue();
    }

    private string AskText(string title, string prompt, string value)
    {
        using (var dialog = new Form
        {
            Text = Strings.Get(title), FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
            ShowInTaskbar = false, StartPosition = FormStartPosition.CenterParent, BackColor = Palette.Surface,
            ClientSize = new Size(S(375), S(150))
        })
        {
            Put(LabelOn(dialog, prompt, 15), 17, 15, 340, 24);
            var input = InputOn(dialog, prompt);
            input.Text = value;
            input.MaxLength = 28;
            Put(input, 17, 45, 340, 40);
            var save = ButtonOn(dialog, "Add", null, 0, ButtonLook.Primary, () => dialog.DialogResult = DialogResult.OK);
            var cancel = ButtonOn(dialog, "Cancel", null, 0, ButtonLook.Outline, () => dialog.DialogResult = DialogResult.Cancel);
            Put(save, 274, 101, 83, 36);
            Put(cancel, 183, 101, 83, 36);
            dialog.AcceptButton = save;
            dialog.CancelButton = cancel;
            return dialog.ShowDialog(this) == DialogResult.OK ? input.Text.Trim() : null;
        }
    }
}
