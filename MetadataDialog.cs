using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace BingWallpapers;

internal sealed class MetadataDialog : AntdUI.Window
{
    private readonly MainForm owner;
    private readonly WallpaperLibrary library;
    private readonly InkLabel missing, missingHelp, untracked, untrackedHelp, written, note;
    private readonly FrameButton review, remove, add, editor;
    private readonly RichTextBox preview;
    public bool ReviewMissing { get; private set; }

    public MetadataDialog(MainForm owner, WallpaperLibrary library)
    {
        this.owner = owner;
        this.library = library;
        Text = "Metadata file";
        BackColor = Palette.Canvas;
        BorderColor = Palette.Line;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = MaximizeBox = false;
        Resizable = false;
        AutoScaleMode = AutoScaleMode.None;
        AutoHandDpi = false;
        _ = Handle;
        Size = new Size(Palette.Px(800), Palette.Px(592));

        var panel = MainForm.PanelOn(this, Palette.Surface, 19, true);
        panel.Dock = DockStyle.Fill;
        Put(MainForm.LabelOn(panel, "Metadata file", 36, false, Palette.Text, "Instrument Serif"), 28, 18, 605, 45);
        Put(MainForm.LabelOn(panel, "Everything the app remembers about your wallpapers lives in one JSON file next to the images.",
            13, false, Palette.Muted), 28, 62, 703, 32);
        Put(Button(panel, "", "close", 18, ButtonLook.Soft, Close, "Close"), 737, 24, 36, 36);
        var path = MainForm.PanelOn(panel, Palette.Canvas, 11, true);
        Put(path, 28, 116, 742, 44);
        var icon = MainForm.Add(path, new PictureBox { Image = Palette.Icon("json", 18, Palette.Muted), SizeMode = PictureBoxSizeMode.CenterImage });
        Put(icon, 13, 9, 23, 26);
        var pathText = MainForm.LabelOn(path, library.IndexPath, 12, true, Palette.Text, "JetBrains Mono");
        Put(pathText, 42, 0, 497, 44);
        Put(Button(path, "Show in folder", "folder", 16, ButtonLook.Plain, () => owner.Reveal(library.IndexPath)), 552, 6, 141, 32);
        Put(Button(path, "", "copy", 16, ButtonLook.Plain, () => owner.Copy(library.IndexPath), "Copy path"), 704, 6, 32, 32);

        var lost = Issue(panel, 176, 109, "warning", Palette.Amber);
        missing = MainForm.LabelOn(lost, "", 14, true);
        Put(missing, 57, 12, 264, 25);
        missingHelp = MainForm.LabelOn(lost, "", 12, false, Palette.Subtle);
        Put(missingHelp, 57, 37, 260, 20);
        review = Button(lost, "Review", null, 0, ButtonLook.Soft, () => { ReviewMissing = true; Close(); });
        remove = Button(lost, "Remove entries", null, 0, ButtonLook.Plain, RemoveEntries);
        Put(review, 57, 63, 69, 32);
        Put(remove, 134, 63, 126, 32);

        var newFiles = Issue(panel, 295, 109, "image", Palette.Amber);
        untracked = MainForm.LabelOn(newFiles, "", 14, true);
        Put(untracked, 57, 12, 264, 25);
        untrackedHelp = MainForm.LabelOn(newFiles, "", 12, false, Palette.Subtle);
        Put(untrackedHelp, 57, 37, 260, 20);
        add = Button(newFiles, "Add to library", null, 0, ButtonLook.Soft, () => Mutate(() => library.ImportFiles(false)));
        Put(add, 57, 63, 110, 32);
        var backup = Issue(panel, 414, 67, "check", Palette.Blue);
        written = MainForm.LabelOn(backup, "", 14, true);
        note = MainForm.LabelOn(backup, "", 12, false, Palette.Subtle);
        Put(written, 57, 10, 263, 24);
        Put(note, 57, 34, 261, 22);

        var code = MainForm.PanelOn(panel, Palette.Canvas, 14, true);
        Put(code, 374, 176, 396, 334);
        Put(MainForm.LabelOn(code, "Preview", 13, true), 17, 12, 61, 34);
        var readOnly = Button(code, "Read-only", "lock", 14, ButtonLook.Chip, null);
        readOnly.Font = Palette.Font(12);
        readOnly.Radius = 12;
        readOnly.TabStop = false;
        Put(readOnly, 77, 16, 101, 25);
        editor = Button(code, "Open in editor", "external", 16, ButtonLook.Plain, OpenEditor);
        Put(editor, 247, 12, 138, 34);
        preview = MainForm.Add(code, new RichTextBox
        {
            ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Palette.Canvas, ForeColor = Palette.Subtle,
            Font = Palette.Font(12, false, "JetBrains Mono"), WordWrap = false, DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.None, AccessibleName = "Read-only JSON preview"
        });
        Put(preview, 17, 57, 369, 265);
        Put(Button(panel, "Rebuild from folder", "refresh", 18, ButtonLook.Outline, Rebuild), 28, 526, 188, 40);
        Put(Button(panel, "Export…", null, 0, ButtonLook.Outline, ExportIndex), 226, 526, 91, 40);
        var done = Button(panel, "Done", null, 0, ButtonLook.Primary, Close);
        Put(done, 679, 526, 91, 40);
        CancelButton = done;
        UpdatePreview();
    }

