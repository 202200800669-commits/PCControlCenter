using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace PCControlCenter.Desktop.Services;

public static class PreviewRenderer
{
    public static void RenderMotion(MainWindow window, string directory)
    {
        Directory.CreateDirectory(directory);
        Appearance.Preferences.Motion = true;
        Appearance.Apply("ice", false);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.ShowInTaskbar = false;
        window.Loaded += async (_, _) =>
        {
            var root = (FrameworkElement)window.Content;
            for (int i = 0; i < 3; i++)
            {
                await Task.Delay(i == 0 ? 100 : 3000);
                Capture(root, 1200, 840, Path.Combine(directory, $"motion-{i}.png"));
            }
            Appearance.SetMotion(false);
            Capture(root, 1200, 840, Path.Combine(directory, "still-0.png"));
            await Task.Delay(1200);
            Capture(root, 1200, 840, Path.Combine(directory, "still-1.png"));
            window.Close();
        };
        Application.Current.Run(window);
    }
    // Offline layout verification; no device initialization or hardware commands.
    public static void Render(MainWindow window, string directory)
    {
        Directory.CreateDirectory(directory);
        var root = (FrameworkElement)window.Content;
        foreach (var palette in Appearance.Palettes)
        {
            Appearance.Apply(palette.Id, false);
            foreach (var child in ((System.Windows.Controls.Grid)root).Children)
                if (child is Views.AmbientBackground ambient)
                    ambient.Rebuild();
            window.SetSidebar(true, false);
            foreach (string page in new[] { "总览", "散热", "设备", "场景", "反馈", "设置" })
            {
                window.Navigate(page);
                Capture(root, 1200, 840, Path.Combine(directory, palette.Id + "-" + page + ".png"));
            }
            window.SetSidebar(false, false);
            window.Navigate("总览");
            Capture(root, 940, 650, Path.Combine(directory, palette.Id + "-compact.png"));
        }
        window.Close();
    }
    private static void Capture(FrameworkElement root, int width, int height, string path)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
