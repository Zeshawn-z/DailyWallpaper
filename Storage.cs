using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BingWallpapers;

[DataContract]
internal sealed class Settings
{
    public static readonly string Profile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BingWallpapers");
    public static readonly string DefaultDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Bing Wallpapers");
    public static string FilePath => Path.Combine(Profile, "settings.json");

    [DataMember] public string LibraryDirectory { get; set; } = DefaultDirectory;
    [DataMember] public string ProxyMode { get; set; } = "system";
    [DataMember] public string ProxyAddress { get; set; } = "";
    [DataMember] public string RotationMode { get; set; } = "off";
    [DataMember] public int Interval { get; set; } = 60;
    [DataMember] public int RandomMinimum { get; set; } = 30;
    [DataMember] public int RandomMaximum { get; set; } = 120;
    [DataMember] public string CurrentId { get; set; }
    [DataMember] public string CurrentFit { get; set; }
    [DataMember] public string Market { get; set; } = "en-US";
    [DataMember] public string Fit { get; set; } = "Fill";
    [DataMember] public string Pool { get; set; } = "library";
    [DataMember] public bool? RotationEnabled { get; set; } = false;
    [DataMember] public bool? AvoidRecent { get; set; } = true;
    [DataMember] public bool? KeepInTray { get; set; } = true;
    [DataMember] public bool PauseOnBattery { get; set; }
    [DataMember] public string PausedUntil { get; set; }
    [DataMember] public List<ChangeRecord> History { get; set; } = new List<ChangeRecord>();

    public static Settings Load()
    {
        var value = JsonFile.Read<Settings>(FilePath) ?? new Settings();
        value.Market = value.Market ?? "en-US";
        value.Fit = value.Fit ?? "Fill";
        value.CurrentFit = value.CurrentFit ?? value.Fit;
        value.Pool = value.Pool ?? "library";
        value.RotationEnabled = value.RotationEnabled ?? value.RotationMode != "off";
        if (value.RotationMode == "off")
            value.RotationMode = "fixed";
        value.AvoidRecent = value.AvoidRecent ?? true;
        value.KeepInTray = value.KeepInTray ?? true;
        value.History = value.History ?? new List<ChangeRecord>();
        return value;
    }
    public void Save() => JsonFile.Write(FilePath, this);
    public Settings Copy() => (Settings)MemberwiseClone();
}

[DataContract]
internal sealed class ChangeRecord
{
    [DataMember] public string Id { get; set; }
    [DataMember] public string Title { get; set; }
    [DataMember] public string At { get; set; }
}

[DataContract]
internal sealed class Wallpaper
{
    [DataMember] public string Id { get; set; }
    [DataMember] public string Title { get; set; }
    [DataMember] public string Date { get; set; }
    [DataMember] public string Copyright { get; set; }
    [DataMember] public string CopyrightLink { get; set; }
    [DataMember] public string BingPath { get; set; }
    [DataMember] public bool Downloadable { get; set; }

    public DateTime Day => DateTime.ParseExact(Date, "yyyyMMdd", CultureInfo.InvariantCulture);
    public string DisplayDate => Day.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
}

[DataContract]
internal sealed class SavedWallpaper
{
    [DataMember] public Wallpaper Wallpaper { get; set; }
    [DataMember] public string FileName { get; set; }
    [DataMember] public string SavedAt { get; set; }
    [DataMember] public int Width { get; set; }
    [DataMember] public int Height { get; set; }
    public bool Favorite { get; set; }
    public bool Rotation { get; set; } = true;
    public List<string> Tags { get; set; } = new List<string>();
    public string AppliedAt { get; set; }

    public SavedWallpaper With(string title = null, bool? favorite = null, bool? rotation = null,
        List<string> tags = null, string appliedAt = null)
    {
        var item = (SavedWallpaper)MemberwiseClone();
        if (title != null)
            item.Wallpaper = new Wallpaper
            {
                Id = Wallpaper.Id, Title = title, Date = Wallpaper.Date, Copyright = Wallpaper.Copyright,
                CopyrightLink = Wallpaper.CopyrightLink, BingPath = Wallpaper.BingPath, Downloadable = Wallpaper.Downloadable
            };
        item.Favorite = favorite ?? Favorite;
        item.Rotation = rotation ?? Rotation;
        item.Tags = tags ?? Tags;
        item.AppliedAt = appliedAt ?? AppliedAt;
        return item;
    }
}

