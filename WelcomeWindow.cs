using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TradeGlass;

// Shown once, on the first launch that created a fresh config. Walks a new
// user through the two setup steps and states plainly what this tool is.
public sealed class WelcomeWindow : Window
{
    public WelcomeWindow(Action openSettings, Action drawRegions)
    {
        Title = "Welcome to TradeGlass";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        Background = new SolidColorBrush(Color.FromRgb(27, 27, 30));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var fg = new SolidColorBrush(Color.FromRgb(225, 225, 228));
        var muted = new SolidColorBrush(Color.FromRgb(150, 150, 155));

        var stack = new StackPanel { Margin = new Thickness(20) };

        stack.Children.Add(new TextBlock
        {
            Text = "TradeGlass enforces your trading time windows.",
            Foreground = fg,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Outside the windows you declare, a click-blocking glass covers your "
                 + "trading platform. It never touches positions or orders, it only "
                 + "stands between you and new entries. Overriding it costs a countdown "
                 + "plus a typed sentence, and every override is logged.",
            Foreground = muted,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 12),
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Two steps to set up:\n"
                 + "1. Settings: declare your trading windows, timezone, market, and platform.\n"
                 + "2. Draw regions: drag rectangles over your order-entry areas (DOMs).",
            Foreground = fg,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
        });

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };

        var settingsBtn = new Button { Content = "1. Open Settings", Padding = new Thickness(12, 6, 12, 6) };
        settingsBtn.Click += (_, _) => openSettings();

        var regionsBtn = new Button
        {
            Content = "2. Draw regions",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(8, 0, 0, 0),
        };
        regionsBtn.Click += (_, _) =>
        {
            drawRegions();
            Close();
        };

        var doneBtn = new Button
        {
            Content = "Close",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(8, 0, 0, 0),
        };
        doneBtn.Click += (_, _) => Close();

        btnRow.Children.Add(settingsBtn);
        btnRow.Children.Add(regionsBtn);
        btnRow.Children.Add(doneBtn);
        stack.Children.Add(btnRow);

        stack.Children.Add(new TextBlock
        {
            Text = "Both are always available later from the tray icon.",
            Foreground = muted,
            FontSize = 11,
            Margin = new Thickness(0, 10, 0, 0),
        });

        Content = stack;
    }
}
