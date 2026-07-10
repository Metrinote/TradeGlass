using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradeGlass;

public sealed class TradingWindow
{
    // Day names: Sun Mon Tue Wed Thu Fri Sat
    public List<string> Days { get; set; } = new();
    public string Start { get; set; } = "09:45";
    public string End { get; set; } = "11:35";

    public bool Matches(DateTime et)
    {
        var dayName = et.DayOfWeek.ToString().Substring(0, 3);
        if (!Days.Contains(dayName)) return false;
        var t = et.TimeOfDay;
        return t >= TimeSpan.Parse(Start) && t < TimeSpan.Parse(End);
    }
}

public sealed class RegionRect
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
}

public sealed class AppConfig
{
    public List<TradingWindow> Windows { get; set; } = new()
    {
        new TradingWindow { Days = new() { "Mon", "Tue", "Wed", "Thu", "Fri" }, Start = "09:45", End = "11:35" },
        new TradingWindow { Days = new() { "Sun", "Mon", "Tue", "Wed", "Thu" }, Start = "19:45", End = "21:15" },
    };

    public List<RegionRect> Regions { get; set; } = new();

    public List<string> PlatformTitleKeywords { get; set; } = new() { "Tradovate", "SuperDOM" };

    public string OverrideSentence { get; set; } = "I am breaking my window rule on purpose";

    public int OverrideUnlockMinutes { get; set; } = 5;

    // Seconds the impulse-check countdown runs before the override textbox
    // even appears. Impulses decay on a timescale of seconds; make them wait.
    public int OverrideDelaySeconds { get; set; } = 30;

    // Quality of life: a soft chime plus toast when a trading window opens
    // (only if a platform window is on screen), and a warning toast this many
    // minutes before the window closes. ChimeOnOpen false or
    // CloseWarningMinutes 0 to disable either.
    public bool ChimeOnOpen { get; set; } = true;
    public int CloseWarningMinutes { get; set; } = 5;

    // When the glass is allowed to patrol at all. Outside these periods it
    // never shows, so pre-market logins (before 9:30 AM, before 7:30 PM)
    // stay friction free. The kill zones are the minutes right before each
    // window opens and everything after each window closes.
    public List<TradingWindow> GuardWindows { get; set; } = new()
    {
        new TradingWindow { Days = new() { "Mon", "Tue", "Wed", "Thu", "Fri" }, Start = "09:30", End = "09:45" },
        new TradingWindow { Days = new() { "Mon", "Tue", "Wed", "Thu", "Fri" }, Start = "11:35", End = "16:45" },
        new TradingWindow { Days = new() { "Sun", "Mon", "Tue", "Wed", "Thu" }, Start = "19:30", End = "19:45" },
        new TradingWindow { Days = new() { "Sun", "Mon", "Tue", "Wed", "Thu" }, Start = "21:15", End = "23:59" },
    };

    // Shown at the bottom of the glass, rotating by day. Edit freely in
    // config.json, no rebuild needed. Best material: your own words from
    // disciplined days, aimed at yourself on undisciplined ones.
    public List<string> FooterQuotes { get; set; } = new()
    {
        "A winning rule-break is more expensive training data than a losing one.",
        "The window is the edge. Everything outside it is a donation.",
        "You wrote these rules on a green month. Trust that version of you.",
        "The market will be here tomorrow. The account has to be too.",
        "You built this glass two days after a win streak took the wheel. It knows why it exists.",
    };

    [JsonIgnore]
    public static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TradeGlass");

    [JsonIgnore]
    public static string ConfigPath => Path.Combine(Dir, "config.json");

    [JsonIgnore]
    public static string LogPath => Path.Combine(Dir, "violations.jsonl");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), JsonOpts);
                if (cfg != null) return cfg;
            }
        }
        catch (Exception ex)
        {
            ViolationLog.Append("config_load_error", ex.Message);
        }
        var fresh = new AppConfig();
        fresh.Save();
        return fresh;
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOpts));
    }
}