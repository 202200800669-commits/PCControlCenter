using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PCControlCenter.Core;

public static class Diagnostics
{
    public static JsonSerializerOptions Json
    {
        get;
    } = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    // Only explicitly selected fields enter the report. Never serialize process output,
    // exception messages, registry dumps, serial numbers or user paths.
    public static object Report(Snapshot s) => new
    {
        schemaVersion = 2,
        applicationVersion = "0.1.0-alpha.18",
        capturedAt = DateTimeOffset.UtcNow,
        provider = s.Provider,
        device = new
        {
            manufacturer = s.Device.Manufacturer,
            product = s.Device.Product,
            model = s.Device.Model,
            bios = s.Device.Bios,
            platform = s.Device.Platform
        },
        system = s.System,
        capabilities = s.Capabilities.Select(c => new { c.Feature, c.Level, c.CanWrite, c.Unit, c.Min, c.Max }),
        readings = s.Readings.Select(r => new { r.Feature, r.Channel, r.Value, r.Unit, r.At, r.Status }),
        issueCodes = s.IssueCodes.Where(c => Regex.IsMatch(c, "^[A-Z][A-Z0-9_]{0,63}$")),
        privacy = "No serial number, user name, machine name, full paths or network identifiers are intentionally collected. Review model/firmware strings before sharing."
    };
    public static string ToJson(Snapshot snapshot) => JsonSerializer.Serialize(Report(snapshot), Json);
    public static string FormatGitHubIssueMarkdown(Snapshot s)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("### 硬件环境与固件信息 (Hardware Environment)");
        sb.AppendLine($"* **制造商 (Manufacturer)**: `{PublicText.Clean(s.Device.Manufacturer)}`");
        sb.AppendLine($"* **产品代号 (Product)**: `{PublicText.Clean(s.Device.Product)}`");
        sb.AppendLine($"* **营销型号 (Model)**: `{PublicText.Clean(s.Device.Model)}`");
        sb.AppendLine($"* **BIOS 固件版本**: `{PublicText.Clean(s.Device.Bios)}`");
        if (s.System is not null)
        {
            sb.AppendLine($"* **操作系统**: `{s.System.OsVersion}` (Build `{s.System.OsBuild}`)");
            sb.AppendLine($"* **处理器**: `{s.System.CpuName}`");
            sb.AppendLine($"* **主板型号**: `{s.System.BoardMaker}` `{s.System.BoardProduct}`");
            if (!string.IsNullOrEmpty(s.System.PowerSource))
                sb.AppendLine($"* **供电状态**: `{s.System.PowerSource}`");
            if (!string.IsNullOrEmpty(s.System.PowerPlan))
                sb.AppendLine($"* **Windows 电源计划**: `{s.System.PowerPlan}`");
        }
        sb.AppendLine();
        sb.AppendLine("### 底层接口探查 (Interface Survey)");
        if (s.System?.DiscoveredInterfaces is { Count: > 0 } ifaces)
            sb.AppendLine($"* **检测到的底层接口**: {string.Join(", ", ifaces.Select(i => $"`{i}`"))}");
        else
            sb.AppendLine("* **检测到的底层接口**: `None`");
        sb.AppendLine($"* **匹配适配器 ID**: `{s.Provider}`");
        sb.AppendLine();
        sb.AppendLine("### 功能能力矩阵 (Capabilities Matrix)");
        sb.AppendLine("| 功能 (Feature) | 支持级别 (Level) | 写入 (CanWrite) | 单位/范围 | 约束与原因 |");
        sb.AppendLine("| :--- | :---: | :---: | :---: | :--- |");
        foreach (var c in s.Capabilities)
        {
            var range = c.Min is not null || c.Max is not null ? $"{c.Min ?? 0}~{c.Max ?? 0} {c.Unit}" : c.Unit;
            sb.AppendLine($"| `{c.Feature}` | `{c.Level}` | `{(c.CanWrite ? "YES" : "NO")}` | {range} | {c.Reason ?? "-"} |");
        }
        sb.AppendLine();
        sb.AppendLine("### 传感器遥测读数 (Current Readings)");
        sb.AppendLine("| 通道 (Channel) | 对应功能 | 读数 (Value) | 状态 (Status) |");
        sb.AppendLine("| :--- | :--- | :--- | :--- |");
        foreach (var r in s.Readings)
        {
            var valStr = r.Value is not null ? $"{r.Value} {r.Unit}" : "Unavailable";
            sb.AppendLine($"| `{r.Channel}` | `{r.Feature}` | {valStr} | `{r.Status}` |");
        }
        sb.AppendLine();
        sb.AppendLine("### 诊断与排障代码 (Issue Codes)");
        var validCodes = s.IssueCodes.Where(c => Regex.IsMatch(c, "^[A-Z][A-Z0-9_]{0,63}$")).ToArray();
        sb.AppendLine(validCodes.Length > 0 ? string.Join(", ", validCodes.Select(c => $"`{c}`")) : "`NONE`");
        sb.AppendLine();
        sb.AppendLine("### 实机使用体验与反馈 (User Observations)");
        sb.AppendLine("<!-- 请在下方补充您的实际体验：例如风扇是否可调、快捷键是否有效、是否有报错等 -->");
        sb.AppendLine("- [ ] 散热监控正常");
        sb.AppendLine("- [ ] 性能模式调节有效");
        sb.AppendLine("- [ ] 电池充电阈值有效");
        sb.AppendLine("- **补充说明 / 问题复现**: ");
        return sb.ToString();
    }
    public static void Export(Snapshot snapshot, string destination)
    {
        var json = ToJson(snapshot);
        using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry("diagnostics.json").Open());
        writer.Write(json);
    }
}
