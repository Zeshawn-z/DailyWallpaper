using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BingWallpapers;

internal sealed class BingClient : IDisposable
{
    private readonly string market;
    private static readonly Regex ImagePath = new Regex(@"^/th\?id=OHR\.[a-zA-Z0-9._-]+$");
    private static string CacheDirectory => Path.Combine(Settings.Profile, "cache");
    private static string FeedPath(string market) =>
        Path.Combine(CacheDirectory, market == "zh-CN" ? "feed.json" : "feed-en-US.json");
    private readonly HttpClient http;
    private readonly SemaphoreSlim imageSlots = new SemaphoreSlim(3);

    public BingClient(Settings settings)
    {
        market = settings.Market;
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            UseCookies = false,
            UseProxy = settings.ProxyMode != "direct"
        };
        if (settings.ProxyMode == "custom")
            handler.Proxy = new WebProxy(ParseProxy(settings.ProxyAddress));

        http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(18),
            MaxResponseContentBufferSize = 24 * 1024 * 1024
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Dayframe/" + Program.Version);
    }

    public static Uri ParseProxy(string address)
    {
        if (!address.Contains("://"))
            address = "http://" + address;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "http" ||
            uri.HostNameType == UriHostNameType.Unknown || uri.Port <= 0 ||
            uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
            throw new ArgumentException(Strings.Get("Enter an HTTP proxy address, for example 127.0.0.1:7890."));
        return uri;
    }

    public List<Wallpaper> CachedFeed() => CachedFeed(market);
    public static List<Wallpaper> CachedFeed(string market) => JsonFile.Read<List<Wallpaper>>(FeedPath(market));

    public static TimeZoneInfo MarketZone(string market) =>
        TimeZoneInfo.FindSystemTimeZoneById(market == "zh-CN" ? "China Standard Time" : "Pacific Standard Time");

    public async Task<List<Wallpaper>> FetchFeedAsync(CancellationToken token)
    {
        var images = await FetchArchiveAsync(market, 0, token).ConfigureAwait(false);
        if (images.Count == 0)
            throw new InvalidDataException(Strings.Get("Bing returned no wallpapers."));
        token.ThrowIfCancellationRequested();
        JsonFile.Write(FeedPath(market), images);
        foreach (var path in Directory.EnumerateFiles(CacheDirectory, "*.jpg"))
        {
            if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-14))
                File.Delete(path);
        }
        return images;
    }

    public async Task<List<Wallpaper>> FetchArchiveAsync(string sourceMarket, int offset, CancellationToken token)
    {
        string host = sourceMarket == "zh-CN" ? "https://cn.bing.com/" : "https://www.bing.com/";
        var archive = new Uri($"{host}HPImageArchive.aspx?format=js&idx={offset}&n=8&mkt={sourceMarket}");
        using (var response = await http.GetAsync(archive, token).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            {
                var data = (ArchiveResponse)new DataContractJsonSerializer(typeof(ArchiveResponse)).ReadObject(stream);
                return data?.Images?.Where(image => image != null).Select(image => ParseEntry(image, sourceMarket))
                    .Where(image => image != null)
                    .GroupBy(image => image.Id).Select(group => group.First())
                    .OrderByDescending(image => image.Date, StringComparer.Ordinal)
                    .Take(8).ToList() ?? new List<Wallpaper>();
            }
        }
    }

    private static Wallpaper ParseEntry(ArchiveImage image, string market)
    {
        if (image.UrlBase == null || !ImagePath.IsMatch(image.UrlBase))
            return null;

        // Bing reports the start in UTC. The displayed day belongs to the selected market.
        string date = DateTime.TryParseExact(image.FullStartDate, "yyyyMMddHHmm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var utc)
            ? TimeZoneInfo.ConvertTimeFromUtc(utc, MarketZone(market)).ToString("yyyyMMdd", CultureInfo.InvariantCulture)
            : market == "zh-CN" ? image.EndDate : image.StartDate;
        if (!DateTime.TryParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return null;

        return new Wallpaper
        {
            Id = image.UrlBase.Substring("/th?id=".Length),
            BingPath = image.UrlBase,
            Date = date,
            Title = string.IsNullOrWhiteSpace(image.Title) ? "Bing wallpaper" : image.Title.Trim(),
            Copyright = image.Copyright ?? "",
            CopyrightLink = image.CopyrightLink ?? "",
            Downloadable = image.Downloadable ?? true
        };
    }

    public async Task<Bitmap> PreviewAsync(Wallpaper wallpaper, string localFile, bool thumbnail, CancellationToken token)
    {
        int width = thumbnail ? 320 : 1366;
        if (localFile != null)
        {
            return await Task.Run(() =>
            {
                using (var stream = File.OpenRead(localFile))
                using (var image = Image.FromStream(stream))
                    return Resize(image, width);
            }, token).ConfigureAwait(false);
        }

        await imageSlots.WaitAsync(token).ConfigureAwait(false);
        try
        {
            string key;
            using (var hash = SHA256.Create())
                key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(wallpaper.Id))).Replace("-", "");
            string path = Path.Combine(CacheDirectory, $"{key}_{width}.jpg");
            if (File.Exists(path))
            {
                return await Task.Run(() =>
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                    using (var image = Image.FromStream(stream))
                        return new Bitmap(image);
                }, token).ConfigureAwait(false);
            }

            var bytes = await GetImageAsync(wallpaper, thumbnail ? "320x180" : "1366x768", token).ConfigureAwait(false);
            using (var source = Image.FromStream(new MemoryStream(bytes), false, true))
            {
                var preview = Resize(source, width);
                try
                {
                    token.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(CacheDirectory);
                    string temp = Path.Combine(CacheDirectory, "." + Guid.NewGuid().ToString("N") + ".tmp");
                    try
                    {
                        preview.Save(temp, ImageFormat.Jpeg);
                        if (!File.Exists(path))
                            File.Move(temp, path);
                    }
                    finally
                    {
                        if (File.Exists(temp))
                            File.Delete(temp);
                    }
                    return preview;
                }
                catch
                {
                    preview.Dispose();
                    throw;
                }
            }
        }
        finally
        {
            imageSlots.Release();
        }
    }

    public async Task<Download> DownloadAsync(Wallpaper wallpaper, CancellationToken token)
    {
        if (!wallpaper.Downloadable)
            throw new InvalidOperationException(Strings.Get("This image is unavailable for download from Bing."));

        var bytes = await GetImageAsync(wallpaper, "UHD", token).ConfigureAwait(false);
        using (var stream = new MemoryStream(bytes))
        using (var image = Image.FromStream(stream, false, true))
            return new Download { Bytes = bytes, Width = image.Width, Height = image.Height };
    }

    private async Task<byte[]> GetImageAsync(Wallpaper wallpaper, string size, CancellationToken token)
    {
        if (wallpaper.BingPath == null || !ImagePath.IsMatch(wallpaper.BingPath))
            throw new InvalidDataException(Strings.Get("Invalid Bing image address."));
        Uri host = wallpaper.BingPath.Contains("_ZH-CN") ? new Uri("https://cn.bing.com/") : new Uri("https://www.bing.com/");
        Uri url = new Uri(host, wallpaper.BingPath + "_" + size + ".jpg");
        using (var response = await http.GetAsync(url, token).ConfigureAwait(false))
        {
            if ((response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.BadRequest) &&
                size != "1920x1080")
                return await GetImageAsync(wallpaper, "1920x1080", token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8)
                throw new InvalidDataException(Strings.Get("Bing did not return a JPEG image."));
            return bytes;
        }
    }

    private static Bitmap Resize(Image source, int maxWidth)
    {
        int width = Math.Min(maxWidth, source.Width);
        int height = Math.Max(1, (int)Math.Round((double)source.Height * width / source.Width));
        var result = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphic = Graphics.FromImage(result))
        {
            graphic.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphic.DrawImage(source, new Rectangle(0, 0, width, height));
        }
        return result;
    }

    public void Dispose() => http.Dispose();

    internal sealed class Download
    {
        public byte[] Bytes { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    [DataContract]
    private sealed class ArchiveResponse
    {
        [DataMember(Name = "images")] public ArchiveImage[] Images { get; set; }
    }

    [DataContract]
    private sealed class ArchiveImage
    {
        [DataMember(Name = "fullstartdate")] public string FullStartDate { get; set; }
        [DataMember(Name = "startdate")] public string StartDate { get; set; }
        [DataMember(Name = "enddate")] public string EndDate { get; set; }
        [DataMember(Name = "urlbase")] public string UrlBase { get; set; }
        [DataMember(Name = "title")] public string Title { get; set; }
        [DataMember(Name = "copyright")] public string Copyright { get; set; }
        [DataMember(Name = "copyrightlink")] public string CopyrightLink { get; set; }
        [DataMember(Name = "wp")] public bool? Downloadable { get; set; }
    }
}
