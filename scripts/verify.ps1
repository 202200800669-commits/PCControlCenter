$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
 $dotnet='dotnet'
 $portable=Join-Path (Get-Location) 'local/dotnet10/dotnet.exe'
 if(Test-Path -LiteralPath $portable){$dotnet=$portable}
 & $dotnet build PCControlCenter.sln -c Release
 if($LASTEXITCODE -ne 0){throw 'Build failed'}
 & $dotnet run --project tests/PCControlCenter.Tests -c Release --no-build
 if($LASTEXITCODE -ne 0){throw 'Tests failed'}
} finally {Pop-Location}
