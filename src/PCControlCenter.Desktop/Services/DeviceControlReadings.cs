using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PCControlCenter.Desktop.Services;

public sealed record DeviceControlReadings(int Mode, int Charge, int Night, int Keyboard)
{
    // These are read-only cached OEM values. Hardware writes still perform full broker identity checks.
    public static int ReadMode()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\LenovoProcessManagement\Performance\PowerSlider");
            if (key?.GetValue("Version") is not int version || version < 8192)
                return -1;
            bool alternate = key.GetValue("ITS_FN_Capability") is int caps && (caps & 16) != 0;
            return key.GetValue(alternate ? "ITS_CurrentSettingV" : "ITS_CurrentSetting") is int mode && mode is 0 or 1 or 3 or 4 ? mode : -1;
        }
        catch { return -1; }
    }

    public static Task<DeviceControlReadings> ReadAsync(CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        return new DeviceControlReadings(ReadMode(), EnergyReader.ReadCharge(), EnergyReader.ReadNight(), EnergyReader.ReadKeyboard());
    }, ct);
}
