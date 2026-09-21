using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace PCControlCenter.Desktop.Services;

public static class SystemMetrics
{
    private static ulong prevIdle, prevTotal;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint Load;
        public ulong Total;
        public ulong Available;
        public ulong Page;
        public ulong AvailablePage;
        public ulong Virtual;
        public ulong AvailableVirtual;
        public ulong Extended;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    public static double ReadCpuLoad()
    {
        if (!OperatingSystem.IsWindows())
            return 0;
        try
        {
            if (GetSystemTimes(out ulong idle, out ulong kernel, out ulong user))
            {
                ulong total = kernel + user;
                if (prevTotal > 0 && total > prevTotal)
                {
                    double load = 100.0 * (1.0 - (double)(idle - prevIdle) / (total - prevTotal));
                    prevIdle = idle;
                    prevTotal = total;
                    return Math.Clamp(load, 0, 100);
                }
                prevIdle = idle;
                prevTotal = total;
            }
        }
        catch { }
        return 0;
    }

    public static (double UsedGb, double TotalGb, double LoadPercent) ReadMemory()
    {
        if (!OperatingSystem.IsWindows())
            return (0, 0, 0);
        try
        {
            var mem = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (GlobalMemoryStatusEx(ref mem))
            {
                double totalGb = mem.Total / 1073741824.0;
                double usedGb = (mem.Total - mem.Available) / 1073741824.0;
                return (usedGb, totalGb, mem.Load);
            }
        }
        catch { }
        return (0, 0, 0);
    }

    public static string ReadBattery()
    {
        if (!OperatingSystem.IsWindows())
            return "电池状态暂不可用";
        try
        {
            var p = Forms.SystemInformation.PowerStatus;
            if (p.BatteryChargeStatus != Forms.BatteryChargeStatus.Unknown && p.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery))
                return "未检测到电池";
            string percent = p.BatteryLifePercent >= 0 && p.BatteryLifePercent <= 1 ? p.BatteryLifePercent.ToString("P0", CultureInfo.InvariantCulture) : "—";
            string line = p.PowerLineStatus switch
            {
                Forms.PowerLineStatus.Online => "外接电源",
                Forms.PowerLineStatus.Offline => "电池供电",
                _ => "供电状态未知"
            };
            return $"电池 {percent}  ·  {line}";
        }
        catch
        {
            return "电池状态暂不可用";
        }
    }

    public static string DescribeBatteryState(Forms.BatteryChargeStatus status, Forms.PowerLineStatus power, float percent)
    {
        if (status == Forms.BatteryChargeStatus.Unknown)
            return "充电状态未知";
        if (status.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery))
            return "未检测到电池";
        if (status.HasFlag(Forms.BatteryChargeStatus.Charging))
            return "正在充电";
        if (power == Forms.PowerLineStatus.Online)
            return percent >= 0.995f && percent <= 1 ? "已充满" : "已接电 · 未充电";
        return power == Forms.PowerLineStatus.Offline ? "正在使用电池" : "充电状态未知";
    }

    public static string ReadBatteryState()
    {
        try
        {
            var p = Forms.SystemInformation.PowerStatus;
            return DescribeBatteryState(p.BatteryChargeStatus, p.PowerLineStatus, p.BatteryLifePercent);
        }
        catch { return "充电状态未知"; }
    }
}
