using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using PCControlCenter.Desktop.Services;

namespace PCControlCenter.Desktop;

public static class Program
{
    private const string MutexName = "Local\\PCControlCenter.Desktop.Instance";
    private const string WakeEventName = "Local\\PCControlCenter.Desktop.WakeEvent";

    [STAThread]
    public static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance && !args.Contains("--multi-instance"))
        {
            try
            {
                using var existingWakeEvent = EventWaitHandle.OpenExisting(WakeEventName);
                existingWakeEvent.Set();
            }
            catch
            {
                MessageBox.Show("PC Control Center 已在运行中，请从任务栏或系统托盘打开。", "PC Control Center", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }

        EventWaitHandle? wakeEvent = null;
        if (isFirstInstance)
        {
            try
            {
                wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WakeEventName);
            }
            catch { }
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

        Appearance.Preview = args.Contains("--preview") || args.Contains("--render-preview") || args.Contains("--render-motion");
        Appearance.Load();

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

        bool startMinimized = args.Contains("--minimized");
        var window = new MainWindow(startMinimized, Appearance.Preview);
        if (args.Contains("--render-motion"))
        {
            PreviewRenderer.RenderMotion(window, args[Array.IndexOf(args, "--render-motion") + 1]);
            wakeEvent?.Dispose();
            return;
        }
        if (args.Contains("--render-preview"))
        {
            int index = Array.IndexOf(args, "--render-preview");
            PreviewRenderer.Render(window, args[index + 1]);
            wakeEvent?.Dispose();
            return;
        }
        if (startMinimized)
        {
            window.WindowState = WindowState.Minimized;
            window.ShowInTaskbar = false;
        }

        RegisteredWaitHandle? waitRegistration = null;
        try
        {
            if (wakeEvent != null)
            {
                waitRegistration = ThreadPool.RegisterWaitForSingleObject(
                    wakeEvent,
                    (state, timedOut) =>
                    {
                        if (!timedOut && state is MainWindow win)
                        {
                            win.RestoreAndActivate();
                        }
                    },
                    window,
                    -1,
                    false);
            }

            app.Run(window);
        }
        finally
        {
            waitRegistration?.Unregister(null);
            wakeEvent?.Dispose();
        }
    }
}
