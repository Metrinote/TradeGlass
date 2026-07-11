using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace TradeGlass;

public static class Program
{
    private static AppConfig _cfg = null!;
    private static readonly List<OverlayWindow> _overlays = new();
    private static Application _app = null!;
    private static WinForms.NotifyIcon _tray = null!;
    private static DateTime _overrideUntil = DateTime.MinValue;
    private static DateTime _pauseUntil = DateTime.MinValue;
    private static bool _overlaysShown;
    private static bool _warnedNoRegions;
    private static bool _firstEval = true;
    private static bool _lastInWindow;
    private static DateTime _lastWarnedWindowEnd = DateTime.MinValue;

    [STAThread]
    public static void Main()
    {
        using var mutex = new Mutex(true, "TradeGlass_SingleInstance", out bool isNew);
        if (!isNew) return;

        _cfg = AppConfig.Load();
        Schedule.Initialize(_cfg);
        ViolationLog.Append("app_start", "TradeGlass started");

        _app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        SetupTray();
        SurfaceStartupNotices();
        RebuildOverlays();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => Evaluate();
        timer.Start();

        _app.Exit += (_, _) => _tray.Visible = false;
        _app.Run();
    }

    private static void SetupTray()
    {
        _tray = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Shield,
            Text = "TradeGlass",
            Visible = true,
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add("Configure regions", null, (_, _) => OpenRegionPicker());
        menu.Items.Add("View violation log", null, (_, _) => OpenLog());
        menu.Items.Add("Pause for 1 hour (logged)", null, (_, _) => Pause());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit (logged)", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
    }

    // Config recovery and timezone fallback messages, surfaced once at
    // startup so problems are never silent.
    private static void SurfaceStartupNotices()
    {
        var notices = new List<string>();
        if (AppConfig.LoadNotice != null) notices.Add(AppConfig.LoadNotice);
        if (Schedule.InitNotice != null) notices.Add(Schedule.InitNotice);
        if (notices.Count == 0) return;
        _tray.BalloonTipTitle = "TradeGlass notice";
        _tray.BalloonTipText = string.Join(" ", notices);
        _tray.ShowBalloonTip(10000);
    }

    private static SettingsWindow? _settings;

    private static void OpenSettings()
    {
        if (_settings != null)
        {
            _settings.Activate();
            return;
        }
        _settings = new SettingsWindow(_cfg, ApplySettings);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
        _settings.Activate();
    }

    // Live apply: persist, re-derive schedule state, rebuild overlays with
    // the new sentence and delay, reset notification dedupe. No restart.
    private static void ApplySettings()
    {
        _cfg.Save();
        Schedule.Initialize(_cfg);
        RebuildOverlays();
        _lastWarnedWindowEnd = DateTime.MinValue;
        _warnedNoRegions = false;
        ViolationLog.Append("settings_changed", "settings updated from settings window");
        if (Schedule.InitNotice != null)
        {
            _tray.BalloonTipTitle = "TradeGlass notice";
            _tray.BalloonTipText = Schedule.InitNotice;
            _tray.ShowBalloonTip(10000);
        }
    }

    private static void OpenRegionPicker()
    {
        HideOverlays();
        var picker = new RegionPicker(rects =>
        {
            _cfg.Regions = rects;
            _cfg.Save();
            ViolationLog.Append("regions_configured", $"{rects.Count} regions saved");
            RebuildOverlays();
        });
        picker.Show();
        picker.Activate();
    }

    private static void OpenLog()
    {
        try
        {
            if (!System.IO.File.Exists(AppConfig.LogPath))
                System.IO.File.WriteAllText(AppConfig.LogPath, "");
            Process.Start("notepad.exe", AppConfig.LogPath);
        }
        catch { }
    }

    private static void Pause()
    {
        _pauseUntil = Schedule.Now().AddHours(1);
        ViolationLog.Append("pause", "paused for 1 hour from tray");
        HideOverlays();
    }

    private static void ExitApp()
    {
        ViolationLog.Append("app_exit", "exited from tray");
        _tray.Visible = false;
        _app.Shutdown();
    }

    private static void RebuildOverlays()
    {
        foreach (var o in _overlays) o.Close();
        _overlays.Clear();
        foreach (var r in _cfg.Regions)
        {
            _overlays.Add(new OverlayWindow(r, _cfg.OverrideSentence, _cfg.OverrideDelaySeconds, OnOverrideConfirmed));
        }
        _overlaysShown = false;
    }

