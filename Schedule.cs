using System;
using System.Collections.Generic;
using System.Linq;

namespace TradeGlass;

public static class Schedule
{
    private static readonly TimeZoneInfo Eastern =
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    private static TimeZoneInfo _userTz = Eastern;
    private static string _profile = "futures";

    // Set when Initialize() had to fall back, so the tray can surface it.
    public static string? InitNotice { get; private set; }

    // Call once at startup and again whenever config changes.
    public static void Initialize(AppConfig cfg)
    {
        InitNotice = null;
        try
        {
            _userTz = TimeZoneInfo.FindSystemTimeZoneById(cfg.TimeZoneId);
        }
        catch
        {
            _userTz = Eastern;
            InitNotice = $"Timezone id '{cfg.TimeZoneId}' was not recognized. Falling back to Eastern Time. Fix TimeZoneId in config.";
            ViolationLog.Append("timezone_fallback", InitNotice);
        }
        _profile = (cfg.MarketHoursProfile ?? "futures").Trim().ToLowerInvariant();
    }

    // The trader's clock. All windows, guards, and log timestamps use this.
    public static DateTime Now() =>
        TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.Utc, _userTz);

    // Exchange clocks are the exchange's own, independent of the trader's
    // timezone, so market-open is evaluated in ET for the US calendars.
    private static DateTime NowEt() =>
        TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.Utc, Eastern);

    public static bool IsMarketOpen()
    {
        switch (_profile)
        {
            case "always_open":
                return true;
            case "us_equities":
            {
                var et = NowEt();
                if (et.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
                var t = et.TimeOfDay;
                return t >= new TimeSpan(9, 30, 0) && t < new TimeSpan(16, 0, 0);
            }
            default:
                return FuturesOpen(NowEt());
        }
    }

    // CME Globex equity index and energy hours, ET:
    // opens Sunday 6:00 PM, closes Friday 5:00 PM,
    // daily maintenance break 5:00 to 6:00 PM Monday through Thursday.
    // Exchange holidays are not modeled; on a holiday the glass may patrol
    // a shut market, which is harmless.
    private static bool FuturesOpen(DateTime et)
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

    public static bool InAllowedWindow(AppConfig cfg, DateTime local) =>
        cfg.Windows.Any(w => w.Matches(local));

    public static bool InGuardPeriod(AppConfig cfg, DateTime local) =>
        EffectiveGuards(cfg).Any(w => w.Matches(local));

    // Guard periods: when the glass is allowed to exist at all. Either the
    // user's explicit list (advanced), or derived from the trading windows:
    // arm ArmMinutesBefore each open (the pre-window rush zone), and stay on
    // duty after each close until the day session winds down per the market
    // profile. Time before the arm point stays friction free for logins.
    public static IReadOnlyList<TradingWindow> EffectiveGuards(AppConfig cfg)
    {
        if (!cfg.AutoDeriveGuards) return cfg.GuardWindows;

        var guards = new List<TradingWindow>();
        foreach (var w in cfg.Windows)
        {
            TimeSpan start, end;
            try
            {
                start = TimeSpan.Parse(w.Start);
                end = TimeSpan.Parse(w.End);
            }
            catch
            {
                continue; // malformed window; skip rather than crash the guard
            }

            var armStart = start - TimeSpan.FromMinutes(Math.Max(0, cfg.ArmMinutesBefore));
            if (armStart < TimeSpan.Zero) armStart = TimeSpan.Zero;
            if (armStart < start)
            {
                guards.Add(new TradingWindow { Days = w.Days, Start = Fmt(armStart), End = w.Start });
            }

            var postEnd = PostGuardEnd(end);
            if (postEnd > end)
            {
                guards.Add(new TradingWindow { Days = w.Days, Start = w.End, End = Fmt(postEnd) });
            }
        }
        return guards;
    }

    // How long the glass stays on duty after a window closes. Futures: until
    // the 5 PM day-session close if the window ended before it, else until
    // midnight. Equities: until the 4 PM cash close, else midnight.
    // Always-open markets: until midnight. Interpreted on the trader's
    // clock, which matches exchange time for ET traders; non-ET futures
    // traders can switch to explicit GuardWindows if this approximation
    // does not fit their session.
    private static TimeSpan PostGuardEnd(TimeSpan windowEnd)
    {
        var midnight = new TimeSpan(23, 59, 0);
        return _profile switch
        {
            "us_equities" => windowEnd < new TimeSpan(16, 0, 0) ? new TimeSpan(16, 0, 0) : midnight,
            "futures" => windowEnd < new TimeSpan(17, 0, 0) ? new TimeSpan(17, 0, 0) : midnight,
            _ => midnight,
        };
    }

    private static string Fmt(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}";

    // End time of the allowed window containing the given local time, if
    // any. If overlapping windows ever exist, the latest end wins so the
    // close warning fires once.
    public static DateTime? CurrentWindowEnd(AppConfig cfg, DateTime local)
    {
        DateTime? latest = null;
        foreach (var w in cfg.Windows)
        {
            if (!w.Matches(local)) continue;
            var end = local.Date.Add(TimeSpan.Parse(w.End));
            if (latest == null || end > latest) latest = end;
        }
        return latest;
    }

    // Human status line for the overlay, e.g. how long until the next boundary.
    public static string StatusLine(AppConfig cfg, DateTime local)
    {
        TimeSpan? best = null;
        string label = "";
        foreach (var w in cfg.Windows)
        {
            TimeSpan start;
            try { start = TimeSpan.Parse(w.Start); } catch { continue; }
            for (int addDays = 0; addDays <= 7; addDays++)
            {
                var candidate = local.Date.AddDays(addDays).Add(start);
                if (candidate <= local) continue;
                var dayName = candidate.DayOfWeek.ToString().Substring(0, 3);
                if (!w.Days.Contains(dayName)) continue;
                var wait = candidate - local;
                if (best == null || wait < best)
                {
                    best = wait;
                    label = $"next window opens {candidate:ddd h:mm tt}";
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