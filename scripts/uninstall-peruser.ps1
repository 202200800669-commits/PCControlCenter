#requires -Version 5.1
param(
    [switch]$Quiet
)
$ErrorActionPreference = 'SilentlyContinue'

if (-not $Quiet) {
    Write-Output "正在卸载 PC Control Center..."
}

# 1. 停止相关运行中进程
Get-Process -Name 'pc-control-desktop','pc-control','pc-control-broker' -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }

# 2. 清理开机自启动注册表
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'PCControlCenter' -ErrorAction SilentlyContinue

# 3. 清理快捷方式
$startShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\PC Control Center.lnk'
if (Test-Path -LiteralPath $startShortcut) {
    Remove-Item -LiteralPath $startShortcut -Force -ErrorAction SilentlyContinue
}

$desktopDir = [Environment]::GetFolderPath('Desktop')
$desktopShortcut = Join-Path $desktopDir 'PC Control Center.lnk'
if (Test-Path -LiteralPath $desktopShortcut) {
    Remove-Item -LiteralPath $desktopShortcut -Force -ErrorAction SilentlyContinue
}

# 4. 清理控制面板卸载注册表
Remove-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PCControlCenter' -Recurse -Force -ErrorAction SilentlyContinue

# 5. 清理程序目录
$targetDir = Join-Path $env:LOCALAPPDATA 'Programs\PCControlCenter'
if (Test-Path -LiteralPath $targetDir) {
    # 异步延时彻底移除安装根目录（避免正在运行本脚本的文件锁占用）
    Start-Process -FilePath cmd.exe -ArgumentList "/c timeout /t 1 /nobreak >nul & rmdir /s /q `"$targetDir`"" -WindowStyle Hidden
}

if (-not $Quiet) {
    Write-Output "PC Control Center 已干净卸载。"
}
