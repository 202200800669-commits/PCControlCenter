# Embedded resource. All parameters are generated from validated typed values.
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
$inputReader=New-Object IO.StreamReader([Console]::OpenStandardInput())
$stop=$inputReader.ReadLineAsync()

$result=@{Code='NOT_STARTED';TargetMode=$targetMode;PreviousMode=$null;FinalMode=$null;Recovery='NOT_NEEDED'}
$touched=$false;$locked=$false;$mutex=$null;$fanLocked=$false;$fanMutex=$null

function Get-ItsMode {
    $k=Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\LenovoProcessManagement\Performance\PowerSlider' -ErrorAction Stop
    $v=$k.ITS_CurrentSetting
    if(($k.ITS_FN_Capability -band 16) -ne 0){$v=$k.ITS_CurrentSettingV}
    if($null -eq $v){return $null}
    $intVal=[int]$v
    if($intVal -notin @(0,1,3)){return $null}
    return $intVal
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
    try{$fanLocked=$fanMutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$fanLocked=$true}
    if(!$fanLocked){
        $result.Code='BUSY'
        throw 'BUSY'
    }

    $mutex=New-Object Threading.Mutex($false,'Global\PCControlCenter.ThinkBookModeSession')
    try{$locked=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$locked=$true}
    if(!$locked){
        $result.Code='BUSY'
        throw 'BUSY'
    }

    $prev=Get-ItsMode
    if($null -eq $prev -or $prev -notin @(0,1,3)){
        $result.Code='INVALID_PREVIOUS_MODE'
        throw 'INVALID_PREVIOUS_MODE'
    }
    $result.PreviousMode=$prev

    if($prev -eq $targetMode){
        $result.FinalMode=$prev
        $result.Code='COMPLETED'
        $result.Recovery='NOT_NEEDED'
    } else {
        $touched=$true
        Send-ItsCommand $targetMode
        $confirmed=$false
        for($j=0;$j -lt 15;$j++){
            if($stop.IsCompleted){$result.Code='INTERRUPTED';break}
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
        if(!$confirmed -and $result.Code -ne 'INTERRUPTED'){
            $result.Code='TARGET_NOT_REACHED'
        }
    }
} catch {
    if($result.Code -notin @('BUSY','IDENTITY_MISMATCH','SERVICE_UNAVAILABLE','TARGET_NOT_REACHED','INTERRUPTED','INVALID_PREVIOUS_MODE')){
        $result.Code='CONTROL_FAILED'
    }
} finally {
    if($touched -and $result.Code -ne 'COMPLETED' -and $prev -in @(0,1,3)){
        try {
            Send-ItsCommand $prev
            $restored=$false
            for($r=0;$r -lt 10;$r++){
                Start-Sleep -Milliseconds 200
                if((Get-ItsMode) -eq $prev){
                    $restored=$true
                    $result.Recovery='RESTORED_PREVIOUS'
                    $result.FinalMode=$prev
                    break
                }
            }
            if(!$restored){
                $result.Recovery='ROLLBACK_FAILED'
                try{$result.FinalMode=Get-ItsMode}catch{}
            }
        } catch {
            $result.Recovery='ROLLBACK_FAILED'
            try{$result.FinalMode=Get-ItsMode}catch{}
        }
    }
    if($locked){try{$mutex.ReleaseMutex()}catch{}}
    if($mutex){$mutex.Dispose()}
    if($fanLocked){try{$fanMutex.ReleaseMutex()}catch{}}
    if($fanMutex){$fanMutex.Dispose()}
}

$result|ConvertTo-Json -Compress
