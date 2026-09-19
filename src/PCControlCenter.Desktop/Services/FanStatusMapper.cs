using PCControlCenter.Core;
using PCControlCenter.Ipc;

namespace PCControlCenter.Desktop.Services;

public record FanUiStatus(string FanStateText, string StatusText, bool IsError);

public static class FanStatusMapper
{
    public static FanUiStatus MapRestoreAuto(BrokerResponse? resp, bool cancelled)
    {
        if (cancelled)
        {
            return new("恢复未确认", "恢复自动已请求取消 (恢复未确认)", true);
        }
        if (resp == null)
        {
            return new("恢复未确认", "通信中断 (恢复未确认)", true);
        }
        if (resp.Code != "OK" || resp.Receipt is null)
        {
            return new("恢复未确认", $"恢复自动未确认 (Broker: {resp.Code})", true);
        }

        var fr = resp.Receipt;
        if (fr.Code == "COMPLETED")
        {
            if (fr.Recovery == "AUTO_COMMANDS_SENT_OVERRIDE_OFF")
            {
                return new("自动控制指令已发送", "自动控制指令已发送 (覆盖已关闭)", false);
            }
            if (fr.Recovery == "UNCONFIRMED")
            {
                return new("恢复未确认", "恢复指令已发送但固件未确认", true);
            }
            return new("恢复提示", $"恢复完成: {fr.Recovery}", false);
        }

        return new("恢复未确认", $"恢复自动未成功 (Code={fr.Code}, Recovery={fr.Recovery})", true);
    }

    public static FanUiStatus MapTrial(BrokerResponse? resp, bool cancelled, string mode = "manual")
    {
        if (cancelled)
        {
            return new("试运行已取消 (恢复未确认)", "试运行已取消 (等待恢复确认)", true);
        }
        if (resp == null)
        {
            return new("试运行未完成 (通信中断)", "风扇试运行通信中断", true);
        }
        if (resp.Code != "OK" || resp.Receipt is null)
        {
            return new($"试运行失败: {resp.Code}", $"风扇试运行失败: {resp.Code}", true);
        }

        var fr = resp.Receipt;
        if (fr.Code == "COMPLETED")
        {
            if (fr.Recovery == "AUTO_COMMANDS_SENT_OVERRIDE_OFF")
            {
                string desc = mode == "full" ? "全速散热试运行完成 (自动控制指令已发送)" : "试运行完成 (自动控制指令已发送)";
                return new(desc, "试运行完成并已发送自动控制指令", false);
            }
            if (fr.Recovery == "UNCONFIRMED")
            {
                return new("试运行完成 (恢复未确认)", "试运行完成，但自动恢复未确认", true);
            }
            return new($"试运行完成 ({fr.Recovery})", $"试运行完成: {fr.Recovery}", false);
        }

        if (fr.Recovery == "UNCONFIRMED" || fr.Recovery == "ROLLBACK_FAILED")
        {
            return new($"试运行未完成 ({fr.Code})", $"试运行未完成 ({fr.Code}), 恢复未确认", true);
        }

        return new($"试运行未完成: {fr.Code}", $"试运行未完成 ({fr.Code}), 恢复: {fr.Recovery}", true);
    }
}
