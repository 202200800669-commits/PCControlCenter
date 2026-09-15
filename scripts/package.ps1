$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
 $stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
 $bundle=Join-Path (Get-Location) ('artifacts/pc-control-alpha-'+$stamp)
 dotnet publish src/PCControlCenter.Cli -c Release --no-self-contained -o $bundle
 if($LASTEXITCODE -ne 0){throw 'Publish failed'}
 dotnet publish src/PCControlCenter.Broker -c Release --no-self-contained -o $bundle
 if($LASTEXITCODE -ne 0){throw 'Broker publish failed'}
 $usage=@'
PC Control Center 0.1.0-alpha.3
Windows x64 + .NET 9 Runtime required. This package is framework-dependent.
Double-click probe.cmd for read-only detection.
Terminal: pc-control.exe probe --elevated (one-time UAC for fan reads).
Terminal: pc-control.exe export feedback.zip
Review diagnostics.json before sharing. Nothing is uploaded automatically.
Experimental reference ThinkBook only: fans manual 3500 4500 12
Trial restores firmware auto commands after expiry or disconnect.
No persistent hardware control or new graphical interface/icon is included.
This is a local development package; publication and licensing review are pending.
'@
 [IO.File]::WriteAllText((Join-Path $bundle 'USAGE.txt'),$usage,[Text.Encoding]::UTF8)
 $launcher=@'
@echo off
chcp 65001 >nul
cd /d "%~dp0"
pc-control.exe probe
pause
'@
 [IO.File]::WriteAllText((Join-Path $bundle 'probe.cmd'),$launcher,[Text.Encoding]::ASCII)
 Get-ChildItem -LiteralPath $bundle -File | Where-Object Extension -eq '.pdb' | Remove-Item
 $hashes=Get-ChildItem -LiteralPath $bundle -File | ForEach-Object { [ordered]@{file=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} }
 $hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $bundle 'SHA256.json') -Encoding utf8
 Compress-Archive -LiteralPath $bundle -DestinationPath ($bundle+'.zip')
 Write-Output $bundle
} finally {Pop-Location}
