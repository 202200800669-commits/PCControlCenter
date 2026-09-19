using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PCControlCenter.Desktop.Services;

static class BrightnessScriptTests
{
    private static readonly string PsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        @"System32\WindowsPowerShell\v1.0\powershell.exe");

    public static async Task RunAllAsync(Action<bool, string> check)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(PsPath))
            return;

        var script = WindowsBrightnessService.GetScript();
        check(!string.IsNullOrWhiteSpace(script), "embedded brightness script is loaded");

        // 1. PowerShell 5.1 AST 语法解析测试
        var syntaxOk = await TestSyntaxInPs51Async(script);
        check(syntaxOk, "brightness script has 0 syntax errors in Windows PowerShell 5.1");

        // 2. Mock CIM 测试 - 正常成功与读回
        var successResult = await RunMockScriptAsync(
            targetPercent: 70,
            mockMethods: "@([PSCustomObject]@{ Active = $true; InstanceName = 'DISPLAY_MAIN' })",
            mockInvoke: "[PSCustomObject]@{ ReturnValue = 0 }",
            mockBrightness: "@([PSCustomObject]@{ Active = $true; InstanceName = 'DISPLAY_MAIN'; CurrentBrightness = 70 })");
        check(successResult.Status == "Success" && successResult.Confirmed == 70, "mock CIM success returns Status=Success and Confirmed=70");

        // 3. Mock CIM 测试 - 无亮度控制接口 (Unsupported)
        var unsuppResult = await RunMockScriptAsync(
            targetPercent: 50,
            mockMethods: "@()",
            mockInvoke: "$null",
            mockBrightness: "@()");
        check(unsuppResult.Status == "Unsupported" && unsuppResult.Confirmed == null, "mock CIM unsupported returns Status=Unsupported and Confirmed=null");

        // 4. Mock CIM 测试 - 方法调用失败 (ReturnValue != 0)
        var failResult = await RunMockScriptAsync(
            targetPercent: 50,
            mockMethods: "@([PSCustomObject]@{ Active = $true; InstanceName = 'DISPLAY_MAIN' })",
            mockInvoke: "[PSCustomObject]@{ ReturnValue = 2 }",
            mockBrightness: "@([PSCustomObject]@{ Active = $true; InstanceName = 'DISPLAY_MAIN'; CurrentBrightness = 50 })");
        check(failResult.Status == "Failed" && failResult.Confirmed == null, "mock CIM non-zero ReturnValue returns Status=Failed");

        // 5. Mock CIM 测试 - 写入成功但无读回 (SuccessUnconfirmed)
        var unconfResult = await RunMockScriptAsync(
            targetPercent: 60,
            mockMethods: "@([PSCustomObject]@{ Active = $true; InstanceName = 'DISPLAY_MAIN' })",
            mockInvoke: "[PSCustomObject]@{ ReturnValue = 0 }",
            mockBrightness: "@()");
        check(unconfResult.Status == "SuccessUnconfirmed" && unconfResult.Confirmed == null, "mock CIM without readback returns Status=SuccessUnconfirmed");

        // 6. Mock CIM 测试 - 多显示器匹配 (按 InstanceName 匹配)
        var multiResult = await RunMockScriptAsync(
            targetPercent: 85,
            mockMethods: "@([PSCustomObject]@{ Active = $true; InstanceName = 'TARGET_PANEL' })",
            mockInvoke: "[PSCustomObject]@{ ReturnValue = 0 }",
            mockBrightness: "@([PSCustomObject]@{ Active = $true; InstanceName = 'OTHER_PANEL'; CurrentBrightness = 15 }, [PSCustomObject]@{ Active = $true; InstanceName = 'TARGET_PANEL'; CurrentBrightness = 85 })");
        check(multiResult.Status == "Success" && multiResult.Confirmed == 85, "mock CIM multi-instance strictly matches active InstanceName");

        // 7. Mock CIM 测试 - 目标实例缺失时绝不借用其他显示器的值 (报告反例: 目标TARGET, 读回列表仅OTHER=12)
        var missingTargetResult = await RunMockScriptAsync(
            targetPercent: 85,
            mockMethods: "@([PSCustomObject]@{ Active = $true; InstanceName = 'TARGET_PANEL' })",
            mockInvoke: "[PSCustomObject]@{ ReturnValue = 0 }",
            mockBrightness: "@([PSCustomObject]@{ Active = $true; InstanceName = 'OTHER_PANEL'; CurrentBrightness = 12 })");
        check(missingTargetResult.Status == "SuccessUnconfirmed" && missingTargetResult.Confirmed == null, "mock CIM missing target panel returns SuccessUnconfirmed and never borrows other panel value");

        // 8. Mock CIM 测试 - 目标实例存在但非活动状态时不读回
        var inactiveTargetResult = await RunMockScriptAsync(
            targetPercent: 85,
            mockMethods: "@([PSCustomObject]@{ Active = $true; InstanceName = 'TARGET_PANEL' })",
            mockInvoke: "[PSCustomObject]@{ ReturnValue = 0 }",
            mockBrightness: "@([PSCustomObject]@{ Active = $false; InstanceName = 'TARGET_PANEL'; CurrentBrightness = 85 })");
        check(inactiveTargetResult.Status == "SuccessUnconfirmed" && inactiveTargetResult.Confirmed == null, "mock CIM inactive target panel returns SuccessUnconfirmed");

        // 9. Mock CIM 测试 - 目标标识缺失且存在多块活动屏幕时不借用 (消除歧义)
        var ambiguousResult = await RunMockScriptAsync(
            targetPercent: 50,
            mockMethods: "@([PSCustomObject]@{ Active = $true; InstanceName = '' })",
            mockInvoke: "[PSCustomObject]@{ ReturnValue = 0 }",
            mockBrightness: "@([PSCustomObject]@{ Active = $true; InstanceName = 'PANEL_A'; CurrentBrightness = 30 }, [PSCustomObject]@{ Active = $true; InstanceName = 'PANEL_B'; CurrentBrightness = 60 })");
        check(ambiguousResult.Status == "SuccessUnconfirmed" && ambiguousResult.Confirmed == null, "mock CIM missing target identifier with multiple active panels returns SuccessUnconfirmed");

        // 10. Mock CIM 测试 - 目标标识缺失但仅有单块活动屏幕时安全读回
        var singlePanelResult = await RunMockScriptAsync(
            targetPercent: 50,
            mockMethods: "@([PSCustomObject]@{ Active = $true; InstanceName = '' })",
            mockInvoke: "[PSCustomObject]@{ ReturnValue = 0 }",
            mockBrightness: "@([PSCustomObject]@{ Active = $true; InstanceName = 'SINGLE_PANEL'; CurrentBrightness = 50 })");
        check(singlePanelResult.Status == "Success" && singlePanelResult.Confirmed == 50, "mock CIM missing target identifier with single active panel returns Success");
    }

    private static async Task<bool> TestSyntaxInPs51Async(string scriptContent)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"ps51_syntax_{Guid.NewGuid():N}.ps1");
        try
        {
            await File.WriteAllTextAsync(tempFile, scriptContent, Encoding.UTF8);
            var parseCmd = $"$content = [System.IO.File]::ReadAllText('{tempFile.Replace("'", "''")}', [System.Text.Encoding]::UTF8); " +
                           "$tokens = $null; $errors = $null; " +
                           "[void][System.Management.Automation.Language.Parser]::ParseInput($content, [ref]$tokens, [ref]$errors); " +
                           "if ($null -eq $errors -or $errors.Count -eq 0) { 'OK' } else { 'ERRORS' }";

            var start = new ProcessStartInfo(PsPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(parseCmd)) })
                start.ArgumentList.Add(a);

            using var p = Process.Start(start);
            if (p == null)
                return false;
            var output = (await p.StandardOutput.ReadToEndAsync()).Trim();
            await p.WaitForExitAsync();
            return p.ExitCode == 0 && output == "OK";
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
            catch { }
        }
    }

    private static async Task<(string Status, int? Confirmed, string? Message)> RunMockScriptAsync(
        int targetPercent,
        string mockMethods,
        string mockInvoke,
        string mockBrightness)
    {
        var script = WindowsBrightnessService.GetScript();
        var mockSetup = $@"
function Get-CimInstance {{
    param([string]$Namespace, [string]$ClassName, [string]$ErrorAction)
    if ($ClassName -eq 'WmiMonitorBrightnessMethods') {{ return {mockMethods} }}
    if ($ClassName -eq 'WmiMonitorBrightness') {{ return {mockBrightness} }}
    return $null
}}
function Invoke-CimMethod {{
    param($InputObject, [string]$MethodName, $Arguments, [string]$ErrorAction)
    return {mockInvoke}
}}
$percent = {targetPercent}
";
        var fullScript = mockSetup + "\n" + script;

        var start = new ProcessStartInfo(PsPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(fullScript)) })
            start.ArgumentList.Add(a);

        using var p = Process.Start(start) ?? throw new Exception("Failed to start powershell process");
        var output = (await p.StandardOutput.ReadToEndAsync()).Trim();
        await p.WaitForExitAsync();

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        var status = root.GetProperty("Status").GetString() ?? "";
        int? confirmed = root.TryGetProperty("Confirmed", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : null;
        string? msg = root.TryGetProperty("Message", out var m) ? m.GetString() : null;
        return (status, confirmed, msg);
    }
}
