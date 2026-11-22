using System;
using System.Collections.Generic;
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

    public static Settings Load() => JsonFile.Read<Settings>(FilePath) ?? new Settings();
    public void Save() => JsonFile.Write(FilePath, this);
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

    public string DisplayDate => DateTime.ParseExact(Date, "yyyyMMdd", CultureInfo.InvariantCulture)
        .ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
}

[DataContract]
internal sealed class SavedWallpaper
{
    [DataMember] public Wallpaper Wallpaper { get; set; }
    [DataMember] public string FileName { get; set; }
    [DataMember] public string SavedAt { get; set; }
    [DataMember] public int Width { get; set; }
    [DataMember] public int Height { get; set; }
}

internal sealed class WallpaperLibrary
{
    public string DirectoryPath { get; }
    private string IndexPath => Path.Combine(DirectoryPath, "wallpapers.json");
    private List<SavedWallpaper> entries;
    public IReadOnlyList<SavedWallpaper> Entries => entries;

    public WallpaperLibrary(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory);
        try
        {
            entries = JsonFile.Read<List<SavedWallpaper>>(IndexPath) ?? new List<SavedWallpaper>();
            if (entries.Any(entry => entry?.Wallpaper == null ||
                !DateTime.TryParseExact(entry.Wallpaper.Date, "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _) ||
                string.IsNullOrWhiteSpace(entry.Wallpaper.Id) || string.IsNullOrWhiteSpace(entry.Wallpaper.Title) ||
                !IsImageName(entry.FileName)) ||
                entries.Select(entry => entry.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count ||
                entries.Select(entry => entry.Wallpaper.Id).Distinct(StringComparer.Ordinal).Count() != entries.Count)
                throw new InvalidDataException("索引中的壁纸信息不完整，或包含重复的图片。");
        }
        catch (SerializationException exception)
        {
            throw new InvalidDataException($"无法读取壁纸索引 {IndexPath}。", exception);
        }
    }

    private static bool IsImageName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase);

    public string PathOf(SavedWallpaper entry) => Path.Combine(DirectoryPath, entry.FileName);
    public SavedWallpaper Find(string id) => entries.Find(entry => entry.Wallpaper.Id == id);

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
            SavedAt = existing?.SavedAt ?? DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture)
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
            JsonFile.Write(IndexPath, next);
        }
        catch
        {
            File.Delete(path);
            throw;
        }
        entries = next;
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
            JsonFile.Write(IndexPath, next);
        }
        catch
        {
            if (hasImage)
                File.Move(staged, path);
            throw;
        }
        entries = next;
        if (hasImage)
            File.Delete(staged);
    }

    private string NextName(Wallpaper wallpaper)
    {
        const int LegacyPathLimit = 259;
        int available = Math.Min(180, LegacyPathLimit - DirectoryPath.Length - 1);
        if (available < 40)
            throw new PathTooLongException("壁纸目录太深，请在设置中选择较短的路径。");

        var invalid = Path.GetInvalidFileNameChars();
        string title = new string(wallpaper.Title.Select(character =>
            Array.IndexOf(invalid, character) >= 0 ? '_' : character).ToArray()).Trim(' ', '.');
        if (title.Length == 0)
            title = "Bing 壁纸";
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
}

internal static class JsonFile
{
    public static T Read<T>(string path) where T : class
    {
        try
        {
            using (var stream = File.OpenRead(path))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public static void Write<T>(string path, T value)
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
                File.Replace(temp, path, null);
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
