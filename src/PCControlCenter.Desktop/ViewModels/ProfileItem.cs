namespace PCControlCenter.Desktop.ViewModels;

public sealed class ProfileItem
{
    public string Name { get; set; } = "我的配置";
    public int Mode { get; set; } = 0; // 0 智能, 1 节能, 3 性能
    public string FanKind { get; set; } = "auto"; // auto, full, manual
    public int Fan1 { get; set; } = 3500;
    public int Fan2 { get; set; } = 3500;
    public int Brightness { get; set; } = 80;

    public override string ToString() => Name;
}
