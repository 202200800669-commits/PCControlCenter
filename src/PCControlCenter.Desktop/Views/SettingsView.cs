using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PCControlCenter.Desktop.ViewModels;
using static PCControlCenter.Desktop.Views.UIFactory;

namespace PCControlCenter.Desktop.Views;

public sealed class SettingsView : StackPanel
{
    private readonly MainViewModel vm;
    private readonly TextBox logBox = new()
    {
        IsReadOnly = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Background = Brushes.Transparent,
        Foreground = Brush("#66809B"),
        BorderThickness = new Thickness(0),
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
        MinHeight = 160,
        MaxHeight = 260
    };
    private readonly CheckBox trayCheck = new() { Content = "关闭主窗口时收起到系统托盘", IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
    private readonly CheckBox autoStartCheck = new() { Content = "开机自动启动 (最小化到托盘)", Margin = new Thickness(0, 0, 0, 8) };
    private readonly ComboBox intervalCombo = new() { ItemsSource = new[] { "2 秒", "3 秒", "5 秒", "10 秒" }, SelectedIndex = 1, MinHeight = 36 };

    public event Action? RequestExit;

    public SettingsView(MainViewModel vm)
    {
        this.vm = vm;
        Build();
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(vm.LogText))
            {
                logBox.Text = vm.LogText;
                logBox.ScrollToEnd();
            }
            else if (e.PropertyName == nameof(vm.AutoStart))
            {
                autoStartCheck.IsChecked = vm.AutoStart;
            }
        };
    }

    private void Build()
    {
        // Appearance & Preferences
        var colorRow = Row();
        foreach (var (name, color) in new[] { ("冰蓝", "#278FCD"), ("浅紫", "#8A74CF"), ("薄荷", "#35A6AE") })
        {
            var btn = new Button { Content = name, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(16, 8, 16, 8) };
            btn.Foreground = Brush(color);
            btn.Click += (s, e) =>
            {
                vm.AccentColor = color;
                if (Application.Current != null)
                    Application.Current.Resources["Accent"] = Brush(color);
                vm.AddLog($"主题色已切换为: {name} ({color})");
            };
            colorRow.Children.Add(btn);
        }

        trayCheck.IsChecked = vm.MinimizeToTray;
        trayCheck.Checked += (s, e) => vm.MinimizeToTray = true;
        trayCheck.Unchecked += (s, e) => vm.MinimizeToTray = false;

        autoStartCheck.IsChecked = vm.AutoStart;
        autoStartCheck.Checked += (s, e) => vm.AutoStart = true;
        autoStartCheck.Unchecked += (s, e) => vm.AutoStart = false;

        intervalCombo.SelectionChanged += (s, e) =>
        {
            vm.PollIntervalSeconds = new[] { 2, 3, 5, 10 }[intervalCombo.SelectedIndex];
            vm.AddLog($"采样刷新周期已设定为: {vm.PollIntervalSeconds} 秒");
        };

        var prefCard = Card(Stack(
            Head("个性化与常规偏好"),
            Text("主题重音色彩:", 12),
            colorRow,
            trayCheck,
            autoStartCheck,
            Text("后台遥测刷新间隔:", 12, "#315772"),
            intervalCombo
        ));

        // Diagnostics Log Card
        var logCard = Card(Stack(
            Head("运行与受控审计日志"),
            logBox
        ));

        // Actions Card
        var actionsRow = Row(
            CreateActionButton("程序目录", () => Process.Start(new ProcessStartInfo(AppContext.BaseDirectory) { UseShellExecute = true })),
            CreateActionButton("导出脱敏反馈", () =>
            {
                var zipPath = Path.Combine(AppContext.BaseDirectory, "feedback.zip");
                vm.AddLog($"诊断反馈包指令: 可使用 CLI 'pc-control export {zipPath}' 生成");
                MessageBox.Show($"已准备诊断反馈链路，建议使用 CLI 执行脱敏打包。\n目标路径: {zipPath}", "诊断支持");
            }),
            CreateActionButton("退出程序", () => RequestExit?.Invoke())
        );

        var actionCard = Card(Stack(Head("程序维护"), actionsRow));

        Children.Add(prefCard);
        Children.Add(logCard);
        Children.Add(actionCard);
    }

    private Button CreateActionButton(string text, Action action)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(16, 8, 16, 8) };
        b.Click += (s, e) => action();
        return b;
    }
}
