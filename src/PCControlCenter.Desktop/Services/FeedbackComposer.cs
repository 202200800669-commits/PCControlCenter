using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PCControlCenter.Core;

namespace PCControlCenter.Desktop.Services;

public static class FeedbackComposer
{
    public const string DefaultRepository = "https://github.com/202200800669-commits/PCControlCenter";
    public static string Build(Snapshot snapshot, string category, string description, string log, bool includeEvents)
    {
        var report = JsonNode.Parse(Diagnostics.ToJson(snapshot))!.AsObject();
        var events = new JsonArray();
        if (includeEvents)
        {
            foreach (var line in log.Split('\n').TakeLast(80))
            {
                string? feature = new[] { "风扇", "模式", "亮度", "能源", "场景", "恢复" }.FirstOrDefault(line.Contains);
                if (feature is null)
                    continue;
                var time = Regex.Match(line, @"^\d{2}:\d{2}:\d{2}").Value;
                var result = line.Contains("失败") || line.Contains("异常") ? "failure" : line.Contains("未确认") ? "unconfirmed" : line.Contains("成功") || line.Contains("已确认") ? "confirmed" : "event";
                // Store structured event labels only. Never export raw exception text, paths or command lines.
                events.Add(new JsonObject { ["time"] = time, ["feature"] = feature, ["result"] = result });
            }
        }
        report["userFeedback"] = new JsonObject { ["category"] = category, ["description"] = description[..Math.Min(description.Length, 3000)], ["events"] = events };
        return report.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
    public static void Export(string previewedJson, string path)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry("diagnostics.json").Open());
        writer.Write(previewedJson);
    }
    public static Uri IssuePage(string repository, string title, string? previewedJson = null)
    {
        if (!Uri.TryCreate(repository.Trim().TrimEnd('/'), UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !uri.IsDefaultPort || !Regex.IsMatch(uri.AbsolutePath, @"^/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"))
            throw new ArgumentException("请输入 GitHub 仓库地址，例如 https://github.com/作者/仓库。");
        string body = "请在此补充复现步骤，并拖入已预览的诊断 ZIP 附件。";
        if (previewedJson is not null)
        {
            var report = JsonNode.Parse(previewedJson)!;
            string Field(string group, string key) => PublicText.Clean(report[group]?[key]?.GetValue<string>());
            string description = report["userFeedback"]?["description"]?.GetValue<string>() ?? "";
            body = $"### 设备\n{Field("device", "manufacturer")} / {Field("device", "model")}\nBIOS: {Field("device", "bios")}\n\n### 反馈类型\n{Field("userFeedback", "category")}\n\n### 问题与复现步骤\n{description[..Math.Min(description.Length, 800)]}\n\n### 诊断附件\n请拖入已预览并导出的 ZIP 文件。较长说明及操作记录以附件为准。\n\n此反馈为用户报告，尚未经维护者实机验证。";
        }
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/issues/new?title=" + Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(body));
    }
}
