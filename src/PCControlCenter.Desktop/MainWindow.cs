using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using PCControlCenter.Desktop.ViewModels;
using PCControlCenter.Desktop.Views;
using static PCControlCenter.Desktop.Views.UIFactory;

namespace PCControlCenter.Desktop;

public sealed class MainWindow : Window
{
    private readonly MainViewModel vm = new();
    private readonly Dictionary<string, FrameworkElement> pages = new();
    private readonly Dictionary<string, Button> navButtons = new();
    private readonly ContentControl contentHost = new();
    private readonly TextBlock pageTitle = Text("设备总览", 24, "#224A65");
    private readonly TextBlock statusLabel = Text("正在连接本机硬件…", 11, "#7A96AA");
    private readonly DispatcherTimer timer = new();
    private Forms.NotifyIcon? tray;
    private bool allowExit;

    public MainWindow(bool startMinimized = false)
    {
        Title = "PC Control Center";
        Width = 1140;
        Height = 780;
        MinWidth = 900;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush("#F2F7FB");
        Foreground = Brush("#173C58");
        FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
        FontSize = 13;
        WindowStyle = WindowStyle.None;

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 38,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(16)
        });

        try
        {
            var icoPath = Path.Combine(AppContext.BaseDirectory, "Control.ico");
            if (File.Exists(icoPath))
                Icon = BitmapFrame.Create(new Uri(icoPath));
        }
        catch { }

        BuildShell();
        SetupViews();
        Navigate("总览");

        timer.Interval = TimeSpan.FromSeconds(vm.PollIntervalSeconds);
        timer.Tick += async (s, e) => await vm.RefreshTelemetryAsync();

        Loaded += async (s, e) =>
        {
            SetupTray();
            if (startMinimized)
            {
                Hide();
                tray?.ShowBalloonTip(1500, "PC Control Center", "已在后台静默运行，双击托盘图标打开主界面。", Forms.ToolTipIcon.Info);
            }
            await vm.InitializeAsync();
            timer.Start();
        };

        StateChanged += (s, e) =>
        {
            if (WindowState == WindowState.Minimized && vm.MinimizeToTray)
            {
                Hide();
                tray?.ShowBalloonTip(1000, "PC Control Center", "已收起到系统托盘，双击托盘图标恢复窗口。", Forms.ToolTipIcon.Info);
            }
        };

        Closing += (s, e) =>
        {
            if (!allowExit && vm.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                tray?.ShowBalloonTip(1000, "PC Control Center", "已最小化到托盘；右键托盘可彻底退出。", Forms.ToolTipIcon.Info);
                return;
            }
            timer.Stop();
            tray?.Dispose();
        };
    }

    private void BuildShell()
    {
        var outer = new Grid();
        outer.RowDefinitions.Add(new()
        {
            Height = new GridLength(38)
        });
        outer.RowDefinitions.Add(new()
        {
            Height = new GridLength(1, GridUnitType.Star)
        });
        Content = outer;

        // Title Bar
        var titlebar = new DockPanel { Margin = new Thickness(18, 0, 8, 0) };
        outer.Children.Add(titlebar);

        var winButtons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(winButtons, Dock.Right);
        titlebar.Children.Add(winButtons);

        foreach (var (symbol, action) in new[]
        {
            ("—", (Action)(() => WindowState = WindowState.Minimized)),
            ("□", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized),
            ("×", () => Close())
        })
        {
            var b = new Button
            {
                Content = symbol,
                Width = 36,
                Height = 28,
                Margin = new Thickness(1, 4, 1, 4),
                Padding = new Thickness(0),
                Background = Brush("#00FFFFFF"),
                FontSize = 14
            };
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            b.Click += (s, e) => action();
            winButtons.Children.Add(b);
        }

        var topTitle = Text("PC Control Center · 多品牌电脑控制中心", 11, "#7390A3");
        topTitle.VerticalAlignment = VerticalAlignment.Center;
        titlebar.Children.Add(topTitle);

        // Body Grid
        var body = new Grid();
        Grid.SetRow(body, 1);
        outer.Children.Add(body);
        body.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(180)
        });
        body.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });

        // Sidebar
        var sidebar = new DockPanel { Margin = new Thickness(16, 16, 12, 18) };
        body.Children.Add(sidebar);

        var brand = Stack(
            Text("PC CONTROL", 18, "#224A65"),
            Text(vm.DeviceTitle, 11, "#7F9CB0")
        );
        brand.Margin = new Thickness(8, 0, 0, 26);
        DockPanel.SetDock(brand, Dock.Top);
        sidebar.Children.Add(brand);

        var navMenu = new StackPanel();
        sidebar.Children.Add(navMenu);

        foreach (var (key, icon, label) in new[]
        {
            ("总览", "◈", "设备总览"),
            ("散热", "≋", "散热控制"),
            ("设备", "▣", "设备设置"),
            ("场景", "◇", "我的配置"),
            ("设置", "⚙", "常规设置")
        })
        {
            string navKey = key;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Text(icon, 18, "#6C96AF"));
            var t = Text(label, 13, "#315772");
            t.Margin = new Thickness(12, 2, 0, 0);
            row.Children.Add(t);

            var b = new Button
            {
                Content = row,
                Padding = new Thickness(12, 10, 10, 10),
                Margin = new Thickness(0, 0, 0, 8),
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            b.Click += (s, e) => Navigate(navKey);
            navButtons[navKey] = b;
            navMenu.Children.Add(b);
        }

        // Main Area
        var mainArea = new Grid { Margin = new Thickness(16, 10, 24, 16) };
        Grid.SetColumn(mainArea, 1);
        body.Children.Add(mainArea);
        mainArea.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        mainArea.RowDefinitions.Add(new()
        {
            Height = new GridLength(1, GridUnitType.Star)
        });
        mainArea.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });

        // Heading
        var heading = new DockPanel { Margin = new Thickness(6, 0, 0, 18) };
        var refreshBtn = new Button { Content = "↻", Width = 38, Height = 36, Padding = new Thickness(0), FontSize = 18 };
        refreshBtn.Click += async (s, e) => await vm.RefreshTelemetryAsync();
        DockPanel.SetDock(refreshBtn, Dock.Right);
        heading.Children.Add(refreshBtn);

        pageTitle.FontWeight = FontWeights.SemiBold;
        heading.Children.Add(pageTitle);
        mainArea.Children.Add(heading);

        // Content ScrollViewer
        var scroller = new ScrollViewer
        {
            Content = contentHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(6, 2, 10, 6)
        };
        Grid.SetRow(scroller, 1);
        mainArea.Children.Add(scroller);

        // Bottom Status
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(vm.StatusText))
                statusLabel.Text = vm.StatusText;
        };
        statusLabel.Margin = new Thickness(6, 10, 0, 0);
        Grid.SetRow(statusLabel, 2);
        mainArea.Children.Add(statusLabel);
    }

    private void SetupViews()
    {
        pages["总览"] = new OverviewView(vm);
        pages["散热"] = new CoolingView(vm);
        pages["设备"] = new DeviceView(vm);
        pages["场景"] = new ProfilesView(vm);
        var settingsView = new SettingsView(vm);
        settingsView.RequestExit += () =>
        {
            allowExit = true;
            Close();
        };
        pages["设置"] = settingsView;
    }

    private void Navigate(string key)
    {
        if (!pages.TryGetValue(key, out var view))
            return;
        contentHost.Content = view;
        pageTitle.Text = key switch
        {
            "总览" => "设备总览",
            "散热" => "散热控制",
            "设备" => "设备设置",
            "场景" => "我的配置",
            "设置" => "常规与审计",
            _ => key
        };
        foreach (var (name, btn) in navButtons)
        {
            btn.Background = name == key ? new LinearGradientBrush(Color.FromRgb(210, 236, 252), Color.FromRgb(236, 248, 255), 0) : Brush("#00FFFFFF");
            btn.BorderBrush = name == key ? Brush("#FFFFFF") : Brush("#00FFFFFF");
        }
    }

    private void SetupTray()
    {
        try
        {
            var icoPath = Path.Combine(AppContext.BaseDirectory, "Control.ico");
            var icon = File.Exists(icoPath) ? new System.Drawing.Icon(icoPath) : System.Drawing.SystemIcons.Application;
            tray = new Forms.NotifyIcon
            {
                Icon = icon,
                Text = "PC Control Center",
                Visible = true
            };
            tray.DoubleClick += (s, e) => Dispatcher.Invoke(() =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            });

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("打开主界面", null, (s, e) => Dispatcher.Invoke(() =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            }));
            menu.Items.Add("刷新遥测状态", null, (s, e) => Dispatcher.Invoke(async () => await vm.RefreshTelemetryAsync()));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出控制中心", null, (s, e) => Dispatcher.Invoke(() =>
            {
                allowExit = true;
                Close();
            }));
            tray.ContextMenuStrip = menu;
        }
        catch { }
    }
}
