using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using PCControlCenter.Desktop.Services;
using PCControlCenter.Desktop.ViewModels;
using PCControlCenter.Desktop.Views;
using Forms = System.Windows.Forms;
using static PCControlCenter.Desktop.Views.UIFactory;

namespace PCControlCenter.Desktop;

public sealed class MainWindow : Window
{
    private readonly MainViewModel vm = new();
    private readonly Dictionary<string, FrameworkElement> pages = new();
    private readonly Dictionary<string, Button> navButtons = new();
    private readonly List<FrameworkElement> sidebarLabels = new();
    private readonly ContentControl contentHost = new();
    private readonly TextBlock pageTitle = Text("设备总览", 25);
    private readonly TextBlock statusLabel = Text("就绪", 11);
    private readonly DispatcherTimer timer = new();
    private readonly Border sidebar = new();
    private Forms.NotifyIcon? tray;
    private bool allowExit;
    private string selectedPage = "总览";
    private readonly bool preview;

    public MainWindow(bool startMinimized = false, bool preview = false)
    {
        this.preview = preview;
        Title = "PC Control Center";
        Width = 1200;
        Height = 840;
        MinWidth = 940;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "WindowSurface");
        SetResourceReference(ForegroundProperty, "TextPrimary");
        FontFamily = new FontFamily("Segoe UI Variable, Microsoft YaHei UI");
        FontSize = 13;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 36, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(16) });
        try
        {
            Icon = BitmapFrame.Create(new Uri(Path.Combine(AppContext.BaseDirectory, "Control.ico")));
        }
        catch { }
        vm.MinimizeToTray = Appearance.Preferences.MinimizeToTray;
        vm.PollIntervalSeconds = Appearance.Preferences.PollSeconds;
        vm.AccentColor = Appearance.Current.Accent;
        BuildShell();
        SetupViews();
        Navigate("总览");
        Appearance.Changed += OnThemeChanged;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.StatusText))
            {
                statusLabel.Text = vm.StatusText;
                statusLabel.ToolTip = vm.StatusText;
            }
            if (e.PropertyName == nameof(vm.PollIntervalSeconds))
                timer.Interval = TimeSpan.FromSeconds(Math.Clamp(vm.PollIntervalSeconds, 2, 30));
        };
        timer.Interval = TimeSpan.FromSeconds(vm.PollIntervalSeconds);
        timer.Tick += async (_, _) => await vm.RefreshTelemetryAsync();
        Loaded += async (_, _) =>
        {
            if (preview)
                return;
            SetupTray();
            if (startMinimized)
                Hide();
            await vm.InitializeAsync();
            timer.Start();
        };
        StateChanged += (_, _) => { if (!preview && WindowState == WindowState.Minimized && vm.MinimizeToTray) Hide(); };
        Closing += (_, e) =>
        {
            if (!preview && !allowExit && vm.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            timer.Stop();
            tray?.Dispose();
            Appearance.Changed -= OnThemeChanged;
        };
    }
    private void OnThemeChanged()
    {
        vm.AccentColor = Appearance.Current.Accent;
        UpdateNavigation();
    }
    private void BuildShell()
    {
        var root = new Grid();
        root.Children.Add(new AmbientBackground());
        Content = root;
        var outer = new Grid();
        outer.RowDefinitions.Add(new()
        {
            Height = new GridLength(36)
        });
        outer.RowDefinitions.Add(new());
        root.Children.Add(outer);
        var titlebar = new DockPanel { Margin = new Thickness(20, 0, 8, 0) };
        outer.Children.Add(titlebar);
        var winButtons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(winButtons, Dock.Right);
        titlebar.Children.Add(winButtons);
        foreach (var (symbol, name, action) in new[] { ("—", "最小化", (Action)(() => WindowState = WindowState.Minimized)), ("□", "最大化", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized), ("×", "关闭", () => Close()) })
        {
            var b = new Button { Content = symbol, Width = 42, Height = 30, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = name };
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            b.Click += (_, _) => action();
            winButtons.Children.Add(b);
        }
        var windowTitle = Text("PC Control Center", 11);
        windowTitle.VerticalAlignment = VerticalAlignment.Center;
        titlebar.Children.Add(windowTitle);
        var body = new Grid();
        body.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        body.ColumnDefinitions.Add(new());
        Grid.SetRow(body, 1);
        outer.Children.Add(body);
        sidebar.Width = Appearance.Preferences.SidebarExpanded ? 208 : 76;
        sidebar.Margin = new Thickness(12, 12, 0, 16);
        sidebar.Padding = new Thickness(12, 18, 12, 16);
        sidebar.CornerRadius = new CornerRadius(22);
        sidebar.BorderThickness = new Thickness(1);
        sidebar.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        sidebar.SetResourceReference(Border.BorderBrushProperty, "SurfaceLine");
        body.Children.Add(sidebar);
        var rail = new DockPanel();
        sidebar.Child = rail;
        var brandRow = new Grid { Margin = new Thickness(0, 0, 0, 30) };
        brandRow.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(48)
        });
        brandRow.ColumnDefinitions.Add(new());
        var logo = new Image { Width = 36, Height = 36, Stretch = Stretch.Uniform };
        try
        {
            logo.Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "FanMark.png")));
        }
        catch { }
        brandRow.Children.Add(logo);
        var brand = Stack(Text("PC CONTROL", 14), Text("电脑控制中心", 10));
        brand.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(brand, 1);
        brandRow.Children.Add(brand);
        sidebarLabels.Add(brand);
        DockPanel.SetDock(brandRow, Dock.Top);
        rail.Children.Add(brandRow);
        var device = Text("本机设备", 11);
        device.TextTrimming = TextTrimming.CharacterEllipsis;
        device.TextWrapping = TextWrapping.NoWrap;
        device.Margin = new Thickness(8, 18, 0, 0);
        sidebarLabels.Add(device);
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.DeviceTitle)) { device.Text = vm.DeviceTitle; device.ToolTip = vm.DeviceTitle; } };
        DockPanel.SetDock(device, Dock.Bottom);
        rail.Children.Add(device);
        var utilityNav = new StackPanel();
        var utilityCard = Inset(utilityNav);
        utilityCard.Padding = new Thickness(0, 4, 0, 0);
        utilityCard.Margin = new Thickness(0);
        DockPanel.SetDock(utilityCard, Dock.Bottom);
        rail.Children.Add(utilityCard);
        var nav = new StackPanel();
        var mainNavCard = Inset(nav);
        mainNavCard.Padding = new Thickness(0, 4, 0, 0);
        mainNavCard.Margin = new Thickness(0);
        mainNavCard.VerticalAlignment = VerticalAlignment.Top;
        rail.Children.Add(mainNavCard);
        foreach (var (key, icon, label) in new[] { ("总览", "home", "设备总览"), ("散热", "fan", "性能与散热"), ("设备", "device", "设备与电源"), ("场景", "profiles", "场景配置"), ("反馈", "feedback", "测试与反馈"), ("设置", "settings", "外观与设置") })
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new()
            {
                Width = new GridLength(24)
            });
            row.ColumnDefinitions.Add(new());
            row.Children.Add(UIFactory.Icon(icon));
            var labelView = Text(label, 13, "#173C58");
            labelView.TextWrapping = TextWrapping.NoWrap;
            labelView.VerticalAlignment = VerticalAlignment.Center;
            labelView.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(labelView, 1);
            row.Children.Add(labelView);
            sidebarLabels.Add(labelView);
            var button = new Button { Content = row, Height = 48, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 0, 6), HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = label };
            System.Windows.Automation.AutomationProperties.SetName(button, label);
            string target = key;
            button.Click += (_, _) => Navigate(target);
            navButtons[key] = button;
            (key is "反馈" or "设置" ? utilityNav : nav).Children.Add(button);
        }
        SetSidebar(Appearance.Preferences.SidebarExpanded, false);
        var main = new Grid { Margin = new Thickness(28, 16, 28, 18) };
        main.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        main.RowDefinitions.Add(new());
        main.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        Grid.SetColumn(main, 1);
        body.Children.Add(main);
        var heading = new DockPanel { Margin = new Thickness(0, 4, 0, 22) };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(tools, Dock.Right);
        heading.Children.Add(tools);
        var themes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
        foreach (var palette in Appearance.Palettes)
        {
            var swatch = new Border { Width = 13, Height = 13, CornerRadius = new CornerRadius(7), Background = Brush(palette.Id == "white" ? "#D1D7DF" : palette.Id == "black" ? "#26303C" : palette.Accent) };
            var b = new Button { Content = swatch, Width = 28, Height = 32, MinHeight = 32, Padding = new Thickness(6), Margin = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = palette.Name };
            System.Windows.Automation.AutomationProperties.SetName(b, palette.Name + "主题");
            b.Click += (_, _) => Appearance.Apply(palette.Id);
            themes.Children.Add(b);
        }
        tools.Children.Add(themes);
        tools.Children.Add(IconButton("refresh", "刷新", async () => { if (!preview) await vm.RefreshTelemetryAsync(); }));
        var menu = IconButton("menu", "展开或收起导航", () => SetSidebar(!Appearance.Preferences.SidebarExpanded));
        menu.Margin = new Thickness(0, 0, 14, 0);
        heading.Children.Add(menu);
        pageTitle.FontWeight = FontWeights.SemiBold;
        pageTitle.VerticalAlignment = VerticalAlignment.Center;
        heading.Children.Add(pageTitle);
        main.Children.Add(heading);
        var scroller = new ScrollViewer { Content = contentHost, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 8, 0) };
        Grid.SetRow(scroller, 1);
        main.Children.Add(scroller);
        statusLabel.TextWrapping = TextWrapping.NoWrap;
        statusLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        statusLabel.Margin = new Thickness(0, 10, 0, 0);
        Grid.SetRow(statusLabel, 2);
        main.Children.Add(statusLabel);
    }
    public void SetSidebar(bool expanded, bool animate = true)
    {
        Appearance.Preferences.SidebarExpanded = expanded;
        foreach (var label in sidebarLabels)
            label.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in navButtons.Values)
            button.ToolTip = expanded ? null : System.Windows.Automation.AutomationProperties.GetName(button);
        var target = expanded ? 208d : 76d;
        if (animate && Appearance.Preferences.Motion && SystemParameters.ClientAreaAnimation)
            sidebar.BeginAnimation(WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        else
        {
            sidebar.BeginAnimation(WidthProperty, null);
            sidebar.Width = target;
        }
        if (animate)
            Appearance.Save();
    }
    private void SetupViews()
    {
        pages["总览"] = new OverviewView(vm);
        pages["散热"] = new CoolingView(vm);
        pages["设备"] = new DeviceView(vm);
        pages["场景"] = new ProfilesView(vm);
        pages["反馈"] = new FeedbackView(vm);
        var settings = new SettingsView(vm);
        settings.RequestExit += () => { allowExit = true; Close(); };
        pages["设置"] = settings;
    }
    public void Navigate(string key)
    {
        if (!pages.TryGetValue(key, out var page))
            return;
        selectedPage = key;
        contentHost.Content = page;
        pageTitle.Text = key switch
        {
            "总览" => "设备总览",
            "散热" => "性能与散热",
            "设备" => "设备与电源",
            "场景" => "场景配置",
            "反馈" => "测试与反馈",
            _ => "外观与设置"
        };
        UpdateNavigation();
    }
    private void UpdateNavigation()
    {
        foreach (var (key, button) in navButtons)
        {
            if (key == selectedPage)
                button.SetResourceReference(BackgroundProperty, "InsetSurface");
            else
                button.Background = Brushes.Transparent;
            button.SetResourceReference(BorderBrushProperty, key == selectedPage ? "Accent" : "SurfaceLine");
            button.BorderThickness = key == selectedPage ? new Thickness(3, 0, 0, 0) : new Thickness(0);
        }
    }
    public void RestoreAndActivate()
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(new Action(RestoreAndActivate));
            return;
        }
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }
    private void SetupTray()
    {
        try
        {
            tray = new Forms.NotifyIcon { Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Control.ico")), Text = "PC Control Center", Visible = true };
            tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) RestoreAndActivate(); };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("打开", null, (_, _) => RestoreAndActivate());
            menu.Items.Add("退出", null, (_, _) => { allowExit = true; Close(); });
            tray.ContextMenuStrip = menu;
        }
        catch { }
    }
}
