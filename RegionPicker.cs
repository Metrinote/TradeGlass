using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TradeGlass;

// Full virtual-screen window for dragging out the DOM regions.
// Rectangle coordinates are captured with GetCursorPos (raw pixels), which
// keeps the saved regions correct even across monitors with different DPI.
// The drawn preview uses WPF coordinates and may look slightly offset on
// mixed-DPI setups; the SAVED pixels are still right.
public sealed class RegionPicker : Window
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private readonly List<RegionRect> _rects = new();
    private readonly Canvas _canvas = new();
    private readonly Action<List<RegionRect>> _onSave;

    private POINT _dragStartPx;
    private System.Windows.Point _dragStartDip;
    private Rectangle? _preview;
    private bool _dragging;

    public RegionPicker(Action<List<RegionRect>> onSave)
    {
        _onSave = onSave;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(90, 10, 10, 10));
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;

        var hint = new TextBlock
        {
            Text = "Drag a rectangle over each DOM. Backspace removes the last one. Enter saves. Esc cancels.",
            Foreground = Brushes.White,
            FontSize = 16,
            Margin = new Thickness(24),
        };

        var root = new Grid();
        root.Children.Add(_canvas);
        root.Children.Add(hint);
        Content = root;

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        KeyDown += OnKey;

        SourceInitialized += (_, _) =>
        {
            var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowPos(hwnd, HWND_TOPMOST, vs.X, vs.Y, vs.Width, vs.Height, 0);
        };
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        GetCursorPos(out _dragStartPx);
        _dragStartDip = e.GetPosition(_canvas);
        _dragging = true;

        _preview = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(90, 170, 250)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(50, 90, 170, 250)),
        };
        Canvas.SetLeft(_preview, _dragStartDip.X);
        Canvas.SetTop(_preview, _dragStartDip.Y);
        _canvas.Children.Add(_preview);
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _preview == null) return;
        var p = e.GetPosition(_canvas);
        Canvas.SetLeft(_preview, Math.Min(p.X, _dragStartDip.X));
        Canvas.SetTop(_preview, Math.Min(p.Y, _dragStartDip.Y));
        _preview.Width = Math.Abs(p.X - _dragStartDip.X);
        _preview.Height = Math.Abs(p.Y - _dragStartDip.Y);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        GetCursorPos(out var endPx);

        var rect = new RegionRect
        {
            X = Math.Min(_dragStartPx.X, endPx.X),
            Y = Math.Min(_dragStartPx.Y, endPx.Y),
            W = Math.Abs(endPx.X - _dragStartPx.X),
            H = Math.Abs(endPx.Y - _dragStartPx.Y),
        };
        if (rect.W < 40 || rect.H < 40)
        {
            if (_preview != null) _canvas.Children.Remove(_preview);
            _preview = null;
            return;
        }
        _rects.Add(rect);
        _preview = null;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                _onSave(_rects);
                Close();
                break;
            case Key.Escape:
                Close();
                break;
            case Key.Back:
                if (_rects.Count > 0)
                {
                    _rects.RemoveAt(_rects.Count - 1);
                    if (_canvas.Children.Count > 0)
                        _canvas.Children.RemoveAt(_canvas.Children.Count - 1);
                }
                break;
        }
    }
}