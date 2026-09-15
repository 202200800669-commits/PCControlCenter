$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
 $dotnet='dotnet'
 $portable=Join-Path (Get-Location) 'local/dotnet10/dotnet.exe'
 if(Test-Path -LiteralPath $portable){$dotnet=$portable}
 $stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
 $bundle=Join-Path (Get-Location) ('artifacts/pc-control-alpha-'+$stamp)
 & $dotnet publish src/PCControlCenter.Cli -c Release -r win-x64 --self-contained true -o $bundle
 if($LASTEXITCODE -ne 0){throw 'Publish failed'}
 & $dotnet publish src/PCControlCenter.Broker -c Release -r win-x64 --self-contained true -o $bundle
 if($LASTEXITCODE -ne 0){throw 'Broker publish failed'}
 $runtime=Get-Content -LiteralPath (Join-Path $bundle 'pc-control.runtimeconfig.json') -Raw | ConvertFrom-Json
 $runtimeVersion=($runtime.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App').version
 if(!$runtimeVersion){throw 'Self-contained runtime version missing'}
 $assets=Get-Content -LiteralPath 'src/PCControlCenter.Cli/obj/project.assets.json' -Raw | ConvertFrom-Json
 $runtimePackage=$null
 foreach($folder in $assets.packageFolders.psobject.Properties.Name){
  $candidate=Join-Path $folder ('microsoft.netcore.app.runtime.win-x64/'+$runtimeVersion)
  if(Test-Path -LiteralPath (Join-Path $candidate 'LICENSE.TXT')){$runtimePackage=$candidate;break}
 }
 if(!$runtimePackage){throw 'Runtime license files missing'}
 Copy-Item -LiteralPath (Join-Path $runtimePackage 'LICENSE.TXT') -Destination (Join-Path $bundle 'DOTNET-LICENSE.txt')
 Copy-Item -LiteralPath (Join-Path $runtimePackage 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $bundle 'DOTNET-THIRD-PARTY-NOTICES.txt')
 Copy-Item -LiteralPath 'THIRD_PARTY_NOTICES.md' -Destination $bundle
 Copy-Item -LiteralPath 'LICENSE.pending.md' -Destination $bundle
 $sdk=Get-Content -LiteralPath global.json -Raw | ConvertFrom-Json
 $commit=git rev-parse HEAD
 if($LASTEXITCODE -ne 0){throw 'Source commit unavailable'}
 $dirty=[bool](git status --porcelain)
 [ordered]@{schemaVersion=1;sourceCommit=$commit;sourceDirty=$dirty;sdk=$sdk.sdk.version;runtime=$runtimeVersion;rid='win-x64';selfContained=$true;builtAtUtc=[DateTime]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $bundle 'BUILD.json') -Encoding utf8
 $usage=@'
PC Control Center 0.1.0-alpha.10
Windows x64. .NET 10 runtime is included; no separate runtime installation required.
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
 & (Join-Path $PSScriptRoot 'verify-package.ps1') -PackageDirectory $bundle
 Compress-Archive -LiteralPath $bundle -DestinationPath ($bundle+'.zip')
 Write-Output $bundle
} finally {Pop-Location}
