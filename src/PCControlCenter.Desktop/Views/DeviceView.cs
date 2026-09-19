using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PCControlCenter.Desktop.ViewModels;
using static PCControlCenter.Desktop.Views.UIFactory;

namespace PCControlCenter.Desktop.Views;

public sealed class DeviceView : StackPanel
{
    private readonly MainViewModel vm;
    private readonly TextBlock brightValueLabel = Text("80%", 18, "#173C58");
    private readonly Slider brightSlider = new() { Minimum = 10, Maximum = 100, Value = 80, TickFrequency = 5 };
    private readonly TextBlock chargeStateLabel = Text("—", 14, "#173C58");
    private readonly TextBlock nightStateLabel = Text("—", 12, "#7894AA");
    private readonly TextBlock keyStateLabel = Text("—", 14, "#173C58");

    public DeviceView(MainViewModel vm)
    {
        this.vm = vm;
        Build();
        vm.PropertyChanged += (s, e) => Update();
        Update();
    }

    private void Build()
    {
        // Brightness Card
        brightSlider.ValueChanged += (s, e) =>
        {
            int b = (int)brightSlider.Value;
            brightValueLabel.Text = $"{b}%";
        };
        Children.Add(Card(Stack(
            Head("屏幕显示"),
            Stack(Text("主显示屏亮度", 12), brightValueLabel, brightSlider)
        )));

        // Charging & Energy Card
        var chargeButtons = Row(
            CreateEnergyButton("正常充电", "charge", 0),
            CreateEnergyButton("电池养护 (80%)", "charge", 1),
            CreateEnergyButton("快速充电", "charge", 2)
        );
        var nightButtons = Row(
            CreateEnergyButton("开启夜间慢充", "night", 1),
            CreateEnergyButton("关闭夜间慢充", "night", 0)
        );

        var chargeCard = Card(Stack(
            Head("电池管理"),
            chargeStateLabel,
            chargeButtons,
            nightStateLabel,
            nightButtons
        ));

        // Keyboard Backlight Card
        var keyButtons = Row(
            CreateEnergyButton("关闭", "key", 0),
            CreateEnergyButton("微弱低亮", "key", 1),
            CreateEnergyButton("高亮模式", "key", 2),
            CreateEnergyButton("自动感光", "key", 3)
        );

        var keyCard = Card(Stack(
            Head("键盘背光"),
            keyStateLabel,
            keyButtons
        ));

        Children.Add(Two(chargeCard, keyCard));

        // System Settings Shortcuts
        var quickLinks = Row(
            CreateLinkButton("系统显示设置", "ms-settings:display"),
            CreateLinkButton("系统声音设置", "ms-settings:sound"),
            CreateLinkButton("电源与睡眠", "ms-settings:powersleep")
        );
        Children.Add(Card(Stack(Head("系统快捷入口"), quickLinks)));
    }

    private Button CreateEnergyButton(string text, string kind, int value)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 8, 14, 8) };
        b.Click += async (s, e) => await vm.SetEnergyAsync(kind, value);
        return b;
    }

    private Button CreateLinkButton(string text, string uri)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 8, 14, 8) };
        b.Click += (s, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            }
            catch { }
        };
        return b;
    }

    public void Update()
    {
        chargeStateLabel.Text = vm.EnergyCharge switch
        {
            0 => "当前模式: 正常充电 (已充满或常规充电中)",
            1 => "当前模式: 电池养护已开启 (充电上限限制为 80%)",
            2 => "当前模式: 快速充电已开启",
            _ => "当前状态: 充电驱动状态待核对"
        };
        nightStateLabel.Text = vm.EnergyNight switch
        {
            0 => "夜间慢充保护: 关闭",
            1 => "夜间慢充保护: 开启 (智能学习就寝时间降低发热)",
            _ => "夜间慢充保护: 待读取"
        };
        keyStateLabel.Text = vm.EnergyKey switch
        {
            0 => "当前背光: 已关闭",
            1 => "当前背光: 低亮度",
            2 => "当前背光: 高亮度",
            3 => "当前背光: 自动感光",
            _ => "当前背光: 待核对"
        };
    }
}
