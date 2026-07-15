using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace TradeGlass;

// One overlay per configured region. A normal opaque-enough window that sits
// topmost over the DOMs and swallows every mouse event that lands on it.
// Position and size are applied in raw pixels via SetWindowPos so the dragged
// region coordinates survive multi-monitor and DPI differences.
public sealed class OverlayWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;

    private readonly RegionRect _rect;
    private readonly Action<string> _onOverrideConfirmed;
    private readonly Action? _onManageRequested;
    private readonly string _sentence;
    private readonly int _delaySeconds;
    private readonly bool _manageEnabled;

    private readonly TextBlock _title = new();
    private readonly TextBlock _message = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _quote = new();
    private readonly Button _overrideBtn = new();
    private readonly Button _manageBtn = new();
    private readonly StackPanel _buttonRow = new();
    private readonly TextBlock _countdown = new();
    private readonly StackPanel _typePanel = new();
    private readonly TextBox _typeBox = new();
    private readonly TextBlock _typeError = new();
    private readonly System.Windows.Threading.DispatcherTimer _delayTimer = new();
    private int _remaining;

    public OverlayWindow(RegionRect rect, string sentence, int delaySeconds,
        bool manageEnabled, Action<string> onOverrideConfirmed, Action? onManageRequested)
    {
        _rect = rect;
        _sentence = sentence;
        _delaySeconds = Math.Max(0, delaySeconds);
        _manageEnabled = manageEnabled;
        _onOverrideConfirmed = onOverrideConfirmed;
        _onManageRequested = onManageRequested;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(215, 14, 14, 16));
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;

        _delayTimer.Interval = TimeSpan.FromSeconds(1);
        _delayTimer.Tick += (_, _) => DelayTick();

        BuildUi();
        SourceInitialized += (_, _) => ApplyPixelRect();

        // If the glass hides mid-flow (window opened, override elsewhere),
        // reset the override UI so it never reappears half open.
        IsVisibleChanged += (_, _) => { if (!IsVisible) CancelOverride(); };
    }

    private void BuildUi()
    {
        _title.Text = "Outside your trading window";
        _title.FontSize = 20;
        _title.FontWeight = FontWeights.SemiBold;
        _title.Foreground = new SolidColorBrush(Color.FromRgb(235, 235, 235));
        _title.TextAlignment = TextAlignment.Center;
        _title.TextWrapping = TextWrapping.Wrap;

        _message.Text = "";
        _message.FontSize = 14;
        _message.Foreground = new SolidColorBrush(Color.FromRgb(190, 190, 190));
        _message.TextAlignment = TextAlignment.Center;
        _message.TextWrapping = TextWrapping.Wrap;
        _message.Margin = new Thickness(0, 10, 0, 0);

        _status.Text = "";
        _status.FontSize = 13;
        _status.Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150));
        _status.TextAlignment = TextAlignment.Center;
        _status.Margin = new Thickness(0, 6, 0, 14);

        _overrideBtn.Content = "Override anyway";
        _overrideBtn.Padding = new Thickness(18, 8, 18, 8);
        _overrideBtn.Foreground = new SolidColorBrush(Color.FromRgb(224, 96, 96));
        _overrideBtn.Click += (_, _) => StartDelay();

        _manageBtn.Content = "Manage open position";
        _manageBtn.Padding = new Thickness(18, 8, 18, 8);
        _manageBtn.Margin = new Thickness(0, 0, 10, 0);
        _manageBtn.Click += (_, _) => _onManageRequested?.Invoke();
        _manageBtn.Visibility = _manageEnabled ? Visibility.Visible : Visibility.Collapsed;

        _buttonRow.Orientation = Orientation.Horizontal;
        _buttonRow.HorizontalAlignment = HorizontalAlignment.Center;
        _buttonRow.Children.Add(_manageBtn);
        _buttonRow.Children.Add(_overrideBtn);

        _countdown.FontSize = 13;
        _countdown.Foreground = new SolidColorBrush(Color.FromRgb(224, 178, 96));
        _countdown.TextAlignment = TextAlignment.Center;
        _countdown.Visibility = Visibility.Collapsed;
        _countdown.Margin = new Thickness(0, 4, 0, 0);

        var typeHint = new TextBlock
        {
            Text = $"Type exactly: {_sentence}",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };

        _typeBox.MinWidth = 360;
        _typeBox.FontSize = 13;
        _typeBox.Padding = new Thickness(6);
        _typeBox.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) TryConfirm();
            if (e.Key == System.Windows.Input.Key.Escape) CancelOverride();
        };

        _typeError.Text = "Does not match. Every character, no shortcuts.";
        _typeError.FontSize = 12;
        _typeError.Foreground = new SolidColorBrush(Color.FromRgb(224, 96, 96));
        _typeError.TextAlignment = TextAlignment.Center;
        _typeError.Visibility = Visibility.Collapsed;
        _typeError.Margin = new Thickness(0, 6, 0, 0);

        var confirmBtn = new Button
        {
            Content = "Confirm override",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 10, 8, 0),
        };
        confirmBtn.Click += (_, _) => TryConfirm();

        var cancelBtn = new Button
        {
            Content = "Never mind",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 10, 0, 0),
        };
        cancelBtn.Click += (_, _) => CancelOverride();

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        btnRow.Children.Add(confirmBtn);
        btnRow.Children.Add(cancelBtn);

        _typePanel.Visibility = Visibility.Collapsed;
        _typePanel.Children.Add(typeHint);
        _typePanel.Children.Add(_typeBox);
        _typePanel.Children.Add(_typeError);
        _typePanel.Children.Add(btnRow);

        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 440,
        };
        stack.Children.Add(_title);
        stack.Children.Add(_message);
        stack.Children.Add(_status);
        stack.Children.Add(_buttonRow);
        stack.Children.Add(_countdown);
        stack.Children.Add(_typePanel);

        _quote.FontSize = 12;
        _quote.FontStyle = FontStyles.Italic;
        _quote.Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 135));
        _quote.TextAlignment = TextAlignment.Center;
        _quote.TextWrapping = TextWrapping.Wrap;
        _quote.Margin = new Thickness(0, 22, 0, 0);
        stack.Children.Add(_quote);

        Content = stack;
    }

    private void TryConfirm()
    {
        if (_typeBox.Text.Trim() == _sentence)
        {
            _typeError.Visibility = Visibility.Collapsed;
            _typeBox.Clear();
            CancelOverride();
            _onOverrideConfirmed(_title.Text);
        }
        else
        {
            _typeError.Visibility = Visibility.Visible;
        }
    }

    private void StartDelay()
    {
        _buttonRow.Visibility = Visibility.Collapsed;
        if (_delaySeconds == 0)
        {
            OpenTypePanel();
            return;
        }
        _remaining = _delaySeconds;
        _countdown.Text = $"Impulse check: textbox unlocks in {_remaining}s";
        _countdown.Visibility = Visibility.Visible;
        _delayTimer.Start();
    }

    private void DelayTick()
    {
        _remaining--;
        if (_remaining > 0)
        {
            _countdown.Text = $"Impulse check: textbox unlocks in {_remaining}s";
            return;
        }
        _delayTimer.Stop();
        _countdown.Visibility = Visibility.Collapsed;
        OpenTypePanel();
    }

    private void OpenTypePanel()
    {
        _typePanel.Visibility = Visibility.Visible;
        Activate();
        _typeBox.Focus();
    }

    private void CancelOverride()
    {
        _delayTimer.Stop();
        _countdown.Visibility = Visibility.Collapsed;
        _typePanel.Visibility = Visibility.Collapsed;
        _buttonRow.Visibility = Visibility.Visible;
        _typeBox.Clear();
        _typeError.Visibility = Visibility.Collapsed;
    }

    private void ApplyPixelRect()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, HWND_TOPMOST, _rect.X, _rect.Y, _rect.W, _rect.H, SWP_NOACTIVATE);
    }

    // Called every tick so platforms that fight for z-order lose the fight.
    public void ReassertTopmost()
    {
        if (!IsVisible) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE);
    }

    public void UpdateStatus(string title, string message, string status, string quote)
    {
        _title.Text = title;
        _message.Text = message;
        _status.Text = status;
        _quote.Text = quote;
    }

    // Briefly flash a cap-reached notice in place of the status line.
    public void ShowManageDenied(int cap, int resetMin)
    {
        _status.Text = $"Manage cap reached ({cap}). Wait {resetMin} min, or use Override.";
    }
}