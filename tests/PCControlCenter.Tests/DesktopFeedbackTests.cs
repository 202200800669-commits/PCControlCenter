using PCControlCenter.Core;
using PCControlCenter.Desktop.Services;
using System.Text.Json.Nodes;
using System.IO.Compression;

static class DesktopFeedbackTests
{
    public static void Run(Action<bool, string> check, Snapshot snapshot)
    {
        string log = "12:34:56 风扇失败 C:\\Users\\PrivateUser\\secret.txt token=secret-value\n12:35:00 亮度已确认: 60%";
        string json = FeedbackComposer.Build(snapshot, "型号适配", "拖动后没有变化", log, true);
        var report = JsonNode.Parse(json)!;
        check(!json.Contains("PrivateUser") && !json.Contains("secret-value") && report["userFeedback"]!["events"]!.AsArray().Count == 2, "feedback exports structured events without raw paths or secrets");
        check(JsonNode.Parse(FeedbackComposer.Build(snapshot, "型号适配", "", log, false))!["userFeedback"]!["events"]!.AsArray().Count == 0, "feedback respects event opt-out");
        check(JsonNode.Parse(FeedbackComposer.Build(snapshot, "型号适配", new string('a', 4000), "", false))!["userFeedback"]!["description"]!.GetValue<string>().Length == 3000, "feedback description is bounded");
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        try
        {
            FeedbackComposer.Export(json, path);
            using (var zip = ZipFile.OpenRead(path))
            using (var reader = new StreamReader(zip.Entries.Single().Open()))
                check(reader.ReadToEnd() == json, "feedback archive contains exactly the previewed JSON");
            check(Feedback.Inspect(path).ReportedDevice.Model == snapshot.Device.Model, "desktop feedback is compatible with feedback importer");
            byte[] original = File.ReadAllBytes(path);
            bool rejected = false;
            try
            {
                FeedbackComposer.Export("changed", path);
            }
            catch (IOException) { rejected = true; }
            check(rejected && File.ReadAllBytes(path).SequenceEqual(original), "feedback export never overwrites existing archive");
        }
        finally { File.Delete(path); }
        var uri = FeedbackComposer.IssuePage("https://github.com/example/project", "[测试] A&B");
        check(uri.Host == "github.com" && uri.AbsolutePath == "/example/project/issues/new" && !uri.Query.Contains("A&B"), "feedback issue URL encodes title and opens configured repository");
        var preparedUri = FeedbackComposer.IssuePage(FeedbackComposer.DefaultRepository, "[适配反馈]", json);
        string query = Uri.UnescapeDataString(preparedUri.Query);
        check(query.Contains("拖动后没有变化") && query.Contains(snapshot.Device.Model) && !query.Contains("secret-value"), "submission page includes the previewed description and device, not raw logs");
        check(new AppearancePreferences().FeedbackRepository == FeedbackComposer.DefaultRepository, "fresh installations have a working project feedback destination");
        foreach (string invalid in new[] { "http://github.com/a/b", "https://github.com.evil.test/a/b", "file:///secret", "https://github.com/a/b?redirect=x", "https://user:pass@github.com/a/b", "https://github.com:444/a/b", "https://github.com/a/b/issues" })
        {
            bool rejected = false;
            try
            {
                FeedbackComposer.IssuePage(invalid, "test");
            }
            catch (ArgumentException) { rejected = true; }
            check(rejected, "feedback rejects unsafe or non-repository channel");
        }
    }
}
