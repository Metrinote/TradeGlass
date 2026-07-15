using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TradeGlass;

// The settings hub, opened from the tray. Edits the shared AppConfig in
// place and invokes onSaved so the coordinator can persist and re-apply
// live. Everything a non-technical user needs; the config file remains the
// advanced escape hatch (custom guard windows stay JSON-only).
public sealed class SettingsWindow : Window
{
    private static readonly string[] DayNames = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
    private static readonly string[] KnownPlatforms =
        { "Tradovate", "SuperDOM", "NinjaTrader", "TopstepX", "TradingView", "Quantower" };

    private static readonly Brush Bg = new SolidColorBrush(Color.FromRgb(27, 27, 30));
    private static readonly Brush PanelBg = new SolidColorBrush(Color.FromRgb(37, 37, 41));
    private static readonly Brush Fg = new SolidColorBrush(Color.FromRgb(225, 225, 228));
    private static readonly Brush FgMuted = new SolidColorBrush(Color.FromRgb(150, 150, 155));
    private static readonly Brush ErrorFg = new SolidColorBrush(Color.FromRgb(224, 96, 96));

    private sealed class WindowRow
    {
        public CheckBox[] Days = new CheckBox[7];
        public TextBox Start = new();
        public TextBox End = new();
        public Border Root = new();
    }

    private readonly AppConfig _cfg;
    private readonly Action _onSaved;

    private readonly StackPanel _windowRowsHost = new();
    private readonly List<WindowRow> _rows = new();
    private readonly CheckBox _autoGuards = new();
    private readonly TextBox _armBox = new();
    private readonly ComboBox _tzCombo = new();
    private readonly ComboBox _profileCombo = new();
    private readonly Dictionary<string, CheckBox> _platformChecks = new();
    private readonly TextBox _customPlatforms = new();
    private readonly TextBox _sentenceBox = new();
    private readonly TextBox _customMessageBox = new();
    private readonly TextBox _delayBox = new();
    private readonly TextBox _unlockBox = new();
    private readonly CheckBox _manageCheck = new();
    private readonly TextBox _manageDurBox = new();
    private readonly TextBox _manageCapBox = new();
    private readonly CheckBox _chimeCheck = new();
    private readonly TextBox _warnBox = new();
    private readonly TextBox _quotesBox = new();
    private readonly TextBlock _error = new();

