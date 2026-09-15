#requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$start=[Diagnostics.ProcessStartInfo]::new('git')
$start.WorkingDirectory=$root
$start.UseShellExecute=$false
$start.CreateNoWindow=$true
$start.RedirectStandardOutput=$true
$start.RedirectStandardError=$true
foreach($argument in @('ls-files','-z')){$start.ArgumentList.Add($argument)}
$process=[Diagnostics.Process]::Start($start)
try {
 $output=$process.StandardOutput.ReadToEndAsync()
 $errors=$process.StandardError.ReadToEndAsync()
 $process.WaitForExit()
 $null=$errors.GetAwaiter().GetResult()
 if($process.ExitCode -ne 0){throw 'Cannot enumerate tracked source'}
 $files=@($output.GetAwaiter().GetResult().Split([char]0,[StringSplitOptions]::RemoveEmptyEntries))
} finally {$process.Dispose()}
if($files.Count -eq 0){throw 'No tracked source files'}
foreach($relative in $files) {
 if($relative -match '(^|/)(local|artifacts|bin|obj|\.vs|node_modules)/' -or $relative -match '\.(exe|dll|zip|pfx|p12|snk|pem|key)$'){throw ('Excluded source artifact: '+$relative)}
 $path=Join-Path $root $relative
 $file=Get-Item -LiteralPath $path -Force
 if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){throw ('Tracked link requires review: '+$relative)}
 if($file.Extension -in @('.cs','.ps1','.md','.json','.yml','.yaml','.props','.csproj','.xml','.txt','.config')) {
  if($file.Length -gt 2097152){throw ('Source file exceeds review limit: '+$relative)}
  $body=[IO.File]::ReadAllText($path)
  if($body -match '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----' -or $body -match '\bghp_[A-Za-z0-9]{36}\b' -or $body -match '\bgithub_pat_[A-Za-z0-9_]{80,}\b'){throw ('Possible credential in: '+$relative)}
 }
}
Write-Output ('Source boundary checks passed: '+$files.Count+' tracked files. Manual privacy and license review still required.')
