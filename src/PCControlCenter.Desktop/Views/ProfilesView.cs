using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PCControlCenter.Desktop.ViewModels;
using static PCControlCenter.Desktop.Views.UIFactory;

namespace PCControlCenter.Desktop.Views;

public sealed class ProfilesView : StackPanel
{
    private readonly MainViewModel vm;
    private readonly ComboBox profileList = new() { MinHeight = 36, Margin = new Thickness(0, 6, 0, 16) };
    private readonly TextBox profileNameBox = new() { Text = "自定义场景", Margin = new Thickness(0, 6, 0, 16) };
    private readonly ComboBox modeCombo = new() { ItemsSource = new[] { "智能模式 (0)", "节能模式 (1)", "性能模式 (3)" }, SelectedIndex = 0, MinHeight = 36 };
    private readonly Slider brightnessSlider = new() { Minimum = 10, Maximum = 100, Value = 80, TickFrequency = 5 };

    public ProfilesView(MainViewModel vm)
    {
        this.vm = vm;
        Build();
    }

    private void Build()
    {
        profileList.ItemsSource = vm.Profiles;
        profileList.SelectedItem = vm.SelectedProfile;
        profileList.SelectionChanged += (s, e) =>
        {
            if (profileList.SelectedItem is ProfileItem p)
            {
                vm.SelectedProfile = p;
                profileNameBox.Text = p.Name;
                modeCombo.SelectedIndex = p.Mode == 1 ? 1 : (p.Mode == 3 ? 2 : 0);
                brightnessSlider.Value = p.Brightness;
            }
        };

        var scenarioCard = Card(Stack(
            Head("场景预设库"),
            Text("选择当前工作场景：", 12),
            profileList
        ));

        var applyBtn = new Button { Content = "应用所选场景", Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(0, 12, 8, 0) };
        applyBtn.Click += async (s, e) =>
        {
            if (vm.SelectedProfile is ProfileItem p)
            {
                await vm.ApplyProfileAsync(p);
            }
        };

        var saveBtn = new Button { Content = "保存当前修改", Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(0, 12, 0, 0) };
        saveBtn.Click += async (s, e) =>
        {
            string name = profileNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name))
                return;
            int mode = new[] { 0, 1, 3 }[modeCombo.SelectedIndex];
            var existing = vm.Profiles.FirstOrDefault(x => x.Name == name);
            if (existing != null)
            {
                existing.Mode = mode;
                existing.Brightness = (int)brightnessSlider.Value;
            }
            else
            {
                var newProfile = new ProfileItem { Name = name, Mode = mode, Brightness = (int)brightnessSlider.Value };
                vm.Profiles.Add(newProfile);
                profileList.SelectedItem = newProfile;
            }
            var ok = await vm.SaveProfilesAsync();
            if (ok)
            {
                vm.StatusText = $"已保存场景预设: {name}";
            }
            else
            {
                vm.StatusText = $"保存场景预设失败: {name}";
            }
        };

        var editCard = Card(Stack(
            Head("编辑与应用配置"),
            Text("配置名称:", 12),
            profileNameBox,
            Two(
                Stack(Text("关联性能模式:", 12), modeCombo),
                Stack(Text("推荐屏幕亮度:", 12), brightnessSlider)
            ),
            Row(applyBtn, saveBtn)
        ));

        Children.Add(scenarioCard);
        Children.Add(editCard);
    }
}
