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
            string percent = p.BatteryLifePercent >= 0 && p.BatteryLifePercent <= 1 ? p.BatteryLifePercent.ToString("P0", CultureInfo.InvariantCulture) : "—";
            string line = p.PowerLineStatus == Forms.PowerLineStatus.Online ? "外接电源" : "电池供电";
            return $"电池 {percent}  ·  {line}";
        }
        catch
        {
            return "电池状态暂不可用";
        }
    }
}
