using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PCControlCenter.Desktop.Views;

public static class UIFactory
{
    public static SolidColorBrush Brush(string hex)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
        catch
        {
            return new SolidColorBrush(Colors.LightSteelBlue);
        }
    }

    public static TextBlock Text(string text, double size = 13, string color = "#66809B") =>
        new() { Text = text, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap };

    public static TextBlock Head(string title) =>
        new() { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#173C58"), Margin = new Thickness(0, 0, 0, 12) };

    public static Border Card(UIElement child) => new()
    {
        Background = new LinearGradientBrush(Color.FromArgb(242, 255, 255, 255), Color.FromArgb(220, 235, 248, 255), new Point(0, 0), new Point(1, 1)),
        BorderBrush = Brush("#FFFFFF"),
        BorderThickness = new Thickness(1.5),
        CornerRadius = new CornerRadius(20),
        Padding = new Thickness(22),
        Margin = new Thickness(0, 0, 0, 16),
        Effect = new DropShadowEffect { Color = (Color)ColorConverter.ConvertFromString("#597D94"), BlurRadius = 18, ShadowDepth = 5, Opacity = 0.05 }
    };

    public static StackPanel Stack(params UIElement[] children)
    {
        var s = new StackPanel();
        foreach (var c in children) s.Children.Add(c);
        return s;
    }

    public static WrapPanel Row(params UIElement[] children)
    {
        var w = new WrapPanel();
        foreach (var c in children) w.Children.Add(c);
        return w;
    }

    public static Grid Two(UIElement a, UIElement b)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new() { Width = new GridLength(16) });
        g.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(a);
        Grid.SetColumn(b, 2);
        g.Children.Add(b);
        return g;
    }

    public static ProgressBar Bar(Brush fill) => new()
    {
        Minimum = 0,
        Maximum = 100,
        Height = 4,
        Foreground = fill,
        Background = Brush("#DCEBF5"),
        BorderThickness = new Thickness(0),
        Margin = new Thickness(0, 10, 0, 0)
    };
}
