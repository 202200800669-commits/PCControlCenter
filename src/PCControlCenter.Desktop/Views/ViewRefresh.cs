using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace PCControlCenter.Desktop.Views;

// Coalesce a burst of property notifications into one update on the visible page.
public static class ViewRefresh
{
    public static void Subscribe(FrameworkElement view, INotifyPropertyChanged source, Action update, params string[] properties)
    {
        bool queued = false;
        void Schedule()
        {
            if (!view.IsVisible || queued || view.Dispatcher.HasShutdownStarted)
                return;
            queued = true;
            view.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                queued = false;
                if (view.IsVisible)
                    update();
            }));
        }
        source.PropertyChanged += (_, e) =>
        {
            if (properties.Length == 0 || Array.IndexOf(properties, e.PropertyName) >= 0)
                Schedule();
        };
        view.IsVisibleChanged += (_, _) => Schedule();
        view.Loaded += (_, _) => Schedule();
    }
}
