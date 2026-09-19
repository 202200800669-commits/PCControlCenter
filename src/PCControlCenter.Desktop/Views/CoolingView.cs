using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PCControlCenter.Desktop.ViewModels;
using static PCControlCenter.Desktop.Views.UIFactory;

namespace PCControlCenter.Desktop.Views;

public sealed class CoolingView : StackPanel
{
    private readonly MainViewModel vm;
    private readonly TextBlock currentFansLabel = Text("需提权读取", 22, "#173C58");
    private readonly TextBlock fanStateLabel = Text("需提权", 13, "#7894AA");
    private readonly Slider fan1Slider = new() { Minimum = 1500, Maximum = 5500, Value = 4000, TickFrequency = 100 };
    private readonly Slider fan2Slider = new() { Minimum = 1500, Maximum = 5500, Value = 4000, TickFrequency = 100 };
    private readonly TextBlock fan1ValueLabel = Text("4000 RPM", 18, "#173C58");
    private readonly TextBlock fan2ValueLabel = Text("4000 RPM", 18, "#173C58");
    private readonly CheckBox linkedCheck = new() { Content = "双风扇联动", IsChecked = true };
    private readonly ComboBox durationCombo = new() { ItemsSource = new[] { "10 秒", "15 秒", "20 秒", "30 秒" }, SelectedIndex = 1 };

    public CoolingView(MainViewModel vm)
    {
        this.vm = vm;
        Build();
        vm.PropertyChanged += (s, e) => Update();
        Update();
    }

    private void Build()
    {
        // Status Card
        var topDock = new DockPanel();
        var actions = Row(
            CreateButton("提权读取", async () => await vm.ReadFansElevatedAsync()),
            CreateButton("恢复自动", async () => await vm.RestoreFanAutoAsync())
        );
        DockPanel.SetDock(actions, Dock.Right);
        topDock.Children.Add(actions);

        topDock.Children.Add(Stack(
            Head("散热状态"),
            currentFansLabel,
            fanStateLabel
        ));
        Children.Add(Card(topDock));

        // Fan Trial Card
        fan1Slider.ValueChanged += (s, e) =>
        {
            int val = (int)fan1Slider.Value;
            fan1ValueLabel.Text = $"{val} RPM";
            if (linkedCheck.IsChecked == true && (int)fan2Slider.Value != val)
                fan2Slider.Value = val;
        };
        fan2Slider.ValueChanged += (s, e) =>
        {
            int val = (int)fan2Slider.Value;
            fan2ValueLabel.Text = $"{val} RPM";
            if (linkedCheck.IsChecked == true && (int)fan1Slider.Value != val)
                fan1Slider.Value = val;
        };

        var f1Field = Stack(Text("风扇 1 目标转速", 12), fan1ValueLabel, fan1Slider);
        var f2Field = Stack(Text("风扇 2 目标转速", 12), fan2ValueLabel, fan2Slider);

        var trialButton = new Button { Content = "启动受控试运行", Padding = new Thickness(20, 10, 20, 10), Margin = new Thickness(0, 12, 0, 0) };
        trialButton.Click += async (s, e) =>
        {
            int dur = new[] { 10, 15, 20, 30 }[durationCombo.SelectedIndex];
            await vm.RunFanTrialAsync((int)fan1Slider.Value, (int)fan2Slider.Value, dur);
        };

        var trialOptions = Row(
            Text("试运行持续时长: ", 12, "#315772"),
            durationCombo,
            linkedCheck
        );

        Children.Add(Card(Stack(
            Head("风扇限时试运行 (UAC 最小权限代理)"),
            Two(f1Field, f2Field),
            trialOptions,
            trialButton,
            Text("💡 安全提示：为防范固件失控风险，风扇转速设定为带时间锁的实验性试运行。到期后由独立工作进程恢复固件自动控制，即使应用异常退出或强制结束，管道看门狗仍会触发恢复。", 11, "#7A96AA")
        )));
    }

    private Button CreateButton(string text, Func<Task> action)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 8, 14, 8) };
        b.Click += async (s, e) => await action();
        return b;
    }

    public void Update()
    {
        currentFansLabel.Text = vm.FanText;
        fanStateLabel.Text = $"当前散热状态: {vm.FanStateText}";
    }
}