    private static void OnOverrideConfirmed(string context)
    {
        _overrideUntil = Schedule.Now().AddMinutes(_cfg.OverrideUnlockMinutes);
        ViolationLog.Append("override",
            $"typed override, unlocked {_cfg.OverrideUnlockMinutes} min, context: {context}");
        HideOverlays();
    }

    private static void Evaluate()
    {
        var now = Schedule.Now();

        bool marketOpen = Schedule.IsMarketOpen();
        bool inWindow = Schedule.InAllowedWindow(_cfg, now);
        bool inGuard = Schedule.InGuardPeriod(_cfg, now);
        bool overridden = now < _overrideUntil;
        bool paused = now < _pauseUntil;
        bool platformUp = PlatformDetector.AnyPlatformVisible(_cfg.PlatformTitleKeywords);

        bool shouldGlass = marketOpen && inGuard && !inWindow && !overridden && !paused && platformUp;

        NotifyOpenAndClose(now, inWindow, platformUp);

        if (shouldGlass && _cfg.Regions.Count == 0)
        {
            if (!_warnedNoRegions)
            {
                _warnedNoRegions = true;
                _tray.BalloonTipTitle = "TradeGlass has no regions";
                _tray.BalloonTipText = "Trading platform detected outside your window, but no regions are configured. Right click the tray icon to configure.";
                _tray.ShowBalloonTip(10000);
                ViolationLog.Append("no_regions_warning", "platform up outside window with no regions configured");
            }
            return;
        }

        if (shouldGlass)
        {
            string title = "Outside your trading window";
            string msg = GlassMessage(now.TimeOfDay);
            string status = Schedule.StatusLine(_cfg, now);
            string quote = _cfg.FooterQuotes.Count > 0
                ? _cfg.FooterQuotes[now.DayOfYear % _cfg.FooterQuotes.Count]
                : "";

            foreach (var o in _overlays)
            {
                o.UpdateStatus(title, msg, status, quote);
                if (!_overlaysShown) o.Show();
                o.ReassertTopmost();
            }
            _overlaysShown = true;
        }
        else if (_overlaysShown)
        {
            HideOverlays();
        }
    }

    private static void HideOverlays()
    {
        foreach (var o in _overlays)
        {
            if (o.IsVisible) o.Hide();
        }
        _overlaysShown = false;
    }

    // Soft chime plus toast when a window opens, warning toast shortly
    // before it closes. Both only fire with a platform on screen, and the
    // first evaluation after launch never chimes (starting the app inside a
    // window is not the window opening).
    private static void NotifyOpenAndClose(DateTime now, bool inWindow, bool platformUp)
    {
        if (_firstEval)
        {
            _firstEval = false;
            _lastInWindow = inWindow;
            return;
        }

        if (inWindow && !_lastInWindow && platformUp && _cfg.ChimeOnOpen)
        {
            System.Media.SystemSounds.Asterisk.Play();
            _tray.BalloonTipTitle = "Window open";
            _tray.BalloonTipText = "Trading window is open. Follow the rules and good hunting.";
            _tray.ShowBalloonTip(4000);
        }
        _lastInWindow = inWindow;

        if (inWindow && platformUp && _cfg.CloseWarningMinutes > 0)
        {
            var end = Schedule.CurrentWindowEnd(_cfg, now);
            if (end != null)
            {
                var remaining = end.Value - now;
                if (remaining.TotalMinutes <= _cfg.CloseWarningMinutes && end.Value != _lastWarnedWindowEnd)
                {
                    _lastWarnedWindowEnd = end.Value;
                    System.Media.SystemSounds.Exclamation.Play();
                    _tray.BalloonTipTitle = "Window closing";
                    _tray.BalloonTipText = $"Trading window closes in about {Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))} minutes. Manage what is open, do not initiate.";
                    _tray.ShowBalloonTip(8000);
                }
            }
        }
    }

    // Times referenced in these strings should be kept in sync with the
    // config windows if those ever change. Rewrite the wording over time:
    // the glass works best when it quotes your own violation history.
    private static string GlassMessage(TimeSpan t)
    {
        if (t < new TimeSpan(9, 45, 0))
            return "Four months of Lucid data: every entry before 9:45 AM an expense. Zero for three, about $230 donated. Follow your rules!";
        if (t < new TimeSpan(17, 0, 0))
            return "Morning window closed at 11:35 AM ET. Whatever the chart is doing now, tomorrow has setups too. Positions already open are yours to manage.";
        if (t < new TimeSpan(19, 45, 0))
            return "Evening window opens 7:45 PM ET. The first 15 minutes of the session are for watching, not entering.";
        return "Evening window closed at 9:15 PM ET. If no confirmation came by 9, the session is probably a dud anyway. Positions already open are yours to manage.";
    }
}