using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PCControlCenter.Desktop.Services;

public static class EnergyReader
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, nint sec, uint creation, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle h, uint code, ref uint input, int insize, out uint output, int outsize, out uint returned, nint overlap);

    private static uint? Call(uint code, uint value)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var h = CreateFile(@"\\.\EnergyDrv", 0xC0000000, 3, 0, 3, 0x80, 0);
            if (h.IsInvalid) return null;
            if (!DeviceIoControl(h, code, ref value, 4, out uint result, 4, out uint bytes, 0)) return null;
            if (bytes != 4) return null;
            return result;
        }
        catch
        {
            return null;
        }
    }

    public static int ReadCharge()
    {
        var res = Call(0x831020F8, 255);
        if (res is null) return -1;
        uint v = res.Value;
        return (v & 32) != 0 ? 1 : ((v & 4) != 0 ? 2 : 0);
    }

    public static int ReadNight()
    {
        var res = Call(0x83102150, 17);
        if (res is null) return -1;
        uint v = res.Value;
        if ((v & 1) == 0) return -1;
        return (v & 16) != 0 ? 1 : 0;
    }

    private static uint? KeyArg(uint command, uint level)
    {
        var config = Call(0x83102144, 1);
        if (config is null) return null;
        uint c = config.Value & 0xFFFFFFFEu;
        uint token = c << 3;
        if ((token & ~0xFFF0u) != 0) return null;
        return token | command | (level << 16);
    }

    public static int ReadKeyboard()
    {
        var arg = KeyArg(2, 0);
        if (arg is null) return -1;
        var res = Call(0x83102144, arg.Value);
        if (res is null) return -1;
        uint v = res.Value & 7;
        return v switch
        {
            1 => 0, // off
            3 => 1, // low
            5 => 2, // high
            7 => 3, // auto
            _ => -1
        };
    }
}