    private FramePanel Issue(Control parent, int y, int height, string glyph, Color ink)
    {
        var card = MainForm.PanelOn(parent, Palette.Hover, 15);
        Put(card, 28, y, 330, height);
        var badge = MainForm.PanelOn(card, ink == Palette.Blue ? Color.FromArgb(48, 57, 52) : Palette.AmberTint, 10);
        Put(badge, 13, 14, 32, 32);
        var marker = MainForm.Add(badge, new PictureBox { Image = Palette.Icon(glyph, 18, ink), SizeMode = PictureBoxSizeMode.CenterImage });
        Put(marker, 0, 0, 32, 32);
        return card;
    }

    private void UpdatePreview()
    {
        int missingCount = library.Entries.Count(entry => !library.Exists(entry));
        int untrackedCount = library.UntrackedFiles().Count;
        missingHelp.Text = missingCount > 0 ? "Files were moved or deleted outside the app." : "Every indexed image is available.";
        untrackedHelp.Text = untrackedCount > 0 ? "They will not appear in Library until added." : "The folder and its index are in sync.";
        missing.Text = missingCount switch
        {
            0 => "No missing files",
            1 => "1 entry points to a missing file",
            _ => $"{missingCount} entries point to missing files"
        };
        untracked.Text = untrackedCount switch
        {
            0 => "All images are tracked",
            1 => "1 image in the folder is not tracked",
            _ => $"{untrackedCount} images in the folder are not tracked"
        };
        review.Enabled = remove.Enabled = missingCount > 0;
        add.Enabled = untrackedCount > 0;
        var file = new FileInfo(library.IndexPath);
        editor.Enabled = file.Exists;
        written.Text = file.Exists ? "Last written " + file.LastWriteTime.ToString("dd MMM, HH:mm", CultureInfo.GetCultureInfo("en-US"))
            + " · " + MainForm.FormatBytes(file.Length) : "No index written yet";
        note.Text = File.Exists(library.BackupPath) ? "Backup kept as wallpapers.json.bak" : "Backups are created on the first edit.";
        string source;
        if (!file.Exists) source = "{\r\n  \"version\": 1,\r\n  \"items\": []\r\n}";
        else
        {
            using (var stream = new StreamReader(library.IndexPath))
            {
                char[] buffer = new char[12000];
                int length = stream.ReadBlock(buffer, 0, buffer.Length);
                source = new string(buffer, 0, length);
                if (!stream.EndOfStream) source += "\r\n… Open in editor to view the full index.";
            }
        }
        preview.Text = source;
        foreach (Match token in Regex.Matches(source, @"""(?:\\.|[^""\\])*""(?<key>\s*:)?|\b(?:true|false|null|\d+)\b"))
        {
            preview.Select(token.Index, token.Length);
            preview.SelectionColor = token.Groups["key"].Success ? Palette.Subtle : token.Value.StartsWith("\"") ? Palette.Amber : Palette.Blue;
        }
        preview.Select(0, 0);
        preview.ScrollToCaret();
    }

    private void Mutate(Action action)
    {
        try { action(); UpdatePreview(); }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
        {
            note.Text = error.Message;
            note.ForeColor = Palette.Error;
            owner.Report(error);
        }
    }

    private void RemoveEntries()
    {
        if (MessageBox.Show(this, "Remove records that point to missing files?", "Remove entries",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.OK)
            Mutate(library.RemoveMissing);
    }

    private void Rebuild()
    {
        if (MessageBox.Show(this, "Add untracked JPEGs and remove records whose files are missing? Existing image files and their metadata are kept.",
            "Rebuild from folder", MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) == DialogResult.OK)
            Mutate(() => library.ImportFiles(true));
    }

    private void ExportIndex()
    {
        using (var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "dayframe-wallpapers.json" })
        {
            if (dialog.ShowDialog(this) == DialogResult.OK)
                Mutate(() => library.Export(dialog.FileName));
        }
    }

    private void OpenEditor() => owner.Attempt(() => Process.Start(new ProcessStartInfo("notepad.exe",
        $"\"{library.IndexPath}\"") { UseShellExecute = true }));

    private static FrameButton Button(Control parent, string title, string glyph, int size, ButtonLook look,
        Action click, string description = null) => MainForm.ButtonOn(parent, title, glyph, size, look, click, description);

    private static void Put(Control control, int x, int y, int w, int h) =>
        control.SetBounds(Palette.Px(x), Palette.Px(y), Palette.Px(w), Palette.Px(h));
}
