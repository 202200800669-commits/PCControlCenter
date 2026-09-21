#requires -Version 5.1
param(
    [switch]$Quiet
)
$ErrorActionPreference = 'SilentlyContinue'

if (-not $Quiet) {
    Write-Output "正在卸载 PC Control Center..."
}

# 1. 先让硬件会话自行恢复，不强制终止控制进程。
if (Get-Process -Name 'pc-control-desktop','pc-control','pc-control-broker' -ErrorAction SilentlyContinue) {
    Write-Error '请先恢复自动散热并从托盘退出 PC Control Center，等待控制进程结束后重试卸载。' -ErrorAction Stop
}

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
    $expectedRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs'))
    $resolvedTarget = (Resolve-Path -LiteralPath $targetDir).Path
    if ([IO.Path]::GetDirectoryName($resolvedTarget) -ne $expectedRoot -or [IO.Path]::GetFileName($resolvedTarget) -ne 'PCControlCenter' -or (Get-Item -LiteralPath $resolvedTarget).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw '安装目录校验失败，未删除文件。'
    }
    Remove-Item -LiteralPath $resolvedTarget -Recurse -Force -ErrorAction Stop
}

if (-not $Quiet) {
    Write-Output "PC Control Center 已干净卸载。"
}
