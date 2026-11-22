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
        if (!ready || quitting || page != "library")
            return;
        preferredId = preferredId ?? savedEntry?.Wallpaper.Id;
        var all = library?.Entries ?? Array.Empty<SavedWallpaper>();
        var files = (library?.ImageFiles() ?? Enumerable.Empty<FileInfo>())
            .ToDictionary(file => file.Name, StringComparer.OrdinalIgnoreCase);
        var present = all.Where(entry => files.ContainsKey(entry.FileName)).ToList();
        int missingCount = all.Count - present.Count;
        string query = search.Text.Trim();
        string dateQuery = query.Replace(".", "").Replace("-", "");
        var hits = (libraryFilter == 1 ? all.Where(entry => entry.Favorite) :
            libraryFilter == 2 ? present.Where(entry => entry.Rotation) :
            libraryFilter == 3 ? all.Where(entry => !files.ContainsKey(entry.FileName)) : all).Where(entry => query.Length == 0 ||
                TitleFor(entry.Wallpaper).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                entry.Wallpaper.Title.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                entry.Tags.Any(tag => tag.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0) ||
                entry.Wallpaper.Date.Contains(dateQuery));
        var ordered = order == 1 ? hits.OrderBy(entry => entry.Wallpaper.Date, StringComparer.Ordinal)
            : order == 2 ? hits.OrderBy(entry => TitleFor(entry.Wallpaper), StringComparer.Create(Strings.Culture, true))
            : hits.OrderByDescending(entry => entry.Wallpaper.Date, StringComparer.Ordinal);
        var visible = ordered.ToList();
        libraryStats.Text = Strings.Format(all.Count == 1 ? "{0} wallpaper" : "{0} wallpapers", all.Count) +
            " · " + FormatBytes(present.Sum(entry => files[entry.FileName].Length));
        int[] counts = { all.Count, all.Count(item => item.Favorite), present.Count(item => item.Rotation), missingCount };
        string[] categories = { "All", "Favorites", "In rotation", "Missing file" };
        for (int i = 0; i < libraryFilters.Length; i++)
        {
            libraryFilters[i].Text = $"{Strings.Get(categories[i])} · {counts[i]}";
            libraryFilters[i].Selected = i == libraryFilter;
            libraryFilters[i].Invalidate();
        }
        int issues = missingCount + files.Count - present.Count;
        indexStatus.Text = issues == 0 ? Strings.Get("Metadata is up to date") :
            Strings.Format(issues == 1 ? "{0} metadata issue" : "{0} metadata issues", issues);
        indexStatus.ForeColor = issues == 0 ? Palette.Blue : Palette.Accent;
        indexCount.Text = Strings.Format("wallpapers.json · {0} entries", all.Count);
        metadata.Enabled = library != null;

        string emptyText = Strings.Get(library == null ? "Library unavailable. Check the folder in Settings." :
            query.Length > 0 ? "No matching wallpapers" : libraryFilter == 0 ?
                "Set a Bing wallpaper to start your library." : "No wallpapers in this filter");
        gallery.SetItems(visible.Select(entry =>
        {
            files.TryGetValue(entry.FileName, out FileInfo file);
            return new LibraryGallery.Card(entry, file);
        }).ToArray(), settings.CurrentId, emptyText);
        var selection = visible.Find(item => item.Wallpaper.Id == preferredId) ?? visible.FirstOrDefault();
        bool available = selection != null && files.ContainsKey(selection.FileName);
        if (selection != savedEntry || selection == null || available != libraryPreview.Loaded)
            SelectSaved(selection);
        else
        {
            gallery.SelectEntry(selection);
            UpdateButtons();
        }
    }

    private void SelectSaved(SavedWallpaper entry)
    {
        savedEntry = entry;
        gallery.SelectEntry(entry);
        Cancel(ref inspectorRequest);
        libraryPreview.SetImage(gallery.CopySelectedThumbnail());
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
