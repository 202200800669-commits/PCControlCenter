using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using PCControlCenter.Desktop.Services;

namespace PCControlCenter.Desktop.Views;

public sealed class AmbientBackground : Grid
{
    public AmbientBackground()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        SetResourceReference(BackgroundProperty, "WindowSurface");
        Loaded += (_, _) => { Appearance.Changed += Rebuild; Rebuild(); };
        Unloaded += (_, _) => { Appearance.Changed -= Rebuild; Children.Clear(); SetResourceReference(BackgroundProperty, "WindowSurface"); };
        IsVisibleChanged += (_, _) => Rebuild();
    }
    public void Rebuild()
    {
        Children.Clear();
        SetResourceReference(BackgroundProperty, "WindowSurface");
        var p = Appearance.Current;
        if (!p.Animated)
            return;
        bool move = Appearance.Preferences.Motion && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast && IsVisible;
        Color accent = Parse(p.Accent), secondary = Parse(p.Secondary), paper = Parse(p.Background);
        var backdrop = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        var tones = new[] { Mix(paper, accent, .22), Mix(paper, secondary, .38), Mix(paper, accent, .10) };
        for (int i = 0; i < tones.Length; i++)
        {
            var stop = new GradientStop(tones[i], i * .5);
            backdrop.GradientStops.Add(stop);
            if (move)
                AnimateColor(stop, tones[i], tones[(i + 1) % tones.Length], 8 + i * 2);
        }
        Background = backdrop;
        for (int i = 0; i < 4; i++)
        {
            Color main = i % 2 == 0 ? accent : secondary;
            Color nearby = i % 2 == 0 ? secondary : accent;
            main.A = 145;
            nearby.A = 125;
            var fill = new RadialGradientBrush { RadiusX = .5, RadiusY = .5, GradientOrigin = new Point(.48, .5) };
            var center = new GradientStop(main, 0);
            fill.GradientStops.Add(center);
            var middle = new GradientStop(Color.FromArgb(65, main.R, main.G, main.B), .5);
            fill.GradientStops.Add(middle);
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(0, main.R, main.G, main.B), 1));
            var position = new TranslateTransform();
            var rotation = new RotateTransform(i % 2 == 0 ? -32 : 28);
            var transforms = new TransformGroup();
            transforms.Children.Add(rotation);
            transforms.Children.Add(position);
            var glow = new Ellipse
            {
                Width = i < 2 ? 1320 : 1050,
                Height = i < 2 ? 510 : 620,
                HorizontalAlignment = i % 2 == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = i < 2 ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                Margin = new Thickness(i % 2 == 0 ? -380 : 0, i < 2 ? -140 : 0, i % 2 == 1 ? -310 : 0, i >= 2 ? -250 : 0),
                Fill = fill,
                RenderTransform = transforms,
                RenderTransformOrigin = new Point(.5, .5)
            };
            Children.Add(glow);
            if (!move)
                continue;
            Animate(position, TranslateTransform.XProperty, i % 2 == 0 ? -130 : 130, i % 2 == 0 ? 310 : -310, 7 + i * 1.7);
            Animate(position, TranslateTransform.YProperty, i < 2 ? -65 : 65, i < 2 ? 165 : -165, 9 + i * 1.3);
            Animate(rotation, RotateTransform.AngleProperty, i % 2 == 0 ? -42 : 18, i % 2 == 0 ? -12 : 45, 12 + i);
            AnimateColor(center, main, nearby, 6 + i * 1.9);
            AnimateColor(middle, Color.FromArgb(65, main.R, main.G, main.B), Color.FromArgb(65, nearby.R, nearby.G, nearby.B), 6 + i * 1.9);
        }
    }
    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    private static Color Mix(Color first, Color second, double ratio) => Color.FromRgb((byte)(first.R * (1 - ratio) + second.R * ratio), (byte)(first.G * (1 - ratio) + second.G * ratio), (byte)(first.B * (1 - ratio) + second.B * ratio));
    private static void Animate(Animatable target, DependencyProperty property, double from, double to, double seconds)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        Timeline.SetDesiredFrameRate(animation, 30);
        target.BeginAnimation(property, animation);
    }
    private static void AnimateColor(GradientStop target, Color from, Color to, double seconds)
    {
        var animation = new ColorAnimation(from, to, TimeSpan.FromSeconds(seconds)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        Timeline.SetDesiredFrameRate(animation, 30);
        target.BeginAnimation(GradientStop.ColorProperty, animation);
    }
}
