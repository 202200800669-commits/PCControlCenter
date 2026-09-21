using System;
using System.Windows;
using System.Windows.Controls;
using PCControlCenter.Desktop.Services;
using PCControlCenter.Desktop.ViewModels;
using static PCControlCenter.Desktop.Views.UIFactory;
namespace PCControlCenter.Desktop.Views;

public sealed class SettingsView : StackPanel
{
    public event Action? RequestExit;
    public SettingsView(MainViewModel vm)
    {
        var permissionStatus = Text(vm.AuthorizationText, 12);
        var permissionButton = ActionButton("手动授权", async () => { if (!Appearance.Preview) await vm.AuthorizeAsync(); });
        void UpdatePermission()
        {
            permissionStatus.Text = vm.AuthorizationText;
            permissionButton.Content = vm.IsAuthorized ? "已授权" : "手动授权";
            permissionButton.IsEnabled = !vm.IsAuthorized && !vm.AuthorizationPending && !Appearance.Preview;
        }
        vm.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(vm.IsAuthorized) or nameof(vm.AuthorizationText) or nameof(vm.AuthorizationPending)) UpdatePermission(); };
        UpdatePermission();
        Children.Add(Card(Stack(Head("控制权限"), Row(permissionStatus, permissionButton))));
        var choices = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var p in Appearance.Palettes)
        {
            var sample = new Border { Width = 112, Height = 66, CornerRadius = new CornerRadius(12), Background = new System.Windows.Media.LinearGradientBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(p.Background), (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(p.Accent), 35), Margin = new Thickness(0, 0, 0, 10), Child = new Border { Margin = new Thickness(12), CornerRadius = new CornerRadius(8), Background = Brush(p.Surface), BorderBrush = Brush(p.Line), BorderThickness = new Thickness(1) } };
            var label = Text(p.Name, 12, "#173C58");
            label.HorizontalAlignment = HorizontalAlignment.Center;
            var button = new Button { Content = Stack(sample, label), Padding = new Thickness(10), Margin = new Thickness(0, 0, 12, 10), Tag = p.Id, ToolTip = p.Name + "主题" };
            button.Click += (_, _) => Appearance.Apply(p.Id);
            button.BorderThickness = new Thickness(2);
            choices.Children.Add(button);
        }
        void UpdateChoices()
        {
            foreach (Button item in choices.Children)
            {
                bool selected = Equals(item.Tag, Appearance.Current.Id);
                item.SetResourceReference(Control.BorderBrushProperty, selected ? "Accent" : "SurfaceLine");
                System.Windows.Automation.AutomationProperties.SetItemStatus(item, selected ? "已选中" : "未选中");
            }
        }
        Appearance.Changed += UpdateChoices;
        UpdateChoices();
        var motion = new CheckBox { Content = "流动光影", IsChecked = Appearance.Preferences.Motion };
        motion.Checked += (_, _) => Appearance.SetMotion(true);
        motion.Unchecked += (_, _) => Appearance.SetMotion(false);
        Children.Add(Card(Stack(Head("主题"), choices, motion)));
        var tray = new CheckBox { Content = "关闭时收起到托盘", IsChecked = vm.MinimizeToTray };
        tray.Checked += (_, _) => { vm.MinimizeToTray = true; Appearance.Preferences.MinimizeToTray = true; Appearance.Save(); };
        tray.Unchecked += (_, _) => { vm.MinimizeToTray = false; Appearance.Preferences.MinimizeToTray = false; Appearance.Save(); };
        var startup = new CheckBox { Content = "开机启动", IsChecked = vm.AutoStart };
        startup.Checked += (_, _) => { if (!Appearance.Preview) vm.AutoStart = true; };
        startup.Unchecked += (_, _) => { if (!Appearance.Preview) vm.AutoStart = false; };
        var interval = new ComboBox { ItemsSource = new[] { "2 秒", "3 秒", "5 秒", "10 秒" }, SelectedIndex = Array.IndexOf(new[] { 2, 3, 5, 10 }, vm.PollIntervalSeconds), MinWidth = 120 };
        interval.SelectionChanged += (_, _) => { if (interval.SelectedIndex < 0) return; vm.PollIntervalSeconds = new[] { 2, 3, 5, 10 }[interval.SelectedIndex]; Appearance.Preferences.PollSeconds = vm.PollIntervalSeconds; Appearance.Save(); };
        Children.Add(Card(Stack(Head("运行偏好"), Two(Stack(tray, startup), Stack(Text("状态刷新", 12), interval)))));
        var logs = new TextBox { Text = vm.LogText, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Height = 210, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 12 };
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.LogText)) logs.Text = vm.LogText; };
        Children.Add(Card(new Expander { Header = "运行日志", Content = logs }));
        Children.Add(Row(ActionButton("退出程序", () => RequestExit?.Invoke())));
    }
}
