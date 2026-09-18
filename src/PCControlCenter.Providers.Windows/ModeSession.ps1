# Embedded resource. All parameters are generated from validated typed values.
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)

$result=@{Code='NOT_STARTED';TargetMode=$targetMode;PreviousMode=$null;FinalMode=$null;Recovery='NOT_NEEDED'}
$locked=$false;$mutex=$null

function Get-ItsMode {
    $k=Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\LenovoProcessManagement\Performance\PowerSlider' -ErrorAction Stop
    $v=$k.ITS_CurrentSetting
    if(($k.ITS_FN_Capability -band 16) -ne 0){$v=$k.ITS_CurrentSettingV}
    return [int]$v
}

if (!(Get-Command Send-ItsCommand -ErrorAction SilentlyContinue)) {
    function Send-ItsCommand([int]$m) {
        $cmd=@{0=163;1=164;3=165}[$m]
        if($null -eq $cmd){throw 'INVALID_MODE'}
        $svc=New-Object System.ServiceProcess.ServiceController('LenovoProcessManagement')
        try{$svc.ExecuteCommand($cmd)}finally{$svc.Dispose()}
    }
}

try {
    if($targetMode -notin @(0,1,3)){throw 'INVALID_TARGET_MODE'}

    $c=Get-CimInstance Win32_ComputerSystem -Property Manufacturer -OperationTimeoutSec 3
    $p=Get-CimInstance Win32_ComputerSystemProduct -Property Name,Version -OperationTimeoutSec 3
    $b=Get-CimInstance Win32_BIOS -Property SMBIOSBIOSVersion -OperationTimeoutSec 3
    if($c.Manufacturer -ne 'LENOVO' -or $p.Name -ne '21R0' -or $p.Version -ne 'ThinkBook 16p G6 IAX' -or $b.SMBIOSBIOSVersion -ne 'R2CN57WW'){
        $result.Code='IDENTITY_MISMATCH'
        throw 'IDENTITY_MISMATCH'
    }

    $k=Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\LenovoProcessManagement\Performance\PowerSlider' -ErrorAction Stop
    if($k.Version -lt 8192 -or (Get-Service LenovoProcessManagement -ErrorAction Stop).Status -ne 'Running'){
        $result.Code='SERVICE_UNAVAILABLE'
        throw 'SERVICE_UNAVAILABLE'
    }

    $fanMutex=New-Object Threading.Mutex($false,'Global\PCControlCenter.ThinkBookFanSession')
    $fanLocked=$false
    try{$fanLocked=$fanMutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$fanLocked=$true}
    if(!$fanLocked){
        $fanMutex.Dispose()
        $result.Code='BUSY'
        throw 'BUSY'
    }
    try{$fanMutex.ReleaseMutex()}catch{}
    $fanMutex.Dispose()

    $mutex=New-Object Threading.Mutex($false,'Global\PCControlCenter.ThinkBookModeSession')
    try{$locked=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$locked=$true}
    if(!$locked){
        $result.Code='BUSY'
        throw 'BUSY'
    }

    $prev=Get-ItsMode
    $result.PreviousMode=$prev
    if($prev -eq $targetMode){
        $result.FinalMode=$prev
        $result.Code='COMPLETED'
        $result.Recovery='NOT_NEEDED'
    } else {
        Send-ItsCommand $targetMode
        $confirmed=$false
        for($j=0;$j -lt 15;$j++){
            Start-Sleep -Milliseconds 200
            $cur=Get-ItsMode
            if($cur -eq $targetMode){
                $confirmed=$true
                $result.FinalMode=$cur
                $result.Code='COMPLETED'
                $result.Recovery='NOT_NEEDED'
                break
            }
        }
        if(!$confirmed){
            $result.Code='TARGET_NOT_REACHED'
            try {
                Send-ItsCommand $prev
                for($r=0;$r -lt 10;$r++){
                    Start-Sleep -Milliseconds 200
                    if((Get-ItsMode) -eq $prev){
                        $result.Recovery='RESTORED_PREVIOUS'
                        $result.FinalMode=$prev
                        break
                    }
                }
                if($result.Recovery -ne 'RESTORED_PREVIOUS'){
                    $result.Recovery='ROLLBACK_FAILED'
                    $result.FinalMode=Get-ItsMode
                }
            } catch {
                $result.Recovery='ROLLBACK_FAILED'
                try{$result.FinalMode=Get-ItsMode}catch{}
            }
        }
    }
} catch {
    if($result.Code -notin @('BUSY','IDENTITY_MISMATCH','SERVICE_UNAVAILABLE','TARGET_NOT_REACHED')){
        $result.Code='CONTROL_FAILED'
    }
} finally {
    if($locked){try{$mutex.ReleaseMutex()}catch{}}
    if($mutex){$mutex.Dispose()}
}

$result|ConvertTo-Json -Compress
