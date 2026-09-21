using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;

namespace PCControlCenter.Desktop.Views;

public sealed class SegmentedControl : Border
{
    private readonly List<Button> buttons = new();
    private int selectedIndex = int.MinValue;
    public event Action<int>? Invoked;
    public SegmentedControl(params string[] labels)
    {
        CornerRadius = new CornerRadius(13);
        Padding = new Thickness(4);
        Margin = new Thickness(0, 8, 0, 12);
        SetResourceReference(BackgroundProperty, "InsetSurface");
        var row = new UniformGrid { Rows = 1 };
        Child = row;
        for (int index = 0; index < labels.Length; index++)
        {
            int target = index;
            var button = new Button { Content = labels[index], Margin = new Thickness(0), Padding = new Thickness(8, 7, 8, 7), MinHeight = 34, BorderThickness = new Thickness(0) };
            AutomationProperties.SetName(button, labels[index]);
            button.Click += (_, _) => Invoked?.Invoke(target);
            buttons.Add(button);
            row.Children.Add(button);
        }
        Select(-1);
    }
    public void Select(int index)
    {
        if (selectedIndex == index)
            return;
        selectedIndex = index;
        for (int i = 0; i < buttons.Count; i++)
        {
            bool selected = i == index;
            buttons[i].SetResourceReference(Control.BackgroundProperty, selected ? "Accent" : "InsetSurface");
            buttons[i].SetResourceReference(Control.ForegroundProperty, selected ? "AccentText" : "TextMuted");
            buttons[i].FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            AutomationProperties.SetItemStatus(buttons[i], selected ? "已选中" : "未选中");
        }
    }
}
