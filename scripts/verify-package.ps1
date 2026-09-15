param([Parameter(Mandatory=$true)][string]$PackageDirectory)
$ErrorActionPreference='Stop'
$root=Get-Item -LiteralPath $PackageDirectory
if(!$root.PSIsContainer -or ($root.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Expected a regular package directory'}
$files=@(Get-ChildItem -LiteralPath $root.FullName -Force)
if($files | Where-Object {$_.PSIsContainer -or ($_.Attributes -band [IO.FileAttributes]::ReparsePoint)}){throw 'Unexpected directory or link in package'}
$manifestPath=Join-Path $root.FullName 'SHA256.json'
if((Get-Item -LiteralPath $manifestPath).Length -gt 1048576){throw 'Manifest too large'}
$entries=@(Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json)
if($entries.Count -eq 0 -or $entries.Count -gt 1024){throw 'Invalid manifest size'}
$names=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($entry in $entries) {
 if($entry.file -isnot [string] -or $entry.file -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,200}$' -or $entry.file -eq 'SHA256.json' -or !$names.Add($entry.file)){throw 'Invalid or duplicate manifest name'}
 if($entry.sha256 -isnot [string] -or $entry.sha256 -notmatch '^[A-Fa-f0-9]{64}$'){throw 'Invalid hash'}
 $path=Join-Path $root.FullName $entry.file
 if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw ('Missing file: '+$entry.file)}
 if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256){throw ('Hash mismatch: '+$entry.file)}
}
foreach($file in $files){if($file.Name -ne 'SHA256.json' -and !$names.Contains($file.Name)){throw ('Unlisted file: '+$file.Name)}}
foreach($required in @('pc-control.exe','pc-control-broker.exe','coreclr.dll','BUILD.json','DOTNET-LICENSE.txt','DOTNET-THIRD-PARTY-NOTICES.txt','LICENSE.pending.md')) {
 if(!$names.Contains($required)){throw ('Required file missing: '+$required)}
}
$build=Get-Content -LiteralPath (Join-Path $root.FullName 'BUILD.json') -Raw | ConvertFrom-Json
if($build.schemaVersion -ne 1 -or $build.rid -ne 'win-x64' -or $build.selfContained -ne $true -or $build.sourceCommit -notmatch '^[a-f0-9]{40}$'){throw 'Invalid build metadata'}
Write-Output ('Package verified: '+$entries.Count+' files. Hashes verify integrity, not publisher identity.')