    public SettingsWindow(AppConfig cfg, Action onSaved)
    {
        _cfg = cfg;
        _onSaved = onSaved;

        Title = "TradeGlass Settings";
        Width = 620;
        // Clamp to the work area: a fixed 760 on a laptop screen would push
        // the Save button below the bottom edge with no way to reach it.
        Height = Math.Min(760, SystemParameters.WorkArea.Height - 40);
        Background = Bg;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var content = new StackPanel { Margin = new Thickness(18) };

        content.Children.Add(Section("Trading windows",
            "When you are ALLOWED to enter trades. Everything else gets the glass."));
        _windowRowsHost.Margin = new Thickness(0, 4, 0, 0);
        content.Children.Add(_windowRowsHost);
        var addBtn = MakeButton("Add window");
        addBtn.HorizontalAlignment = HorizontalAlignment.Left;
        addBtn.Margin = new Thickness(0, 6, 0, 0);
        addBtn.Click += (_, _) => AddRow(new TradingWindow
        {
            Days = new() { "Mon", "Tue", "Wed", "Thu", "Fri" },
            Start = "09:30",
            End = "16:00",
        });
        content.Children.Add(addBtn);

        content.Children.Add(Section("Guard behavior",
            "The glass arms shortly before each window opens and stays on duty after each close."));
        _autoGuards.Content = "Derive guard periods automatically (recommended)";
        _autoGuards.Foreground = Fg;
        _autoGuards.IsChecked = _cfg.AutoDeriveGuards;
        content.Children.Add(_autoGuards);
        content.Children.Add(LabeledBox("Arm the glass this many minutes before each window opens:",
            _armBox, _cfg.ArmMinutesBefore.ToString(), 60));
        var advNote = new TextBlock
        {
            Text = "Unchecked: the GuardWindows list in config.json is used verbatim (advanced).",
            Foreground = FgMuted,
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        content.Children.Add(advNote);

        content.Children.Add(Section("Clock and market",
            "Windows are in YOUR timezone. Market hours run on the exchange's own clock."));
        foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
        {
            var item = new ComboBoxItem { Content = tz.DisplayName, Tag = tz.Id };
            _tzCombo.Items.Add(item);
            if (tz.Id == _cfg.TimeZoneId) _tzCombo.SelectedItem = item;
        }
        // If the configured id matched nothing (corrupt or hand-mangled),
        // fall back to Eastern rather than item zero, which is UTC-12 and
        // would get silently written into config on save.
        if (_tzCombo.SelectedItem == null)
        {
            foreach (ComboBoxItem item in _tzCombo.Items)
            {
                if ((string)item.Tag == "Eastern Standard Time")
                {
                    _tzCombo.SelectedItem = item;
                    break;
                }
            }
            if (_tzCombo.SelectedItem == null && _tzCombo.Items.Count > 0)
                _tzCombo.SelectedIndex = 0;
        }
        _tzCombo.Margin = new Thickness(0, 4, 0, 6);
        content.Children.Add(_tzCombo);

        AddProfileItem("Futures (CME Globex hours)", "futures");
        AddProfileItem("US equities (9:30 to 4:00 ET)", "us_equities");
        AddProfileItem("Always open (crypto and 24/7 markets)", "always_open");
        if (_profileCombo.SelectedItem == null) _profileCombo.SelectedIndex = 0;
        content.Children.Add(_profileCombo);

        content.Children.Add(Section("Trading platforms",
            "Windows whose title contains any of these are treated as trading surfaces."));
        var platWrap = new WrapPanel();
        foreach (var name in KnownPlatforms)
        {
            var cb = new CheckBox
            {
                Content = name,
                Foreground = Fg,
                Margin = new Thickness(0, 2, 14, 2),
                IsChecked = _cfg.PlatformTitleKeywords
                    .Any(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)),
            };
            _platformChecks[name] = cb;
            platWrap.Children.Add(cb);
        }
        content.Children.Add(platWrap);
        var customList = _cfg.PlatformTitleKeywords
            .Where(k => !KnownPlatforms.Contains(k, StringComparer.OrdinalIgnoreCase));
        content.Children.Add(LabeledBox("Custom keywords (comma separated):",
            _customPlatforms, string.Join(", ", customList), 300));

        content.Children.Add(Section("Override friction",
            "What it costs to break your own rules. Every override is logged."));
        content.Children.Add(LabeledBox("Sentence that must be typed exactly:",
            _sentenceBox, _cfg.OverrideSentence, 420));
        content.Children.Add(LabeledBox("Custom glass message (optional, replaces the default lock text; your own numbers hit hardest):",
            _customMessageBox, _cfg.CustomGlassMessage, 420));
        content.Children.Add(LabeledBox("Impulse-check countdown before typing is allowed (seconds):",
            _delayBox, _cfg.OverrideDelaySeconds.ToString(), 60));
        content.Children.Add(LabeledBox("Unlock duration after a successful override (minutes):",
            _unlockBox, _cfg.OverrideUnlockMinutes.ToString(), 60));

        content.Children.Add(Section("Manage open position",
            "Lets you briefly lift the glass to adjust stops or cancel orders on a trade you already have open. No countdown, no typed sentence, always logged."));
        _manageCheck.Content = "Show the \"Manage open position\" button on the glass";
        _manageCheck.Foreground = Fg;
        _manageCheck.IsChecked = _cfg.AllowManageBypass;
        content.Children.Add(_manageCheck);
        content.Children.Add(LabeledBox("Seconds the glass lifts per manage click:",
            _manageDurBox, _cfg.ManageDurationSeconds.ToString(), 60));
        content.Children.Add(LabeledBox("Max consecutive manage clicks (0 = unlimited, resets after a few minutes locked):",
            _manageCapBox, _cfg.ManageMaxConsecutive.ToString(), 60));

        content.Children.Add(Section("Notifications", ""));
        _chimeCheck.Content = "Chime and toast when a trading window opens";
        _chimeCheck.Foreground = Fg;
        _chimeCheck.IsChecked = _cfg.ChimeOnOpen;
        content.Children.Add(_chimeCheck);
        content.Children.Add(LabeledBox("Warn this many minutes before a window closes (0 disables):",
            _warnBox, _cfg.CloseWarningMinutes.ToString(), 60));

        content.Children.Add(Section("Glass footer quotes",
            "One per line, rotates daily. Your own words from disciplined days work best."));
        _quotesBox.AcceptsReturn = true;
        _quotesBox.TextWrapping = TextWrapping.Wrap;
        _quotesBox.Height = 110;
        _quotesBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        StyleBox(_quotesBox);
        _quotesBox.Text = string.Join(Environment.NewLine, _cfg.FooterQuotes);
        content.Children.Add(_quotesBox);

        _error.Foreground = ErrorFg;
        _error.TextWrapping = TextWrapping.Wrap;
        _error.Margin = new Thickness(0, 12, 0, 0);
        _error.Visibility = Visibility.Collapsed;
        content.Children.Add(_error);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var saveBtn = MakeButton("Save and apply");
        saveBtn.Click += (_, _) => TrySave();
        var cancelBtn = MakeButton("Cancel");
        cancelBtn.Margin = new Thickness(8, 0, 0, 0);
        cancelBtn.Click += (_, _) => Close();
        btnRow.Children.Add(saveBtn);
        btnRow.Children.Add(cancelBtn);
        content.Children.Add(btnRow);

        Content = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        foreach (var w in _cfg.Windows) AddRow(w);
        if (_rows.Count == 0)
            AddRow(new TradingWindow { Days = new() { "Mon", "Tue", "Wed", "Thu", "Fri" }, Start = "09:30", End = "16:00" });
    }

