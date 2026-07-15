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

    public bool Matches(DateTime local)
    {
        var dayName = local.DayOfWeek.ToString().Substring(0, 3);
        if (!Days.Contains(dayName)) return false;
        // TryParse, not Parse: a malformed hand-edited time must never
        // crash the evaluate loop. A window that cannot be read never matches.
        if (!TimeSpan.TryParse(Start, out var s)) return false;
        if (!TimeSpan.TryParse(End, out var e)) return false;
        var t = local.TimeOfDay;
        return t >= s && t < e;
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
    // Bump when the config shape changes and add a migration branch in Load().
    public const int CurrentSchemaVersion = 2;

    // 0 means the file predates versioning (v1). Migration fills the gaps.
    public int SchemaVersion { get; set; }

    // Windows timezone id, e.g. "Eastern Standard Time", "Central Standard
    // Time", "GMT Standard Time", "AUS Eastern Standard Time". All windows
    // and guards are interpreted in this zone. DST handled automatically.
    public string TimeZoneId { get; set; } = "Eastern Standard Time";

    // Which market calendar suppresses the glass when there is nothing to
    // guard. One of: futures | us_equities | always_open.
    // Exchange hours are evaluated on the exchange's own clock (ET),
    // independent of TimeZoneId.
    public string MarketHoursProfile { get; set; } = "futures";

    // When true, guard periods are derived from Windows automatically:
    // the glass arms ArmMinutesBefore each window opens, and stays on duty
    // after each close until the day session winds down. When false, the
    // explicit GuardWindows list below is used verbatim (advanced).
    public bool AutoDeriveGuards { get; set; } = true;

    public int ArmMinutesBefore { get; set; } = 15;

    public List<TradingWindow> Windows { get; set; } = new()
    {
        new TradingWindow { Days = new() { "Mon", "Tue", "Wed", "Thu", "Fri" }, Start = "09:45", End = "11:35" },
        new TradingWindow { Days = new() { "Sun", "Mon", "Tue", "Wed", "Thu" }, Start = "19:45", End = "21:15" },
    };

    // Only consulted when AutoDeriveGuards is false.
    public List<TradingWindow> GuardWindows { get; set; } = new();

    public List<RegionRect> Regions { get; set; } = new();

    public List<string> PlatformTitleKeywords { get; set; } = new() { "Tradovate", "SuperDOM" };

    public string OverrideSentence { get; set; } = "I am breaking my window rule on purpose";

    public int OverrideUnlockMinutes { get; set; } = 5;

    // Seconds the impulse-check countdown runs before the override textbox
    // even appears. Impulses decay on a timescale of seconds; make them wait.
    public int OverrideDelaySeconds { get; set; } = 30;

    // Manage-open-position bypass. Lets a trader who is already in a position
    // lift the glass briefly to adjust stops or cancel working orders, without
    // the override friction, because managing an existing trade is not the
    // rule-break the glass targets. Logged as its own event type.
    public bool AllowManageBypass { get; set; } = true;

    // How long the glass lifts per manage click, in seconds.
    public int ManageDurationSeconds { get; set; } = 60;

    // 0 means unlimited re-clicks (free management, logged). Above 0 caps
    // consecutive manage clicks; the counter resets after the glass has been
    // continuously locked for ManageResetMinutes, proving you stepped away.
    public int ManageMaxConsecutive { get; set; } = 0;
    public int ManageResetMinutes { get; set; } = 3;

    // Quality of life: a soft chime plus toast when a trading window opens
    // (only if a platform window is on screen), and a warning toast this many
    // minutes before the window closes. ChimeOnOpen false or
    // CloseWarningMinutes 0 to disable either.
    public bool ChimeOnOpen { get; set; } = true;
    public int CloseWarningMinutes { get; set; } = 5;

    // Optional. When set, replaces the default lock-screen message. Best
    // material: your own numbers ("every pre-open entry this quarter lost").
    public string CustomGlassMessage { get; set; } = "";

    // Shown at the bottom of the glass, rotating by day. Edit freely, no
    // rebuild needed. Best material: your own words from disciplined days,
    // aimed at yourself on undisciplined ones.
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

    // Set when Load() had to recover from a broken or migrated config, so the
    // tray can tell the user instead of failing silently.
    [JsonIgnore]
    public static string? LoadNotice { get; private set; }

    // True when Load() created a brand new config, i.e. a first run.
    [JsonIgnore]
    public static bool CreatedFresh { get; private set; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static AppConfig Load()
    {
        LoadNotice = null;
        CreatedFresh = false;

        if (!File.Exists(ConfigPath))
        {
            var fresh = new AppConfig { SchemaVersion = CurrentSchemaVersion };
            fresh.Save();
            CreatedFresh = true;
            return fresh;
        }

        AppConfig? cfg = null;
        try
        {
            cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), JsonOpts);
        }
        catch (Exception ex)
        {
            // Never silently destroy a hand-edited config. Back it up,
            // report, and start from defaults.
            var backup = ConfigPath + $".bad-{DateTime.Now:yyyyMMdd-HHmmss}";
            try { File.Copy(ConfigPath, backup, overwrite: true); } catch { }
            ViolationLog.Append("config_load_error", $"{ex.Message}; original backed up to {backup}");
            LoadNotice = $"Config file could not be read and was reset to defaults. Your original was saved as {Path.GetFileName(backup)}.";
        }

        if (cfg == null)
        {
            var fresh = new AppConfig { SchemaVersion = CurrentSchemaVersion };
            fresh.Save();
            return fresh;
        }

        if (cfg.SchemaVersion < CurrentSchemaVersion)
        {
            Migrate(cfg);
            cfg.Save();
        }
        else if (cfg.SchemaVersion > CurrentSchemaVersion)
        {
            ViolationLog.Append("config_newer_schema",
                $"config schema {cfg.SchemaVersion} is newer than app schema {CurrentSchemaVersion}; proceeding best effort");
        }

        return cfg;
    }

    private static void Migrate(AppConfig cfg)
    {
        // v1 (SchemaVersion 0) to v2: timezone, market profile, and derived
        // guards did not exist. Property initializers already supplied sane
        // defaults for the new fields during deserialization. One behavioral
        // decision: a v1 file carrying explicit GuardWindows keeps them
        // verbatim, so migration never changes when the glass appears.
        if (cfg.SchemaVersion < 2)
        {
            if (cfg.GuardWindows.Count > 0)
            {
                cfg.AutoDeriveGuards = false;
                ViolationLog.Append("config_migrated",
                    "v1 config migrated to v2; existing guard windows preserved verbatim (AutoDeriveGuards=false)");
                LoadNotice = "Config upgraded to v2. Your existing guard windows were kept exactly as they were.";
            }
            else
            {
                cfg.AutoDeriveGuards = true;
                ViolationLog.Append("config_migrated", "v1 config migrated to v2; guards now auto-derived");
            }
        }

        cfg.SchemaVersion = CurrentSchemaVersion;
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOpts));
    }
}