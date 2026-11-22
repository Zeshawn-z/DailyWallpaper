using System;
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
        if (!ready || quitting)
            return;
        preferredId = preferredId ?? savedEntry?.Wallpaper.Id;
        Cancel(ref galleryRequest);
        galleryRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var all = library?.Entries ?? Array.Empty<SavedWallpaper>();
        var missing = all.Where(entry => !library.Exists(entry)).ToList();
        var present = all.Except(missing).ToList();
        string query = search.Text.Trim();
        var hits = (libraryFilter == 1 ? all.Where(entry => entry.Favorite) :
            libraryFilter == 2 ? present.Where(entry => entry.Rotation) :
            libraryFilter == 3 ? missing : all).Where(entry => query.Length == 0 ||
                entry.Wallpaper.Title.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                entry.Tags.Any(tag => tag.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0) ||
                entry.Wallpaper.Date.Contains(query.Replace(".", "").Replace("-", "")));
        var ordered = order == 1 ? hits.OrderBy(entry => entry.Wallpaper.Date, StringComparer.Ordinal)
            : order == 2 ? hits.OrderBy(entry => entry.Wallpaper.Title, StringComparer.CurrentCultureIgnoreCase)
            : hits.OrderByDescending(entry => entry.Wallpaper.Date, StringComparer.Ordinal);
        var visible = ordered.ToList();
        libraryStats.Text = $"{all.Count} wallpapers · {FormatBytes(present.Sum(entry => library.Bytes(entry)))}";
        int[] counts = { all.Count, all.Count(item => item.Favorite), present.Count(item => item.Rotation), missing.Count };
        string[] categories = { "All", "Favorites", "In rotation", "Missing file" };
        for (int i = 0; i < libraryFilters.Length; i++)
        {
            libraryFilters[i].Text = $"{categories[i]} · {counts[i]}";
            libraryFilters[i].Selected = i == libraryFilter;
            libraryFilters[i].Invalidate();
        }
        int issues = missing.Count + (library?.UntrackedFiles().Count ?? 0);
        indexStatus.Text = issues == 0 ? "Metadata is up to date" : $"{issues} metadata issue{(issues == 1 ? "" : "s")}";
        indexStatus.ForeColor = issues == 0 ? Palette.Blue : Palette.Amber;
        indexCount.Text = $"wallpapers.json · {all.Count} entries";
        metadata.Enabled = library != null;

        gallery.SuspendLayout();
        foreach (var tile in gallery.Controls.OfType<PhotoTile>().ToArray())
            tile.Dispose();
        gallery.AutoScrollPosition = Point.Empty;
        galleryEmpty.Text = library == null ? "Library unavailable. Check the folder in Settings." :
            query.Length > 0 ? "No matching wallpapers" : libraryFilter == 0 ? "Set a Bing wallpaper to start your library." : "No wallpapers in this filter";
        galleryEmpty.Visible = visible.Count == 0;
        foreach (var entry in visible)
        {
            var tile = new PhotoTile(entry.Wallpaper, entry, entry.Wallpaper.Day.ToString("d MMM yyyy", English), false)
            {
                OnDesktop = entry.Wallpaper.Id == settings.CurrentId,
                Missing = missing.Contains(entry), Margin = new Padding(0, 0, S(12), S(10))
            };
            tile.Click += (_, __) => SelectSaved(library.Find(entry.Wallpaper.Id));
            gallery.Controls.Add(tile);
        }
        SizeGallery();
        gallery.ResumeLayout(true);
        SelectSaved(visible.Find(item => item.Wallpaper.Id == preferredId) ?? visible.FirstOrDefault());
        QueueGalleryPreviews();
    }

    private void SizeGallery()
    {
        if (gallery == null || gallery.Width < S(100))
            return;
        int width = Math.Max(S(100), (gallery.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - S(32)) / 3);
        int height = width * 9 / 16 + S(44);
        foreach (PhotoTile tile in gallery.Controls.OfType<PhotoTile>())
            tile.Size = new Size(width, height);
        galleryEmpty.Size = new Size(gallery.ClientSize.Width - S(10), S(65));
        QueueGalleryPreviews();
    }

    private void QueueGalleryPreviews()
    {
        if (!ready || page != "library" || galleryRequest == null || quitting)
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
        foreach (PhotoTile tile in gallery.Controls.OfType<PhotoTile>())
        {
            tile.Selected = tile.Wallpaper.Id == entry?.Wallpaper.Id;
            tile.Invalidate();
        }
        Cancel(ref inspectorRequest);
        libraryPreview.SetImage(null);
        editingDetails = true;
        editTitle.Text = entry?.Wallpaper.Title ?? "";
        detailDate.Text = entry?.Wallpaper.Day.ToString("ddd, d MMM yyyy", English) ?? "—";
        detailSize.Text = entry == null ? "—" : $"{entry.Width} × {entry.Height} · {FormatBytes(library.Bytes(entry))}";
        detailCredit.Text = string.IsNullOrWhiteSpace(entry?.Wallpaper.Copyright) ? "—" : Attribution(entry.Wallpaper);
        tip.SetToolTip(detailCredit, entry?.Wallpaper.Copyright ?? "");
        detailApplied.Text = DateTimeOffset.TryParse(entry?.AppliedAt, out var applied)
            ? applied.ToLocalTime().ToString("ddd d MMM, HH:mm", English) : "—";
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
            var chip = ButtonOn(tags, name, null, 0, ButtonLook.Chip, null);
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
        if (savedEntry == null || editingDetails || quitting)
            return;
        string title = editTitle.Text.Trim();
        if (title.Length == 0)
        {
            editTitle.Text = savedEntry.Wallpaper.Title;
            return;
        }
        if (title != savedEntry.Wallpaper.Title)
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
        if (MessageBox.Show(this, $"Remove \"{entry.Wallpaper.Title}\" from Library and delete its local file?",
            "Remove wallpaper", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
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

    private void ShowSortChoices() => Popup(sort, ("Newest first", () => ChangeSort(0, "Newest first")),
        ("Oldest first", () => ChangeSort(1, "Oldest first")), ("Title A–Z", () => ChangeSort(2, "Title A–Z")));

    private void ChangeSort(int index, string label)
    {
        order = index;
        sort.Text = label;
        RenderLibrary(savedEntry?.Wallpaper.Id);
    }

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
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
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