    private void AddProfileItem(string label, string value)
    {
        var item = new ComboBoxItem { Content = label, Tag = value };
        _profileCombo.Items.Add(item);
        if (string.Equals(_cfg.MarketHoursProfile, value, StringComparison.OrdinalIgnoreCase))
            _profileCombo.SelectedItem = item;
    }

    private void AddRow(TradingWindow w)
    {
        var row = new WindowRow();
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        for (int i = 0; i < 7; i++)
        {
            var cb = new CheckBox
            {
                Content = DayNames[i],
                Foreground = Fg,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsChecked = w.Days.Contains(DayNames[i]),
            };
            row.Days[i] = cb;
            panel.Children.Add(cb);
        }

        row.Start.Text = w.Start;
        row.Start.Width = 52;
        StyleBox(row.Start);
        row.Start.Margin = new Thickness(8, 0, 0, 0);
        panel.Children.Add(row.Start);

        panel.Children.Add(new TextBlock
        {
            Text = "to",
            Foreground = FgMuted,
            Margin = new Thickness(6, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        row.End.Text = w.End;
        row.End.Width = 52;
        StyleBox(row.End);
        panel.Children.Add(row.End);

        var remove = MakeButton("Remove");
        remove.Margin = new Thickness(10, 0, 0, 0);
        remove.Click += (_, _) =>
        {
            _rows.Remove(row);
            _windowRowsHost.Children.Remove(row.Root);
        };
        panel.Children.Add(remove);

        row.Root = new Border
        {
            Background = PanelBg,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 3, 0, 3),
            Child = panel,
        };

        _rows.Add(row);
        _windowRowsHost.Children.Add(row.Root);
    }

    private void TrySave()
    {
        var problems = new List<string>();

        var windows = new List<TradingWindow>();
        for (int r = 0; r < _rows.Count; r++)
        {
            var row = _rows[r];
            var days = new List<string>();
            for (int i = 0; i < 7; i++)
                if (row.Days[i].IsChecked == true) days.Add(DayNames[i]);

            if (days.Count == 0)
            {
                problems.Add($"Window {r + 1}: no days selected.");
                continue;
            }
            if (!TimeSpan.TryParse(row.Start.Text.Trim(), out var s))
            {
                problems.Add($"Window {r + 1}: start time '{row.Start.Text}' is not valid. Use 24h HH:mm, e.g. 09:45.");
                continue;
            }
            if (!TimeSpan.TryParse(row.End.Text.Trim(), out var e))
            {
                problems.Add($"Window {r + 1}: end time '{row.End.Text}' is not valid. Use 24h HH:mm, e.g. 11:35.");
                continue;
            }
            if (e <= s)
            {
                problems.Add($"Window {r + 1}: end must be after start (windows cannot cross midnight).");
                continue;
            }
            windows.Add(new TradingWindow
            {
                Days = days,
                Start = $"{(int)s.TotalHours:00}:{s.Minutes:00}",
                End = $"{(int)e.TotalHours:00}:{e.Minutes:00}",
            });
        }
        if (windows.Count == 0)
            problems.Add("At least one valid trading window is required.");

        if (!int.TryParse(_armBox.Text.Trim(), out var arm) || arm < 0 || arm > 120)
            problems.Add("Arm minutes must be a number between 0 and 120.");
        if (!int.TryParse(_delayBox.Text.Trim(), out var delay) || delay < 0 || delay > 600)
            problems.Add("Impulse-check seconds must be a number between 0 and 600.");
        if (!int.TryParse(_unlockBox.Text.Trim(), out var unlock) || unlock < 1 || unlock > 120)
            problems.Add("Unlock minutes must be a number between 1 and 120.");
        if (!int.TryParse(_manageDurBox.Text.Trim(), out var manageDur) || manageDur < 5 || manageDur > 600)
            problems.Add("Manage seconds must be a number between 5 and 600.");
        if (!int.TryParse(_manageCapBox.Text.Trim(), out var manageCap) || manageCap < 0 || manageCap > 50)
            problems.Add("Manage cap must be a number between 0 and 50 (0 = unlimited).");
        if (!int.TryParse(_warnBox.Text.Trim(), out var warn) || warn < 0 || warn > 60)
            problems.Add("Close warning minutes must be a number between 0 and 60.");

        var sentence = _sentenceBox.Text.Trim();
        if (sentence.Length < 10)
            problems.Add("The override sentence must be at least 10 characters. It is supposed to cost something.");

        var platforms = new List<string>();
        foreach (var kv in _platformChecks)
            if (kv.Value.IsChecked == true) platforms.Add(kv.Key);
        foreach (var raw in _customPlatforms.Text.Split(','))
        {
            var k = raw.Trim();
            if (k.Length > 0 && !platforms.Contains(k, StringComparer.OrdinalIgnoreCase))
                platforms.Add(k);
        }
        if (platforms.Count == 0)
            problems.Add("At least one platform keyword is required, otherwise the glass never appears.");

        if (problems.Count > 0)
        {
            _error.Text = string.Join(Environment.NewLine, problems);
            _error.Visibility = Visibility.Visible;
            return;
        }

        _cfg.Windows = windows;
        _cfg.AutoDeriveGuards = _autoGuards.IsChecked == true;
        _cfg.ArmMinutesBefore = arm;
        _cfg.TimeZoneId = (_tzCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? _cfg.TimeZoneId;
        _cfg.MarketHoursProfile = (_profileCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? _cfg.MarketHoursProfile;
        _cfg.PlatformTitleKeywords = platforms;
        _cfg.OverrideSentence = sentence;
        _cfg.CustomGlassMessage = _customMessageBox.Text.Trim();
        _cfg.OverrideDelaySeconds = delay;
        _cfg.OverrideUnlockMinutes = unlock;
        _cfg.AllowManageBypass = _manageCheck.IsChecked == true;
        _cfg.ManageDurationSeconds = manageDur;
        _cfg.ManageMaxConsecutive = manageCap;
        _cfg.ChimeOnOpen = _chimeCheck.IsChecked == true;
        _cfg.CloseWarningMinutes = warn;
        _cfg.FooterQuotes = _quotesBox.Text
            .Split('\n')
            .Select(q => q.Trim().TrimEnd('\r'))
            .Where(q => q.Length > 0)
            .ToList();

        Close();
        _onSaved();
    }

    private static TextBlock Section(string title, string subtitle)
    {
        var block = new TextBlock
        {
            Foreground = Fg,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 16, 0, 2),
            TextWrapping = TextWrapping.Wrap,
        };
        block.Inlines.Add(title);
        if (subtitle.Length > 0)
        {
            block.Inlines.Add(new System.Windows.Documents.LineBreak());
            block.Inlines.Add(new System.Windows.Documents.Run(subtitle)
            {
                FontSize = 11,
                FontWeight = FontWeights.Normal,
                Foreground = FgMuted,
            });
        }
        return block;
    }

    private StackPanel LabeledBox(string label, TextBox box, string initial, double width)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = FgMuted,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 2),
            TextWrapping = TextWrapping.Wrap,
        });
        box.Text = initial;
        box.Width = width;
        box.HorizontalAlignment = HorizontalAlignment.Left;
        StyleBox(box);
        panel.Children.Add(box);
        return panel;
    }

    private static void StyleBox(TextBox box)
    {
        box.Background = PanelBg;
        box.Foreground = Fg;
        box.BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 76));
        box.Padding = new Thickness(4, 3, 4, 3);
        box.CaretBrush = Fg;
    }

    private static Button MakeButton(string label)
    {
        return new Button
        {
            Content = label,
            Padding = new Thickness(12, 5, 12, 5),
        };
    }
}