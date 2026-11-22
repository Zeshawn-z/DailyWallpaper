using System;
using System.Collections.Generic;
using System.Linq;

namespace BingWallpapers;

internal sealed class RotationPlan
{
    private readonly Random random = new Random();
    public IReadOnlyList<DateTimeOffset> Times { get; private set; } = Array.Empty<DateTimeOffset>();
    public DateTimeOffset Started { get; private set; }

    public void Reset(Settings settings, DateTimeOffset now, DateTimeOffset? resume = null)
    {
        Started = now;
        if (settings.RotationEnabled != true)
        {
            Times = Array.Empty<DateTimeOffset>();
            return;
        }
        var times = new List<DateTimeOffset>(5);
        var next = resume > now ? resume.Value : now;
        for (int i = 0; i < 5; i++)
        {
            next = NextTime(settings, next);
            times.Add(next);
        }
        Times = times;
    }

    public void Advance(Settings settings, DateTimeOffset now)
    {
        if (Times.Count < 2 || Times[1] <= now)
        {
            Reset(settings, now);
            return;
        }
        Started = now;
        Times = Times.Skip(1).Concat(new[] { NextTime(settings, Times.Last()) }).ToList();
    }

    private DateTimeOffset NextTime(Settings settings, DateTimeOffset from)
    {
        if (settings.RotationMode == "daily")
        {
            var zone = BingClient.MarketZone(settings.Market);
            var tomorrow = TimeZoneInfo.ConvertTime(from, zone).Date.AddDays(1);
            return new DateTimeOffset(tomorrow, zone.GetUtcOffset(tomorrow)).AddMinutes(5);
        }
        if (settings.RotationMode == "random")
            return from.AddMinutes(random.Next(settings.RandomMinimum, settings.RandomMaximum + 1));
        return from.AddMinutes(settings.Interval);
    }

    public Wallpaper Pick(IReadOnlyList<Wallpaper> pool, Settings settings, bool alternate, string queuedId)
    {
        if (pool.Count == 0)
            return null;
        if (settings.RotationMode == "daily" && !alternate)
            return pool[0].Id == settings.CurrentId ? null : pool[0];
        var excluded = new HashSet<string>(settings.History.Take(10).Select(item => item.Id));
        var available = pool.Where(item => item.Id != settings.CurrentId &&
            (!alternate || item.Id != queuedId)).ToList();
        if (available.Count == 0)
            return null;
        var candidates = settings.AvoidRecent == true ? available.Where(item => !excluded.Contains(item.Id)).ToList() : available;
        if (candidates.Count == 0)
        {
            var order = settings.History.Select(item => item.Id).ToList();
            return available.OrderByDescending(item => order.IndexOf(item.Id)).First();
        }
        if (settings.RotationMode == "random" || alternate)
            return candidates[random.Next(candidates.Count)];
        var ordered = pool.ToList();
        int index = ordered.FindIndex(item => item.Id == settings.CurrentId);
        for (int n = 1; n <= ordered.Count; n++)
        {
            var candidate = ordered[(index + n) % ordered.Count];
            if (candidates.Contains(candidate))
                return candidate;
        }
        return candidates[0];
    }
}
