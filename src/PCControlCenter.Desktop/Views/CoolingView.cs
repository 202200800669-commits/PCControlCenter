using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PCControlCenter.Desktop.Services;
using PCControlCenter.Desktop.ViewModels;
using static PCControlCenter.Desktop.Views.UIFactory;

namespace PCControlCenter.Desktop.Views;

public sealed class CoolingView : StackPanel
{
    private readonly MainViewModel vm;
    private readonly TextBlock fans = Text("—", 28);
    private readonly TextBlock state = Text("待读取", 12);
    private readonly SegmentedControl modes = new("智能", "节能", "性能");
    private readonly Slider first = new() { Minimum = 1500, Maximum = 5500, Value = 4000, TickFrequency = 100 };
    private readonly Slider second = new() { Minimum = 1500, Maximum = 5500, Value = 4000, TickFrequency = 100 };
    private readonly CheckBox linked = new() { Content = "同步双风扇", IsChecked = true };
    private readonly ComboBox duration = new() { ItemsSource = new[] { "10 秒", "15 秒", "20 秒", "30 秒" }, SelectedIndex = 1, Width = 112 };
    private readonly DispatcherTimer commit = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool syncing;
    public CoolingView(MainViewModel vm)
    {
        this.vm = vm;
        var modeLabel = Text(vm.PerformanceModeName, 12);
        modes.Invoked += async index => { if (!Appearance.Preview) await vm.SetPerformanceModeAsync(new[] { 0, 1, 3 }[index]); };
        Children.Add(Card(Stack(Head("性能模式"), modes, modeLabel)));
        var refresh = ActionButton("读取转速", async () => { if (!Appearance.Preview) await vm.ReadFansElevatedAsync(); });
        var restore = ActionButton("恢复自动", async () => { commit.Stop(); if (!Appearance.Preview) await vm.RestoreFanAutoAsync(); });
        Children.Add(Card(Stack(Head("散热状态"), Two(Stack(fans, state), Row(refresh, restore)))));
        var label1 = Text("4000 RPM", 22);
        var label2 = Text("4000 RPM", 22);
        void Changed(Slider source, Slider other)
        {
            label1.Text = $"{first.Value:0} RPM";
            label2.Text = $"{second.Value:0} RPM";
            if (syncing)
                return;
            if (linked.IsChecked == true)
            {
                syncing = true;
                other.Value = source.Value;
                syncing = false;
            }
            if (source.IsKeyboardFocusWithin || source.IsMouseCaptureWithin)
            {
                commit.Stop();
                commit.Start();
            }
        }
        first.ValueChanged += (_, _) => Changed(first, second);
        second.ValueChanged += (_, _) => Changed(second, first);
        first.PreviewMouseLeftButtonUp += (_, _) => { commit.Stop(); commit.Start(); };
        second.PreviewMouseLeftButtonUp += (_, _) => { commit.Stop(); commit.Start(); };
        commit.Tick += async (_, _) =>
        {
            if (first.IsMouseCaptureWithin || second.IsMouseCaptureWithin)
                return;
            commit.Stop();
            if (!IsVisible || Appearance.Preview || vm.IsBusy || !vm.IsThinkBookSupported)
                return;
            await vm.RunFanTrialAsync((int)first.Value, (int)second.Value, new[] { 10, 15, 20, 30 }[duration.SelectedIndex]);
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible) commit.Stop(); };
        Unloaded += (_, _) => commit.Stop();
        Children.Add(Card(Stack(Head("手动转速"), Inset(Two(Stack(Text("风扇 1", 12), label1, first), Stack(Text("风扇 2", 12), label2, second))), Two(Stack(linked), Stack(Text("试运行时长", 12), duration)), Text("松开滑块应用 · 到期恢复自动", 12))));
        void Update()
        {
            fans.Text = vm.FanText;
            state.Text = vm.FanStateText;
            modeLabel.Text = vm.PerformanceModeName;
            modes.Select(vm.PerformanceMode == 0 ? 0 : vm.PerformanceMode == 1 ? 1 : vm.PerformanceMode == 3 ? 2 : -1);
            bool enabled = vm.IsThinkBookSupported && !vm.IsBusy && !Appearance.Preview;
            modes.IsEnabled = first.IsEnabled = second.IsEnabled = linked.IsEnabled = duration.IsEnabled = refresh.IsEnabled = enabled;
            restore.IsEnabled = vm.IsThinkBookSupported && !Appearance.Preview;
        }
        vm.PropertyChanged += (_, _) => Update();
        Update();
    }
}
