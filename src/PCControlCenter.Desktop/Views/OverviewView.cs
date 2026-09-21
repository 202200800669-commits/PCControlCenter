using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PCControlCenter.Desktop.ViewModels;
using static PCControlCenter.Desktop.Views.UIFactory;

namespace PCControlCenter.Desktop.Views;

public sealed class OverviewView : StackPanel
{
    private readonly MainViewModel vm;
    private readonly Canvas graph = new() { Height = 110, ClipToBounds = true };
    private readonly TextBlock deviceLabel = Text("正在识别设备", 24);
    private readonly TextBlock processorLabel = Text("—", 12);
    private readonly TextBlock heroModeLabel = Text("—", 20, "#1C4965");
    private readonly TextBlock cpuLoadLabel = Text("— %", 28, "#173C58");
    private readonly TextBlock gpuTempLabel = Text("—", 28, "#173C58");
    private readonly TextBlock gpuDetailLabel = Text("—", 11, "#7894AA");
    private readonly TextBlock memoryLabel = Text("— / — GB", 28, "#173C58");
    private readonly TextBlock batteryLabel = Text("—", 11, "#7894AA");
    private readonly TextBlock fansLabel = Text("需提权读取", 24, "#173C58");
    private readonly TextBlock fanStateLabel = Text("需提权", 11, "#7894AA");
    private readonly ProgressBar cpuBar = Bar(Brush("#278FCD"));
    private readonly ProgressBar gpuBar = Bar(Brush("#9D8AD8"));
    private readonly ProgressBar memoryBar = Bar(Brush("#7AACD5"));

    public OverviewView(MainViewModel vm)
    {
        this.vm = vm;
        Build();
        vm.PropertyChanged += (s, e) => Update();
        vm.GraphUpdated += DrawGraph;
        graph.SizeChanged += (s, e) => DrawGraph();
        Update();
    }

    private void Build()
    {
        // Hero Card
        var heroLayout = new Grid();
        heroLayout.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        heroLayout.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });

        var titleStack = Stack(
            deviceLabel,
            processorLabel
        );
        titleStack.Margin = new Thickness(6, 4, 0, 0);
        heroLayout.Children.Add(titleStack);

        var modeStatus = Stack(
            Text("当前性能模式", 11, "#668CA4"),
            heroModeLabel
        );
        modeStatus.Margin = new Thickness(20, 6, 8, 0);
        Grid.SetColumn(modeStatus, 1);
        heroLayout.Children.Add(modeStatus);

        var heroCard = Card(heroLayout);
        Children.Add(heroCard);

        // Metrics Grid 1: CPU & GPU
        var cpuCard = Card(Stack(
            Text("CPU", 12, "#6C8BA2"),
            cpuLoadLabel,
            cpuBar,
            Text("实时处理器负载", 11, "#7894AA")
        ));

        var gpuCard = Card(Stack(
            Text("GPU", 12, "#6C8BA2"),
            gpuTempLabel,
            gpuBar,
            gpuDetailLabel
        ));
        Children.Add(Two(cpuCard, gpuCard));

        // Metrics Grid 2: Fans & Memory
        var readFansBtn = new Button { Content = "读取转速", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 8, 0, 0) };
        readFansBtn.IsEnabled = vm.IsThinkBookSupported && vm.IsAuthorized && !vm.IsBusy;
        vm.PropertyChanged += (_, _) => readFansBtn.IsEnabled = vm.IsThinkBookSupported && vm.IsAuthorized && !vm.IsBusy;
        readFansBtn.Click += async (s, e) => await vm.ReadFansElevatedAsync();

        var fansCard = Card(Stack(
            Text("散热风扇", 12, "#6C8BA2"),
            fansLabel,
            fanStateLabel,
            readFansBtn
        ));

        var memoryCard = Card(Stack(
            Text("系统内存与电源", 12, "#6C8BA2"),
            memoryLabel,
            memoryBar,
            batteryLabel
        ));
        Children.Add(Two(fansCard, memoryCard));

        // Trend Graph Card
        Children.Add(Card(Stack(
            Head("负载趋势"),
            Text("CPU · 主色    GPU · 辅色", 11, "#7894AA"),
            graph
        )));
    }

    public void Update()
    {
        deviceLabel.Text = vm.DeviceTitle;
        processorLabel.Text = vm.CpuName;
        heroModeLabel.Text = vm.PerformanceModeName;
        cpuLoadLabel.Text = vm.CpuLoadText;
        cpuBar.Value = vm.CpuLoad;
        gpuTempLabel.Text = vm.GpuTempText;
        gpuBar.Value = vm.GpuLoad;
        gpuDetailLabel.Text = vm.GpuDetailText;
        memoryLabel.Text = vm.MemoryText;
        memoryBar.Value = vm.MemoryLoad;
        batteryLabel.Text = vm.BatteryText;
        fansLabel.Text = vm.FanText;
        fanStateLabel.Text = vm.FanStateText;
    }

    private void DrawGraph()
    {
        double w = graph.ActualWidth;
        double h = graph.Height;
        if (w < 10 || h < 10)
            return;
        graph.Children.Clear();

        // Grid lines
        for (int i = 1; i < 4; i++)
        {
            graph.Children.Add(new Line
            {
                X1 = 0,
                X2 = w,
                Y1 = h * i / 4,
                Y2 = h * i / 4,
                Stroke = (System.Windows.Media.Brush)FindResource("SurfaceLine"),
                StrokeThickness = 1
            });
        }

        // Draw CPU line
        DrawSeries(vm.CpuHistory.ToArray(), vm.AccentColor, w, h);
        // Draw GPU line
        DrawSeries(vm.GpuHistory.ToArray(), Services.Appearance.Current.Secondary, w, h);
    }

    private void DrawSeries(double[] data, string colorHex, double w, double h)
    {
        if (data.Length < 2)
            return;
        Polyline Line() => new()
        {
            Stroke = Brush(colorHex),
            StrokeThickness = 2
        };
        var line = Line();
        for (int i = 0; i < data.Length; i++)
        {
            if (!double.IsFinite(data[i]))
            {
                if (line.Points.Count > 1)
                    graph.Children.Add(line);
                line = Line();
                continue;
            }
            double val = Math.Clamp(data[i], 0, 100);
            double x = w * i / (data.Length - 1);
            double y = h * (1.0 - val / 100.0);
            line.Points.Add(new Point(x, y));
        }
        if (line.Points.Count > 1)
            graph.Children.Add(line);
    }
}
