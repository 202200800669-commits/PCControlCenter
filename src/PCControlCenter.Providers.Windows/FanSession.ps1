# Embedded resource. All parameters are generated from validated typed values.
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
$inputReader=New-Object IO.StreamReader([Console]::OpenStandardInput())
$stop=$inputReader.ReadLineAsync()
$result=@{Code='NOT_STARTED';Recovery='NOT_NEEDED';ObservedFan1=$null;ObservedFan2=$null;AfterFan1=$null;AfterFan2=$null}
$touched=$false;$locked=$false;$mutex=$null
function ReadFanFeature([uint32]$id) {
 $v=Invoke-CimMethod -InputObject $script:fan -MethodName GetFeatureValue -Arguments @{IDs=$id} -OperationTimeoutSec 3
 if($v.ReturnValue -ne $true -or $null -eq $v.value){throw 'READ_FAILED'}
 return [int]$v.value
}
function WriteFanFeature([uint32]$id,[int]$value) {
 if($id -eq 0x04020000){if($value -notin @(0,1)){throw 'INVALID_OVERRIDE'}}
 elseif($id -in @(0x04030001,0x04030002)){if($value -ne 0 -and ($value -lt 1500 -or $value -gt 5500)){throw 'INVALID_RPM'}}
 else{throw 'INVALID_FEATURE'}
 $null=Invoke-CimMethod -InputObject $script:fan -MethodName SetFeatureValue -Arguments @{IDs=$id;value=[uint32]$value} -OperationTimeoutSec 3
}
try {
 if($mode -notin @('manual','full','auto')){throw 'INVALID_MODE'}
 if($mode -eq 'manual' -and ($rpm1 -lt 1500 -or $rpm1 -gt 5500 -or $rpm2 -lt 1500 -or $rpm2 -gt 5500)){throw 'INVALID_RPM'}
 if($mode -ne 'manual' -and ($rpm1 -ne 0 -or $rpm2 -ne 0)){throw 'INVALID_RPM'}
 if(($mode -eq 'auto' -and $seconds -ne 0) -or ($mode -ne 'auto' -and ($seconds -lt 5 -or $seconds -gt 30))){throw 'INVALID_LEASE'}
 $c=Get-CimInstance Win32_ComputerSystem -Property Manufacturer -OperationTimeoutSec 3
 $p=Get-CimInstance Win32_ComputerSystemProduct -Property Name,Version -OperationTimeoutSec 3
 $b=Get-CimInstance Win32_BIOS -Property SMBIOSBIOSVersion -OperationTimeoutSec 3
 if($c.Manufacturer -ne 'LENOVO' -or $p.Name -ne '21R0' -or $p.Version -ne 'ThinkBook 16p G6 IAX' -or $b.SMBIOSBIOSVersion -ne 'R2CN57WW'){throw 'IDENTITY_MISMATCH'}
 $a=@(Get-CimInstance -Namespace root/wmi -ClassName LENOVO_OTHER_METHOD -OperationTimeoutSec 3|Where-Object Active)
 $l=@(Get-CimInstance -Namespace root/wmi -ClassName LENOVO_FAN_TEST_DATA -OperationTimeoutSec 3|Where-Object Active)
 if($a.Count -ne 1 -or $l.Count -ne 1){throw 'INTERFACE_UNAVAILABLE'}
 if($l[0].FanId.Count -ne 2 -or $l[0].FanId[0] -ne 1 -or $l[0].FanId[1] -ne 2 -or $l[0].FanMinSpeed.Count -ne 2 -or $l[0].FanMaxSpeed.Count -ne 2){throw 'LIMITS_CHANGED'}
 for($i=0;$i -lt 2;$i++){if($l[0].FanMinSpeed[$i] -ne 1500 -or $l[0].FanMaxSpeed[$i] -ne 5500){throw 'LIMITS_CHANGED'}}
 $script:fan=$a[0]
 $mutex=New-Object Threading.Mutex($false,'GlobalPCControlCenter.ThinkBookFanSession')
 try{$locked=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$locked=$true}
 if(!$locked){$result.Code='BUSY';throw 'BUSY'}
 if($stop.IsCompleted){$result.Code='CANCELLED_BEFORE_WRITE';throw 'CANCELLED'}
 $clock=[Diagnostics.Stopwatch]::StartNew()
 $touched=$true
 WriteFanFeature 0x04020000 0
 WriteFanFeature 0x04030001 $rpm1
 WriteFanFeature 0x04030002 $rpm2
 if($mode -eq 'full'){WriteFanFeature 0x04020000 1}
 $expected=0;if($mode -eq 'full'){$expected=1}
 if((ReadFanFeature 0x04020000) -ne $expected){throw 'OVERRIDE_MISMATCH'}
 $result.Code='COMPLETED'
 while($clock.Elapsed.TotalSeconds -lt $seconds) {
  if($stop.IsCompleted){$result.Code='INTERRUPTED';break}
  $result.ObservedFan1=ReadFanFeature 0x04030001
  $result.ObservedFan2=ReadFanFeature 0x04030002
  if($result.ObservedFan1 -lt 0 -or $result.ObservedFan1 -gt 10000 -or $result.ObservedFan2 -lt 0 -or $result.ObservedFan2 -gt 10000){throw 'INVALID_READING'}
  Start-Sleep -Milliseconds 250
 }
 if($mode -eq 'manual' -and $result.Code -eq 'COMPLETED' -and ([math]::Abs($result.ObservedFan1-$rpm1) -gt 250 -or [math]::Abs($result.ObservedFan2-$rpm2) -gt 250)){$result.Code='TARGET_NOT_REACHED'}
} catch {if($result.Code -notin @('BUSY','CANCELLED_BEFORE_WRITE')){$result.Code='CONTROL_FAILED'}}
finally {
 if($touched) {
  $restoreFailed=$false
  foreach($id in @([uint32]0x04020000,[uint32]0x04030001,[uint32]0x04030002)) {
   try{WriteFanFeature $id 0}catch{$restoreFailed=$true}
  }
  try{if((ReadFanFeature 0x04020000) -ne 0){$restoreFailed=$true}}catch{$restoreFailed=$true}
  if($restoreFailed){$result.Recovery='UNCONFIRMED'}else{$result.Recovery='AUTO_COMMANDS_SENT_OVERRIDE_OFF'}
  try{Start-Sleep -Milliseconds 1200;$result.AfterFan1=ReadFanFeature 0x04030001;$result.AfterFan2=ReadFanFeature 0x04030002}catch{}
 }
 if($locked){try{$mutex.ReleaseMutex()}catch{}}
 if($mutex){$mutex.Dispose()}
}
$result|ConvertTo-Json -Compress
