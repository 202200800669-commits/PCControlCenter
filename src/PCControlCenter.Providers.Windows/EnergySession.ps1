# Embedded resource. All parameters are generated from validated typed values.
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
$inputReader=New-Object IO.StreamReader([Console]::OpenStandardInput())
$stop=$inputReader.ReadLineAsync()

$result=@{Code='NOT_STARTED';Kind=$kind;TargetValue=$value;PreviousValue=$null;FinalValue=$null;Recovery='NOT_NEEDED'}
$touched=$false;$locked=$false;$mutex=$null;$fanLocked=$false;$fanMutex=$null

if (!(Get-Command Invoke-EnergyRead -ErrorAction SilentlyContinue)) {
    if (!([System.Management.Automation.PSTypeName]'EnergyBridge').Type) {
        $csharp = @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class EnergyBridge {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code, ref uint input, int insize, out uint output, int outsize, out uint returned, IntPtr overlap);

    static uint Call(uint code, uint value, bool expectReply = true) {
        using (var h = CreateFile(@"\\.\EnergyDrv", 0xC0000000, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero)) {
            if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            uint result, bytes;
            if (!DeviceIoControl(h, code, ref value, 4, out result, 4, out bytes, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (expectReply && bytes != 4) throw new InvalidOperationException("Device returned no readable state");
            return result;
        }
    }

    public static int ChargeRead() { uint v = Call(0x831020F8, 255); return (v & 32) != 0 ? 1 : ((v & 4) != 0 ? 2 : 0); }
    public static void ChargeSet(int mode) {
        if (mode < 0 || mode > 2) throw new ArgumentException("charge");
        if (mode == 1) { Call(0x831020F8, 8, false); Call(0x831020F8, 3, false); }
        else { Call(0x831020F8, 5, false); Call(0x831020F8, mode == 2 ? 7u : 8u, false); }
    }

    public static int NightRead() { uint v = Call(0x83102150, 17); if ((v & 1) == 0) throw new InvalidOperationException("Night charge unavailable"); return (v & 16) != 0 ? 1 : 0; }
    public static void NightSet(int mode) { if (mode != 0 && mode != 1) throw new ArgumentException("night"); Call(0x83102150, mode == 1 ? 0x80000012u : 0x12u, false); }

    static uint KeyArg(uint command, uint level) {
        uint config = Call(0x83102144, 1) & 0xFFFFFFFEu;
        uint token = config << 3;
        if ((token & ~0xFFF0u) != 0) throw new InvalidOperationException("Keyboard token invalid");
        return token | command | (level << 16);
    }

    public static int KeyRead() {
        uint v = Call(0x83102144, KeyArg(2, 0)) & 7;
        switch (v) { case 1: return 0; case 3: return 1; case 5: return 2; case 7: return 3; default: throw new InvalidOperationException("Unknown keyboard state"); }
    }

    public static void KeySet(int level) {
        if (level < 0 || level > 3) throw new ArgumentException("key");
        Call(0x83102144, KeyArg(3, (uint)level), false);
    }
}
'@
        Add-Type -TypeDefinition $csharp
    }

    function Invoke-EnergyRead([string]$k) {
        switch ($k) {
            'charge' { return [EnergyBridge]::ChargeRead() }
            'night'  { return [EnergyBridge]::NightRead() }
            'key'    { return [EnergyBridge]::KeyRead() }
            default  { throw 'INVALID_KIND' }
        }
    }

    function Invoke-EnergySet([string]$k, [int]$v) {
        switch ($k) {
            'charge' { [EnergyBridge]::ChargeSet($v) }
            'night'  { [EnergyBridge]::NightSet($v) }
            'key'    { [EnergyBridge]::KeySet($v) }
            default  { throw 'INVALID_KIND' }
        }
    }
}

try {
    if ($kind -eq 'charge' -and ($value -lt 0 -or $value -gt 2)) { throw 'INVALID_VALUE' }
    elseif ($kind -eq 'night' -and ($value -notin @(0, 1))) { throw 'INVALID_VALUE' }
    elseif ($kind -eq 'key' -and ($value -lt 0 -or $value -gt 3)) { throw 'INVALID_VALUE' }
    elseif ($kind -notin @('charge', 'night', 'key')) { throw 'INVALID_KIND' }

    $c=Get-CimInstance Win32_ComputerSystem -Property Manufacturer -OperationTimeoutSec 3
    $p=Get-CimInstance Win32_ComputerSystemProduct -Property Name,Version -OperationTimeoutSec 3
    $b=Get-CimInstance Win32_BIOS -Property SMBIOSBIOSVersion -OperationTimeoutSec 3
    if ($c.Manufacturer -ne 'LENOVO' -or $p.Name -ne '21R0' -or $p.Version -ne 'ThinkBook 16p G6 IAX' -or $b.SMBIOSBIOSVersion -ne 'R2CN57WW') {
        $result.Code='IDENTITY_MISMATCH'
        throw 'IDENTITY_MISMATCH'
    }

    $fanMutex=New-Object Threading.Mutex($false, 'Global\PCControlCenter.ThinkBookFanSession')
    try { $fanLocked=$fanMutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $fanLocked=$true }
    if (!$fanLocked) {
        $result.Code='BUSY'
        throw 'BUSY'
    }

    $mutex=New-Object Threading.Mutex($false, 'Global\PCControlCenter.ThinkBookEnergySession')
    try { $locked=$mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked=$true }
    if (!$locked) {
        $result.Code='BUSY'
        throw 'BUSY'
    }

    $prev = Invoke-EnergyRead $kind
    if ($null -eq $prev) {
        $result.Code='INVALID_PREVIOUS_VALUE'
        throw 'INVALID_PREVIOUS_VALUE'
    }
    $result.PreviousValue = $prev

    if ($stop.IsCompleted) {
        $result.Code = 'CANCELLED_BEFORE_WRITE'
        $result.FinalValue = $prev
        $result.Recovery = 'NOT_NEEDED'
        throw 'CANCELLED_BEFORE_WRITE'
    }

    if ($prev -eq $value) {
        $result.FinalValue = $prev
        $result.Code = 'COMPLETED'
        $result.Recovery = 'NOT_NEEDED'
    } else {
        $touched = $true
        Invoke-EnergySet $kind $value
        $confirmed = $false
        for ($j=0; $j -lt 8; $j++) {
            if ($stop.IsCompleted) { $result.Code = 'INTERRUPTED'; break }
            Start-Sleep -Milliseconds 150
            $cur = Invoke-EnergyRead $kind
            if ($cur -eq $value) {
                $confirmed = $true
                $result.FinalValue = $cur
                $result.Code = 'COMPLETED'
                $result.Recovery = 'NOT_NEEDED'
                break
            }
        }
        if (!$confirmed -and $result.Code -ne 'INTERRUPTED') {
            $result.Code = 'TARGET_NOT_REACHED'
        }
    }
} catch {
    if ($result.Code -notin @('BUSY', 'IDENTITY_MISMATCH', 'TARGET_NOT_REACHED', 'INTERRUPTED', 'INVALID_PREVIOUS_VALUE', 'CANCELLED_BEFORE_WRITE')) {
        $result.Code = 'CONTROL_FAILED'
    }
} finally {
    if ($touched -and $result.Code -ne 'COMPLETED' -and $null -ne $prev) {
        try {
            Invoke-EnergySet $kind $prev
            $restored = $false
            for ($r=0; $r -lt 8; $r++) {
                Start-Sleep -Milliseconds 150
                if ((Invoke-EnergyRead $kind) -eq $prev) {
                    $restored = $true
                    $result.Recovery = 'RESTORED_PREVIOUS'
                    $result.FinalValue = $prev
                    break
                }
            }
            if (!$restored) {
                $result.Recovery = 'ROLLBACK_FAILED'
                try { $result.FinalValue = Invoke-EnergyRead $kind } catch {}
            }
        } catch {
            $result.Recovery = 'ROLLBACK_FAILED'
            try { $result.FinalValue = Invoke-EnergyRead $kind } catch {}
        }
    }
    if ($locked) { try { $mutex.ReleaseMutex() } catch {} }
    if ($mutex) { $mutex.Dispose() }
    if ($fanLocked) { try { $fanMutex.ReleaseMutex() } catch {} }
    if ($fanMutex) { $fanMutex.Dispose() }
}

$result | ConvertTo-Json -Compress