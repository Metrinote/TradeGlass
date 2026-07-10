using System;
using System.Linq;

namespace TradeGlass;

public static class Schedule
{
    private static readonly TimeZoneInfo Eastern =
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    public static DateTime NowEt() =>
        TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.Utc, Eastern);

    // CME Globex equity index and energy hours, ET:
    // opens Sunday 6:00 PM, closes Friday 5:00 PM,
    // daily maintenance break 5:00 to 6:00 PM Monday through Thursday.
    public static bool IsMarketOpen(DateTime et)
    {
        var d = et.DayOfWeek;
        var t = et.TimeOfDay;
        var five = new TimeSpan(17, 0, 0);
        var six = new TimeSpan(18, 0, 0);

        if (d == DayOfWeek.Saturday) return false;
        if (d == DayOfWeek.Sunday && t < six) return false;
        if (d == DayOfWeek.Friday && t >= five) return false;
        if (d is DayOfWeek.Monday or DayOfWeek.Tuesday or DayOfWeek.Wednesday or DayOfWeek.Thursday
            && t >= five && t < six) return false;
        return true;
    }

    public static bool InAllowedWindow(AppConfig cfg, DateTime et) =>
        cfg.Windows.Any(w => w.Matches(et));

    public static bool InGuardPeriod(AppConfig cfg, DateTime et) =>
        cfg.GuardWindows.Any(w => w.Matches(et));

    // End time of the allowed window containing et, if any. If overlapping
    // windows ever exist, the latest end wins so the warning fires once.
    public static DateTime? CurrentWindowEnd(AppConfig cfg, DateTime et)
    {
        DateTime? latest = null;
        foreach (var w in cfg.Windows)
        {
            if (!w.Matches(et)) continue;
            var end = et.Date.Add(TimeSpan.Parse(w.End));
            if (latest == null || end > latest) latest = end;
        }
        return latest;
    }

    // Human status line for the overlay, e.g. how long until the next boundary.
    public static string StatusLine(AppConfig cfg, DateTime et)
    {
        TimeSpan? best = null;
        string label = "";
        foreach (var w in cfg.Windows)
        {
            var start = TimeSpan.Parse(w.Start);
            for (int addDays = 0; addDays <= 7; addDays++)
            {
                var candidate = et.Date.AddDays(addDays).Add(start);
                if (candidate <= et) continue;
                var dayName = candidate.DayOfWeek.ToString().Substring(0, 3);
                if (!w.Days.Contains(dayName)) continue;
                var wait = candidate - et;
                if (best == null || wait < best)
                {
                    best = wait;
                    label = $"next window opens {candidate:ddd h:mm tt} ET";
                }
                break;
            }
        }
        if (best == null) return "no upcoming window found in config";
        if (best.Value.TotalMinutes < 90)
            return $"opens in {(int)Math.Ceiling(best.Value.TotalMinutes)} min";
        return label;
    }
}