internal sealed class WallpaperLibrary
{
    public string DirectoryPath { get; }
    public string IndexPath => Path.Combine(DirectoryPath, "wallpapers.json");
    public string BackupPath => IndexPath + ".bak";
    private List<SavedWallpaper> entries;
    private DateTime indexWritten;
    public IReadOnlyList<SavedWallpaper> Entries => entries;

    public WallpaperLibrary(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory);
        Reload();
    }

    public void Reload()
    {
        DateTime lastWrite = File.GetLastWriteTimeUtc(IndexPath);
        if (entries != null && lastWrite == indexWritten)
            return;
        List<SavedWallpaper> imported;
        try { imported = ReadIndex(); }
        catch (SerializationException error) { throw new InvalidDataException($"Could not read the wallpaper index at {IndexPath}.", error); }
        if (imported.Any(entry => entry?.Wallpaper == null ||
            !DateTime.TryParseExact(entry.Wallpaper.Date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
            string.IsNullOrWhiteSpace(entry.Wallpaper.Id) || string.IsNullOrWhiteSpace(entry.Wallpaper.Title) ||
            !IsImageName(entry.FileName)) ||
            imported.Select(entry => entry.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != imported.Count ||
            imported.Select(entry => entry.Wallpaper.Id).Distinct(StringComparer.Ordinal).Count() != imported.Count)
            throw new InvalidDataException("The index contains incomplete or duplicate wallpapers.");
        entries = imported;
        indexWritten = lastWrite;
    }

    private static bool IsImageName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        (name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
         name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));

    public string PathOf(SavedWallpaper entry) => Path.Combine(DirectoryPath, entry.FileName);
    public SavedWallpaper Find(string id) => entries.Find(entry => entry.Wallpaper.Id == id);
    public string ProposedName(Wallpaper wallpaper) => Find(wallpaper.Id)?.FileName ?? NextName(wallpaper);
    public bool Exists(SavedWallpaper entry) => File.Exists(PathOf(entry));
    public long Bytes(SavedWallpaper entry) => Exists(entry) ? new FileInfo(PathOf(entry)).Length : 0;

    private List<SavedWallpaper> ReadIndex()
    {
        try
        {
            using (var stream = File.OpenRead(IndexPath))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, true))
            {
                while (reader.Peek() >= 0 && char.IsWhiteSpace((char)reader.Peek()))
                    reader.Read();
                int prefix = reader.Peek();
                stream.Position = 0;
                if (prefix == '[')
                {
                    var old = JsonFile.Read<List<SavedWallpaper>>(stream);
                    foreach (var item in old.Where(item => item != null))
                    {
                        item.Rotation = true;
                        item.Tags = new List<string>();
                    }
                    return old;
                }
                var index = JsonFile.Read<IndexFile>(stream);
                if (index == null || index.Version != 1 || index.Items == null)
                    throw new InvalidDataException("Unsupported wallpaper index format.");
                return index.Items.Select(item => item?.ToEntry()).ToList();
            }
        }
        catch (FileNotFoundException) { return new List<SavedWallpaper>(); }
        catch (DirectoryNotFoundException) { return new List<SavedWallpaper>(); }
    }

    private IndexFile Index(IEnumerable<SavedWallpaper> items) => new IndexFile
    {
        Version = 1,
        Items = items.Select(item => new IndexEntry(item)).ToList()
    };

    private void Commit(List<SavedWallpaper> next)
    {
        if (File.GetLastWriteTimeUtc(IndexPath) != indexWritten)
            throw new IOException("wallpapers.json changed outside Dayframe. Reopen Library before editing it.");
        JsonFile.Write(IndexPath, Index(next), BackupPath);
        indexWritten = File.GetLastWriteTimeUtc(IndexPath);
        entries = next;
    }

    public void Update(SavedWallpaper entry)
    {
        Commit(entries.Select(item => item.Wallpaper.Id == entry.Wallpaper.Id ? entry : item).ToList());
    }

    public List<string> UntrackedFiles()
    {
        if (!Directory.Exists(DirectoryPath))
            return new List<string>();
        var indexed = new HashSet<string>(entries.Select(entry => entry.FileName), StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(DirectoryPath).Select(Path.GetFileName)
            .Where(name => IsImageName(name) && !indexed.Contains(name))
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public void ImportFiles(bool rebuild)
    {
        var next = entries.Where(entry => !rebuild || Exists(entry)).ToList();
        foreach (string name in UntrackedFiles())
        {
            string path = Path.Combine(DirectoryPath, name);
            string title = Path.GetFileNameWithoutExtension(name);
            DateTime date = File.GetLastWriteTime(path).Date;
            int separator = title.LastIndexOf('_');
            if (separator > 0 && DateTime.TryParseExact(title.Substring(separator + 1), "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fromName))
            {
                date = fromName;
                title = title.Substring(0, separator);
            }
            using (var image = Image.FromFile(path))
            {
                next.Add(new SavedWallpaper
                {
                    Wallpaper = new Wallpaper
                    {
                        Id = "local-" + Guid.NewGuid().ToString("N"), Title = title,
                        Date = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture), Copyright = ""
                    },
                    FileName = name, Width = image.Width, Height = image.Height,
                    SavedAt = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture)
                });
            }
        }
        Commit(next);
    }

    public void RemoveMissing() => Commit(entries.Where(Exists).ToList());
    public void Export(string path) => JsonFile.Write(path, Index(entries));

    public async Task<SavedWallpaper> SaveAsync(Wallpaper wallpaper, BingClient bing, CancellationToken token)
    {
        var existing = Find(wallpaper.Id);
        if (existing != null && File.Exists(PathOf(existing)))
            return existing;

        var download = await bing.DownloadAsync(wallpaper, token);
        Directory.CreateDirectory(DirectoryPath);
        var fileName = existing?.FileName ?? NextName(wallpaper);
        var entry = new SavedWallpaper
        {
            Wallpaper = wallpaper,
            FileName = fileName,
            Width = download.Width,
            Height = download.Height,
            SavedAt = existing?.SavedAt ?? DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
            Favorite = existing?.Favorite ?? false, Rotation = existing?.Rotation ?? true,
            Tags = existing?.Tags ?? new List<string>(), AppliedAt = existing?.AppliedAt
        };

        var next = new List<SavedWallpaper>(entries);
        if (existing != null)
            next.Remove(existing);
        next.Insert(0, entry);

        var path = PathOf(entry);
        string temp = Path.Combine(DirectoryPath, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                await stream.WriteAsync(download.Bytes, 0, download.Bytes.Length, token);
            File.Move(temp, path);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
        try
        {
            Commit(next);
        }
        catch
        {
            File.Delete(path);
            throw;
        }
        return entry;
    }

    public void Remove(SavedWallpaper entry)
    {
        var path = PathOf(entry);
        var staged = Path.Combine(DirectoryPath, "." + Guid.NewGuid().ToString("N") + ".deleting");
        bool hasImage = File.Exists(path);
        if (hasImage)
            File.Move(path, staged);

        var next = new List<SavedWallpaper>(entries);
        next.Remove(entry);
        try
        {
            Commit(next);
        }
        catch
        {
            if (hasImage)
                File.Move(staged, path);
            throw;
        }
        if (hasImage)
            File.Delete(staged);
    }

    private string NextName(Wallpaper wallpaper)
    {
        const int LegacyPathLimit = 259;
        int available = Math.Min(180, LegacyPathLimit - DirectoryPath.Length - 1);
        if (available < 40)
            throw new PathTooLongException("Choose a shorter library directory in Settings.");

        var invalid = Path.GetInvalidFileNameChars();
        string title = new string(wallpaper.Title.Select(character =>
            Array.IndexOf(invalid, character) >= 0 ? '_' : character).ToArray()).Trim(' ', '.');
        if (title.Length == 0)
            title = "Bing wallpaper";
        for (int number = 1; ; number++)
        {
            string suffix = (number == 1 ? "" : $" ({number})") + "_" + wallpaper.Date + ".jpg";
            int length = Math.Min(title.Length, available - suffix.Length);
            if (char.IsHighSurrogate(title[length - 1]))
                length--;
            string name = title.Substring(0, length).TrimEnd(' ', '.') + suffix;
            if (!File.Exists(Path.Combine(DirectoryPath, name)) &&
                !entries.Any(entry => string.Equals(entry.FileName, name, StringComparison.OrdinalIgnoreCase)))
                return name;
        }
    }

    [DataContract]
    private sealed class IndexFile
    {
        [DataMember(Name = "version", Order = 0)] public int Version { get; set; }
        [DataMember(Name = "items", Order = 1)] public List<IndexEntry> Items { get; set; }
    }

    [DataContract]
    private sealed class IndexEntry
    {
        [DataMember(Name = "date", Order = 0)] public string Date { get; set; }
        [DataMember(Name = "title", Order = 1)] public string Title { get; set; }
        [DataMember(Name = "credit", Order = 2)] public string Credit { get; set; }
        [DataMember(Name = "file", Order = 3)] public string File { get; set; }
        [DataMember(Name = "size", Order = 4)] public int[] Size { get; set; }
        [DataMember(Name = "favorite", Order = 5)] public bool Favorite { get; set; }
        [DataMember(Name = "rotation", Order = 6)] public bool Rotation { get; set; }
        [DataMember(Name = "tags", Order = 7)] public List<string> Tags { get; set; }
        [DataMember(Name = "appliedAt", Order = 8)] public string AppliedAt { get; set; }
        [DataMember(Name = "id", Order = 9, EmitDefaultValue = false)] public string Id { get; set; }
        [DataMember(Name = "bingPath", Order = 10, EmitDefaultValue = false)] public string BingPath { get; set; }
        [DataMember(Name = "creditLink", Order = 11, EmitDefaultValue = false)] public string CreditLink { get; set; }
        [DataMember(Name = "downloadable", Order = 12, EmitDefaultValue = false)] public bool Downloadable { get; set; }
        [DataMember(Name = "savedAt", Order = 13, EmitDefaultValue = false)] public string SavedAt { get; set; }

        public IndexEntry(SavedWallpaper entry)
        {
            var item = entry.Wallpaper;
            Date = item.Date;
            Title = item.Title;
            Credit = item.Copyright;
            File = entry.FileName;
            Size = new[] { entry.Width, entry.Height };
            Favorite = entry.Favorite;
            Rotation = entry.Rotation;
            Tags = entry.Tags;
            AppliedAt = entry.AppliedAt;
            Id = item.Id;
            BingPath = item.BingPath;
            CreditLink = item.CopyrightLink;
            Downloadable = item.Downloadable;
            SavedAt = entry.SavedAt;
        }

        public SavedWallpaper ToEntry() => new SavedWallpaper
        {
            Wallpaper = new Wallpaper
            {
                Id = Id ?? "local-" + File, Title = Title, Date = Date, Copyright = Credit,
                BingPath = BingPath, CopyrightLink = CreditLink, Downloadable = Downloadable
            },
            FileName = File, Width = Size?.ElementAtOrDefault(0) ?? 0, Height = Size?.ElementAtOrDefault(1) ?? 0,
            Favorite = Favorite, Rotation = Rotation, Tags = Tags ?? new List<string>(), SavedAt = SavedAt, AppliedAt = AppliedAt
        };
    }
}

internal static class JsonFile
{
    public static T Read<T>(string path) where T : class
    {
        try
        {
            using (var stream = File.OpenRead(path))
                return Read<T>(stream);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public static T Read<T>(Stream stream) where T : class =>
        (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);

    public static void Write<T>(string path, T value, string backup = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temp = Path.Combine(Path.GetDirectoryName(path), "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, new UTF8Encoding(false), false, true, "  "))
                    new DataContractJsonSerializer(typeof(T)).WriteObject(writer, value);
                stream.Flush(true);
            }
            if (File.Exists(path))
                File.Replace(temp, path, backup);
            else
                File.Move(temp, path);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
