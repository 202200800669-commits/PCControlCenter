using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace PCControlCenter.Desktop;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "Local\\PCControlCenter.Desktop.Instance", out bool isFirstInstance);
        if (!isFirstInstance && !args.Contains("--multi-instance"))
        {
            MessageBox.Show("PC Control Center 已在运行中，请从任务栏或系统托盘打开。", "PC Control Center", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var app = new Application();
        app.DispatcherUnhandledException += (s, e) =>
        {
            try
            {
                var logFile = Path.Combine(AppContext.BaseDirectory, "desktop-crash.log");
                File.AppendAllText(logFile, $"[{DateTime.Now:O}] {e.Exception}{Environment.NewLine}");
            }
            catch { }
            MessageBox.Show($"程序发生未处理异常: {e.Exception.Message}\n详细信息已记录至日志。", "PC Control Center", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        };

        app.Resources["Accent"] = new SolidColorBrush(Color.FromRgb(39, 143, 205));

        try
        {
            var themePath = Path.Combine(AppContext.BaseDirectory, "Theme.xaml");
            if (File.Exists(themePath))
            {
                using var stream = File.OpenRead(themePath);
                var dict = (ResourceDictionary)XamlReader.Load(stream);
                app.Resources.MergedDictionaries.Add(dict);
            }
        }
        catch { }

        var window = new MainWindow();
        app.Run(window);
    }
}
