# Embedded resource. PowerShell 5.1 compatible.
$ErrorActionPreference='Stop'
try {
    $methods = @(Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorBrightnessMethods -ErrorAction SilentlyContinue | Where-Object { $_.Active })
    if ($methods.Count -eq 0) {
        $methods = @(Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorBrightnessMethods -ErrorAction SilentlyContinue)
    }
    if ($null -eq $methods -or $methods.Count -eq 0) {
        @{ Status = 'Unsupported'; Confirmed = $null } | ConvertTo-Json -Compress
        exit 0
    }

    $selectedMethod = $methods[0]
    $inst = $selectedMethod.InstanceName
    $target = [byte]$percent

    $inv = Invoke-CimMethod -InputObject $selectedMethod -MethodName WmiSetBrightness -Arguments @{ Timeout = 2; Brightness = $target } -ErrorAction Stop
    if ($null -ne $inv -and ($inv.PSObject.Properties['ReturnValue']) -and $inv.ReturnValue -ne 0) {
        @{ Status = 'Failed'; Confirmed = $null; Message = "WmiSetBrightness returned non-zero code: $($inv.ReturnValue)" } | ConvertTo-Json -Compress
        exit 0
    }

    $cur = $null
    try {
        $brightnessList = @(Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorBrightness -ErrorAction SilentlyContinue)
        $matched = @($brightnessList | Where-Object { $_.Active -and ($null -eq $inst -or $_.InstanceName -eq $inst) })
        if ($matched.Count -eq 0) {
            $matched = @($brightnessList | Where-Object { $_.Active })
        }
        if ($matched.Count -gt 0 -and $null -ne $matched[0].CurrentBrightness) {
            $cur = [int]$matched[0].CurrentBrightness
        }
    } catch { }

    if ($null -ne $cur) {
        @{ Status = 'Success'; Confirmed = $cur } | ConvertTo-Json -Compress
    } else {
        @{ Status = 'SuccessUnconfirmed'; Confirmed = $null } | ConvertTo-Json -Compress
    }
} catch {
    @{ Status = 'Failed'; Confirmed = $null; Message = $_.Exception.Message } | ConvertTo-Json -Compress
}
