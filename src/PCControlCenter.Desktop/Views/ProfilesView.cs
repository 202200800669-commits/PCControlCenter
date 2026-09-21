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
    private readonly ComboBox profileList = new() { DisplayMemberPath = nameof(ProfileItem.Name), MinHeight = 36, Margin = new Thickness(0, 6, 0, 16) };
    private readonly TextBox profileNameBox = new() { Text = "自定义场景", Margin = new Thickness(0, 6, 0, 16) };
    private readonly ComboBox modeCombo = new() { ItemsSource = new[] { "智能", "节能", "性能" }, SelectedIndex = 0, MinHeight = 36 };
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
            Head("我的场景"),
            profileList
        ));

        var applyBtn = new Button { Content = "应用", Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(0, 12, 8, 0) };
        void UpdatePermission() => applyBtn.IsEnabled = vm.IsAuthorized && !vm.IsBusy && !Services.Appearance.Preview;
        vm.PropertyChanged += (_, _) => UpdatePermission();
        UpdatePermission();
        applyBtn.Click += async (s, e) =>
        {
            if (!Services.Appearance.Preview && vm.SelectedProfile is ProfileItem p)
            {
                await vm.ApplyProfileAsync(p);
            }
        };

        var saveBtn = new Button { Content = "保存", Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(0, 12, 0, 0) };
        saveBtn.Click += async (s, e) =>
        {
            if (Services.Appearance.Preview)
                return;
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
            Head("场景配置"),
            Text("名称", 12),
            profileNameBox,
            Two(
                Stack(Text("性能模式", 12), modeCombo),
                Stack(Text("屏幕亮度", 12), brightnessSlider)
            ),
            Row(applyBtn, saveBtn)
        ));

        Children.Add(scenarioCard);
        Children.Add(editCard);
    }
}
