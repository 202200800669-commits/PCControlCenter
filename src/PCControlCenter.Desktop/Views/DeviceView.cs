using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PCControlCenter.Desktop.Services;
using PCControlCenter.Desktop.ViewModels;
using static PCControlCenter.Desktop.Views.UIFactory;

namespace PCControlCenter.Desktop.Views;

public sealed class DeviceView : StackPanel
{
    public DeviceView(MainViewModel vm)
    {
        bool syncing = false, brightnessBusy = false;
        var value = Text("待读取", 24);
        var slider = new Slider { Minimum = 10, Maximum = 100, Value = vm.Brightness, TickFrequency = 5 };
        var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        var charge = new SegmentedControl("正常", "养护", "快充");
        var backlight = new SegmentedControl("关闭", "低亮", "高亮", "自动");
        var night = new CheckBox { Content = "夜间慢充" };
        var chargeLabel = Text("待读取", 12);
        var keyLabel = Text("待读取", 12);
        var resultLabel = Text("", 12);
        var batteryValue = Text(vm.BatteryText, 22);
        var batteryDetail = Text(vm.BatteryDetailText, 12);
        slider.ValueChanged += (_, _) => { if (syncing) return; value.Text = $"{slider.Value:0}%"; debounce.Stop(); debounce.Start(); };
        debounce.Tick += async (_, _) =>
        {
            if (slider.IsMouseCaptureWithin || brightnessBusy)
                return;
            debounce.Stop();
            if (!IsVisible || Appearance.Preview)
                return;
            brightnessBusy = true;
            try
            {
                await vm.SetBrightnessAsync((int)slider.Value);
            }
            finally { brightnessBusy = false; Update(); }
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible) debounce.Stop(); };
        Unloaded += (_, _) => debounce.Stop();
        charge.Invoked += async index => { if (!Appearance.Preview) await vm.SetEnergyAsync("charge", index); };
        backlight.Invoked += async index => { if (!Appearance.Preview) await vm.SetEnergyAsync("key", index); };
        night.Click += async (_, _) => { if (!Appearance.Preview) await vm.SetEnergyAsync("night", night.IsChecked == true ? 1 : 0); Update(); };
        Children.Add(Card(Stack(Head("显示与键盘"), Inset(Stack(Text("屏幕亮度", 12), value, slider)), Inset(Stack(Text("键盘背光", 12), backlight, keyLabel)))));
        Children.Add(Card(Stack(Head("电池"), Inset(Stack(batteryValue, batteryDetail)), charge, chargeLabel, night)));
        Children.Add(resultLabel);
        Children.Add(Card(Stack(Head("系统设置"), Row(Link("显示", "ms-settings:display"), Link("声音", "ms-settings:sound"), Link("电源", "ms-settings:powersleep")))));
        Button Link(string label, string uri) => ActionButton(label, () => { if (Appearance.Preview) return; try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { vm.StatusText = "无法打开系统设置"; } });
        void Update()
        {
            batteryValue.Text = vm.BatteryText;
            batteryDetail.Text = vm.BatteryDetailText;
            resultLabel.Text = vm.EnergyStatus;
            resultLabel.Visibility = string.IsNullOrEmpty(vm.EnergyStatus) ? Visibility.Collapsed : Visibility.Visible;
            if (!slider.IsMouseCaptureWithin && !slider.IsKeyboardFocusWithin && !debounce.IsEnabled)
            {
                syncing = true;
                slider.Value = vm.Brightness;
                syncing = false;
                value.Text = vm.ConfirmedBrightness is int confirmed ? $"{confirmed}%" : "待读取";
            }
            charge.Select(vm.EnergyCharge);
            backlight.Select(vm.EnergyKey);
            night.IsChecked = vm.EnergyNight == 1;
            bool supported = vm.IsThinkBookSupported && vm.IsAuthorized && vm.CanSwitchHardware && !Appearance.Preview;
            charge.IsEnabled = backlight.IsEnabled = night.IsEnabled = supported;
            chargeLabel.Text = vm.EnergyCharge switch
            {
                0 => "正常模式",
                1 => "养护模式",
                2 => "快充模式",
                _ => "充电状态待读取"
            };
            keyLabel.Text = vm.EnergyKey switch
            {
                0 => "背光已关闭",
                1 => "低亮度",
                2 => "高亮度",
                3 => "自动",
                _ => "背光状态待读取"
            };
        }
        ViewRefresh.Subscribe(this, vm, Update, nameof(vm.Brightness), nameof(vm.ConfirmedBrightness), nameof(vm.EnergyCharge),
            nameof(vm.EnergyKey), nameof(vm.EnergyNight), nameof(vm.EnergyStatus), nameof(vm.BatteryText), nameof(vm.BatteryDetailText),
            nameof(vm.IsBusy), nameof(vm.IsAuthorized), nameof(vm.HardwareTransition), nameof(vm.ManualFanActive), nameof(vm.DeviceTitle));
        Update();
    }
}
