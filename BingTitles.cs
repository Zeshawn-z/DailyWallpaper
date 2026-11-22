using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace BingWallpapers;

internal sealed class BingTitles
{
    private static readonly Regex MarketSuffix = new Regex(@"^(OHR\..+)_(EN-US|ZH-CN)[0-9]+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static string CachePath => Path.Combine(Settings.Profile, "cache", "titles.json");
    private readonly Dictionary<string, TitlePair> titles = new Dictionary<string, TitlePair>(StringComparer.OrdinalIgnoreCase);

    public void Load()
    {
        var cached = JsonFile.Read<Dictionary<string, TitlePair>>(CachePath);
        if (cached != null)
            foreach (var pair in cached)
                if (pair.Value != null)
                    titles[pair.Key] = pair.Value;
    }

    public bool Add(IEnumerable<Wallpaper> images)
    {
        bool changed = false;
        foreach (var image in images)
        {
            var match = MarketSuffix.Match(image.Id);
            if (!match.Success || image.CustomTitle || string.IsNullOrWhiteSpace(image.Title) || image.Title == "Bing wallpaper")
                continue;
            string key = match.Groups[1].Value;
            if (!titles.TryGetValue(key, out var pair))
                titles[key] = pair = new TitlePair();
            bool chinese = match.Groups[2].Value.Equals("ZH-CN", StringComparison.OrdinalIgnoreCase);
            if ((chinese ? pair.Chinese : pair.English) == image.Title)
                continue;
            if (chinese) pair.Chinese = image.Title;
            else pair.English = image.Title;
            changed = true;
        }
        return changed;
    }

    public void Save() => JsonFile.Write(CachePath, titles);

    public string Get(Wallpaper image, string language) =>
        image == null ? null : image.CustomTitle ? image.Title : Get(image.Id, image.Title, language);

    public string Get(string id, string original, string language)
    {
        var match = MarketSuffix.Match(id ?? "");
        if (!match.Success || !titles.TryGetValue(match.Groups[1].Value, out var pair))
            return original;
        // A title in an older index may have been edited before the custom-title flag existed.
        string source = match.Groups[2].Value.Equals("ZH-CN", StringComparison.OrdinalIgnoreCase) ? pair.Chinese : pair.English;
        if (source != null && source != original && original != "Bing wallpaper")
            return original;
        return (language == "zh-CN" ? pair.Chinese : pair.English) ?? original;
    }

    public bool Missing(Wallpaper image, string language)
    {
        var match = MarketSuffix.Match(image.Id);
        return match.Success && (!titles.TryGetValue(match.Groups[1].Value, out var pair) ||
            string.IsNullOrEmpty(language == "zh-CN" ? pair.Chinese : pair.English));
    }

    [DataContract]
    private sealed class TitlePair
    {
        [DataMember] public string English { get; set; }
        [DataMember] public string Chinese { get; set; }
    }
}
