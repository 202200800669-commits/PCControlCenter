using PCControlCenter.Core;
using PCControlCenter.Ipc;
using PCControlCenter.Desktop.Services;

static class FanStatusMapperTests
{
    public static void RunAll(Action<bool, string> check)
    {
        // 1. COMPLETED + UNCONFIRMED 绝不出现“已恢复自动”
        var unconfirmedResp = new BrokerResponse(BrokerProtocol.Version, "req1", "OK", Receipt: new FanReceipt("COMPLETED", "UNCONFIRMED"));
        var res1 = FanStatusMapper.MapTrial(unconfirmedResp, cancelled: false);
        check(res1.FanStateText.Contains("恢复未确认"), "completed trial with unconfirmed recovery shows unconfirmed");
        check(!res1.FanStateText.Contains("已恢复自动") && !res1.StatusText.Contains("已恢复自动"), "completed unconfirmed never claims auto restored");

        var resAuto1 = FanStatusMapper.MapRestoreAuto(unconfirmedResp, cancelled: false);
        check(!resAuto1.FanStateText.Contains("已恢复自动") && !resAuto1.StatusText.Contains("已恢复自动"), "restore auto with unconfirmed never claims auto restored");

        // 2. COMPLETED + AUTO_COMMANDS_SENT_OVERRIDE_OFF 显示指令已发送，不盲目宣称固件已自动
        var autoSentResp = new BrokerResponse(BrokerProtocol.Version, "req2", "OK", Receipt: new FanReceipt("COMPLETED", "AUTO_COMMANDS_SENT_OVERRIDE_OFF"));
        var res2 = FanStatusMapper.MapTrial(autoSentResp, cancelled: false);
        check(res2.FanStateText.Contains("自动控制指令已发送"), "trial shows auto commands sent");
        check(!res2.FanStateText.Contains("固件自动"), "trial does not claim firmware auto verified");

        var resAuto2 = FanStatusMapper.MapRestoreAuto(autoSentResp, cancelled: false);
        check(resAuto2.FanStateText.Contains("自动控制指令已发送"), "restore auto shows auto commands sent");
        check(!resAuto2.FanStateText.Contains("固件自动"), "restore auto does not claim firmware auto verified");

        // 3. 取消无回执 绝不出现“已恢复自动”
        var res3 = FanStatusMapper.MapTrial(null, cancelled: true);
        check(res3.FanStateText.Contains("恢复未确认"), "cancelled trial without receipt shows unconfirmed");
        check(!res3.FanStateText.Contains("已恢复自动") && !res3.StatusText.Contains("已恢复自动"), "cancelled trial never claims auto restored");

        var resAuto3 = FanStatusMapper.MapRestoreAuto(null, cancelled: true);
        check(!resAuto3.FanStateText.Contains("已恢复自动") && !resAuto3.StatusText.Contains("已恢复自动"), "cancelled restore auto never claims auto restored");

        // 4. 恢复失败 绝不出现“已恢复自动”
        var failResp = new BrokerResponse(BrokerProtocol.Version, "req4", "OK", Receipt: new FanReceipt("CONTROL_FAILED", "ROLLBACK_FAILED"));
        var res4 = FanStatusMapper.MapTrial(failResp, cancelled: false);
        check(res4.IsError, "failed trial marked as error");
        check(!res4.FanStateText.Contains("已恢复自动") && !res4.StatusText.Contains("已恢复自动"), "failed trial never claims auto restored");

        var resAuto4 = FanStatusMapper.MapRestoreAuto(failResp, cancelled: false);
        check(resAuto4.IsError, "failed restore auto marked as error");
        check(!resAuto4.FanStateText.Contains("已恢复自动") && !resAuto4.StatusText.Contains("已恢复自动"), "failed restore auto never claims auto restored");
    }
}
