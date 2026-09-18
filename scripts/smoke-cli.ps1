#requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$PackageDirectory)
$ErrorActionPreference='Stop'
$cli=Join-Path (Resolve-Path -LiteralPath $PackageDirectory).Path 'pc-control.exe'
$cases=@(
 @{Args=@('--help');Code=0},
 @{Args=@('unknown-command');Code=2},
 @{Args=@('fans','manual','1499','4500','12');Code=2},
 @{Args=@('fans','manual','3500','4500','31');Code=2},
 @{Args=@('fans','full','0');Code=2},
 @{Args=@('fans','auto','unexpected');Code=2},
 @{Args=@('watch','0');Code=2},
 @{Args=@('watch','31');Code=2},
 @{Args=@('watch','3','--elevated');Code=2},
 @{Args=@('mode','2');Code=2},
 @{Args=@('mode','bad');Code=2},
 @{Args=@('energy','charge','3');Code=2},
 @{Args=@('energy','key','5');Code=2},
 @{Args=@('energy','night','2');Code=2},
 @{Args=@('inspect-report',(Join-Path $env:TEMP ([Guid]::NewGuid().ToString('N')+'.zip')));Code=11}
)
foreach($case in $cases) {
 $start=New-Object Diagnostics.ProcessStartInfo
 $start.FileName=$cli
 $start.UseShellExecute=$false
 $start.CreateNoWindow=$true
 $start.RedirectStandardOutput=$true
 $start.RedirectStandardError=$true
 # All arguments in these cases are fixed tokens or a generated temporary path.
 foreach($argument in $case.Args){$start.ArgumentList.Add($argument)}
 $process=[Diagnostics.Process]::Start($start)
 try {
  $output=$process.StandardOutput.ReadToEndAsync()
  $errorOutput=$process.StandardError.ReadToEndAsync()
  if(!$process.WaitForExit(10000)){$process.Kill();throw 'CLI validation unexpectedly stalled'}
  $null=$output.GetAwaiter().GetResult();$null=$errorOutput.GetAwaiter().GetResult()
  if($process.ExitCode -ne $case.Code){throw ('Unexpected exit code for '+($case.Args -join ' '))}
 } finally {$process.Dispose()}
}
Write-Output ('CLI smoke checks passed: '+$cases.Count+'; no valid hardware operation requested.')
