using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace BingWallpapers;

internal static class LibraryThumbnails
{
    private const int Size = 384;

    public static Bitmap Read((string Path, long Stamp, long Length) source, CancellationToken token)
    {
        string key;
        using (var hash = SHA256.Create())
            key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(
                $"{source.Path}\n{source.Stamp:X}\n{source.Length:X}"))).Replace("-", "");
        string directory = Path.Combine(Settings.Profile, "cache", "library");
        string path = Path.Combine(directory, $"{key}_{Size}.jpg");
        if (File.Exists(path))
        {
            try
            {
                using (var file = File.OpenRead(path))
                using (var image = Image.FromStream(file))
                    return new Bitmap(image);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                error is ArgumentException)
            {
                Debug.WriteLine(error.Message);
            }
        }

        token.ThrowIfCancellationRequested();
        Bitmap thumbnail;
        using (var file = File.OpenRead(source.Path))
        using (var image = Image.FromStream(file))
        {
            int width = Math.Max(1, (int)((long)Size * image.Width / Math.Max(image.Width, image.Height)));
            thumbnail = BingClient.Resize(image, width);
        }
        if (!token.IsCancellationRequested)
            Save(thumbnail, directory, path);
        return thumbnail;
    }

    private static void Save(Bitmap thumbnail, string directory, string path)
    {
        string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(directory);
            try
            {
                thumbnail.Save(temporary, ImageFormat.Jpeg);
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally { File.Delete(temporary); }
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ExternalException)
        {
            Debug.WriteLine(error.Message);
        }
    }
}
