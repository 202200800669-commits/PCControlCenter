using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PCControlCenter.Desktop.Views;

public static class UIFactory
{
    public static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    public static TextBlock Text(string text, double size = 13, string color = "#66809B")
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.5 };
        t.SetResourceReference(TextBlock.ForegroundProperty, size >= 16 || color is "#173C58" or "#315772" or "#224A65" or "#1C4965" ? "TextPrimary" : "TextMuted");
        return t;
    }
    public static TextBlock Head(string title)
    {
        var t = Text(title, 15);
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        t.FontWeight = FontWeights.SemiBold;
        t.Margin = new Thickness(0, 0, 0, 16);
        return t;
    }
    public static Border Card(UIElement child)
    {
        var b = new Border { Child = child, CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(1), Padding = new Thickness(24), Margin = new Thickness(0, 0, 0, 16) };
        b.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        b.SetResourceReference(Border.BorderBrushProperty, "SurfaceLine");
        return b;
    }
    public static Border Inset(UIElement child)
    {
        var b = Card(child);
        b.CornerRadius = new CornerRadius(14);
        b.Padding = new Thickness(16);
        b.Margin = new Thickness(0, 0, 0, 12);
        b.SetResourceReference(Border.BackgroundProperty, "InsetSurface");
        return b;
    }
    public static StackPanel Stack(params UIElement[] children)
    {
        var s = new StackPanel();
        foreach (var c in children)
            s.Children.Add(c);
        return s;
    }
    public static WrapPanel Row(params UIElement[] children)
    {
        var s = new WrapPanel();
        foreach (var c in children)
            s.Children.Add(c);
        return s;
    }
    public static Grid Two(UIElement a, UIElement b)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new());
        g.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(16)
        });
        g.ColumnDefinitions.Add(new());
        g.Children.Add(a);
        Grid.SetColumn(b, 2);
        g.Children.Add(b);
        return g;
    }
    public static ProgressBar Bar(Brush fill)
    {
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 5, BorderThickness = new Thickness(0), Margin = new Thickness(0, 14, 0, 10) };
        bar.SetResourceReference(Control.ForegroundProperty, "Accent");
        bar.SetResourceReference(Control.BackgroundProperty, "TrackSurface");
        return bar;
    }
    public static Button ActionButton(string text, Action action, bool primary = false)
    {
        var b = new Button { Content = text };
        if (primary)
        {
            b.SetResourceReference(Control.BackgroundProperty, "Accent");
            b.SetResourceReference(Control.ForegroundProperty, "AccentText");
        }
        b.Click += (_, _) => action();
        return b;
    }
    public static FrameworkElement Icon(string name, double size = 20)
    {
        var data = name switch
        {
            "home" => "M3,10 L12,3 21,10 M5,9 V21 H10 V14 H14 V21 H19 V9",
            "fan" => "M12,10 C4,3 6,1 10,2 C15,3 17,8 14,11 M14,12 C23,8 24,12 21,16 C18,20 14,19 12,15 M11,14 C11,24 6,22 4,18 C2,13 6,10 10,11 M14,12 A2,2 0 1 1 10,12 A2,2 0 1 1 14,12",
            "device" => "M3,4 H21 V16 H3 Z M8,21 H16 M12,16 V21",
            "profiles" => "M4,5 H20 M4,12 H20 M4,19 H20 M8,2 V8 M16,9 V15 M10,16 V22",
            "settings" => "M9,3 H15 L16,6 19,7 22,10 V14 L19,16 18,19 15,21 H9 L7,18 4,17 2,14 V10 L5,8 6,5 Z M16,12 A4,4 0 1 1 8,12 A4,4 0 1 1 16,12",
            "feedback" => "M3,3 H21 V17 H12 L6,22 V17 H3 Z M7,8 H17 M7,12 H14",
            "menu" => "M4,5 H20 M4,12 H20 M4,19 H20",
            "refresh" => "M20,10 A8,8 0 1 0 19,17 M20,3 V10 H13",
            "chevron" => "M9,5 L16,12 9,19",
            _ => "M5,5 H19 V19 H5 Z"
        };
        var path = new Path { Data = Geometry.Parse(data), StrokeThickness = 1.65, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        path.SetResourceReference(Shape.StrokeProperty, "TextMuted");
        return new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform, Child = new Canvas { Width = 24, Height = 24, Children = { path } } };
    }
    public static Button IconButton(string icon, string label, Action action)
    {
        var b = new Button { Content = Icon(icon), Width = 40, Height = 40, Padding = new Thickness(9), Margin = new Thickness(0), ToolTip = label };
        AutomationProperties.SetName(b, label);
        b.Click += (_, _) => action();
        return b;
    }
}
