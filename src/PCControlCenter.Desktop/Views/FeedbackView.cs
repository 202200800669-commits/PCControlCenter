using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PCControlCenter.Desktop.Services;
using PCControlCenter.Desktop.ViewModels;
using static PCControlCenter.Desktop.Views.UIFactory;

namespace PCControlCenter.Desktop.Views;

public sealed class FeedbackView : StackPanel
{
    public FeedbackView(MainViewModel vm)
    {
        string? prepared = null;
        var device = Text(vm.DeviceTitle, 17);
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.DeviceTitle)) device.Text = vm.DeviceTitle; };
        var category = new ComboBox { ItemsSource = new[] { "型号适配", "风扇与散热", "性能模式", "电池与背光", "显示与亮度", "界面问题" }, SelectedIndex = 0 };
        var notes = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 112, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = 3000 };
        var include = new CheckBox { Content = "附带本次操作记录", IsChecked = true };
        var preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 11, Text = "生成后在这里检查将导出的内容。" };
        var state = Text("仅在本地收集；提交前可预览。", 12);
        var repository = new TextBox { Text = Appearance.Preferences.FeedbackRepository };
        var export = new Button { Content = "导出反馈包", IsEnabled = false };
        var generate = new Button { Content = "收集并预览" };
        generate.SetResourceReference(Control.BackgroundProperty, "Accent");
        generate.SetResourceReference(Control.ForegroundProperty, "AccentText");
        void Invalidate()
        {
            prepared = null;
            export.IsEnabled = false;
            state.Text = "内容已更改，请重新预览。";
        }
        notes.TextChanged += (_, _) => Invalidate();
        category.SelectionChanged += (_, _) => Invalidate();
        include.Click += (_, _) => Invalidate();
        generate.Click += async (_, _) =>
        {
            generate.IsEnabled = false;
            try
            {
                if (Appearance.Preview)
                {
                    state.Text = "界面预览模式，不采集设备数据。";
                    return;
                }
                var snapshot = await vm.GetFreshSnapshotAsync();
                prepared = FeedbackComposer.Build(snapshot, category.SelectedItem?.ToString() ?? "型号适配", notes.Text, vm.LogText, include.IsChecked == true);
                preview.Text = prepared;
                export.IsEnabled = true;
                state.Text = "已生成。请检查型号、问题描述和操作记录。";
            }
            catch (Exception ex) { state.Text = "收集失败：" + ex.Message; }
            finally { generate.IsEnabled = true; }
        };
        export.Click += (_, _) =>
        {
            if (prepared is null)
                return;
            var dialog = new SaveFileDialog { Filter = "诊断反馈包 (*.zip)|*.zip", FileName = $"pc-control-feedback-{DateTime.Now:yyyyMMdd-HHmmss}.zip" };
            if (dialog.ShowDialog() != true)
                return;
            try
            {
                // Save only the exact content the user previewed.
                if (File.Exists(dialog.FileName))
                {
                    state.Text = "请使用新的文件名，避免覆盖已有反馈包。";
                    return;
                }
                FeedbackComposer.Export(prepared, dialog.FileName);
                state.Text = "反馈包已保存，可在 GitHub 提交页上传附件。";
            }
            catch (Exception ex) { state.Text = "导出失败：" + ex.Message; }
        };
        var submit = new Button { Content = "打开提交页" };
        submit.Click += (_, _) =>
        {
            try
            {
                var uri = FeedbackComposer.IssuePage(repository.Text, "[适配反馈] " + vm.DeviceTitle, prepared);
                Appearance.Preferences.FeedbackRepository = repository.Text.Trim();
                Appearance.Save();
                if (!Appearance.Preview)
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex) { state.Text = ex.Message; }
        };
        Children.Add(Card(Stack(Head("设备测试反馈"), Inset(Stack(Text("自动识别设备", 11), device)), Two(Stack(Text("反馈类型", 12), category), Stack(Text("覆盖品牌", 12), Text("联想 · 华硕 · 机械革命 · 其他", 13, "#173C58"))), Text("问题与复现步骤", 12), notes, include, Row(generate, export), state)));
        Children.Add(Card(new Expander { Header = "内容预览", IsExpanded = true, Content = preview }));
        Children.Add(Card(Stack(Head("提交渠道"), Text("GitHub 仓库", 12), repository, Text("导出 ZIP 后，在提交页附件区上传。", 12), Row(submit))));
    }
}
