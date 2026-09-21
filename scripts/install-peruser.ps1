#requires -Version 5.1
param(
    [string]$SourceDirectory
)
$ErrorActionPreference = 'Stop'

if (-not $SourceDirectory) {
    $scriptRoot = Split-Path -Parent $PSScriptRoot
    $artifacts = Join-Path $scriptRoot 'artifacts'
    if (Test-Path $artifacts) {
        $latest = Get-ChildItem -LiteralPath $artifacts -Directory -Filter 'pc-control-alpha-*' |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if ($latest) { $SourceDirectory = $latest.FullName }
    }
    if (-not $SourceDirectory) {
        $candidate = Split-Path -Parent $PSScriptRoot
        if (Test-Path (Join-Path $candidate 'pc-control-desktop.exe')) {
            $SourceDirectory = $candidate
        }
    }
}

if (-not $SourceDirectory -or -not (Test-Path -LiteralPath (Join-Path $SourceDirectory 'pc-control-desktop.exe'))) {
    throw "未找到有效的 PC Control Center 发布目录: $SourceDirectory"
}

$targetDir = Join-Path $env:LOCALAPPDATA 'Programs\PCControlCenter'
Write-Output "正在安装 PC Control Center..."
Write-Output "源目录: $SourceDirectory"
Write-Output "目标目录: $targetDir"

# 硬件会话必须先自行完成恢复，安装程序不能强制终止它。
if (Get-Process -Name 'pc-control-desktop','pc-control','pc-control-broker' -ErrorAction SilentlyContinue) {
    throw '请先恢复自动散热并从托盘退出 PC Control Center，等待控制进程结束后重试安装。'
}

if (-not (Test-Path -LiteralPath $targetDir)) {
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
}

# 复制文件
Get-ChildItem -LiteralPath $SourceDirectory | Copy-Item -Destination $targetDir -Recurse -Force

# 确保卸载脚本存在于安装目录
$uninstallTarget = Join-Path $targetDir 'uninstall-peruser.ps1'
$uninstallSrc = Join-Path $PSScriptRoot 'uninstall-peruser.ps1'
if (Test-Path -LiteralPath $uninstallSrc) {
    Copy-Item -LiteralPath $uninstallSrc -Destination $uninstallTarget -Force
}

# 创建快捷方式
$wsh = New-Object -ComObject WScript.Shell
$desktopExe = Join-Path $targetDir 'pc-control-desktop.exe'
$iconPath = Join-Path $targetDir 'Control.ico'

# 开始菜单快捷方式
$startMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
if (Test-Path -LiteralPath $startMenuDir) {
    $startShortcut = $wsh.CreateShortcut((Join-Path $startMenuDir 'PC Control Center.lnk'))
    $startShortcut.TargetPath = $desktopExe
    $startShortcut.WorkingDirectory = $targetDir
    if (Test-Path -LiteralPath $iconPath) { $startShortcut.IconLocation = "$iconPath,0" }
    $startShortcut.Description = 'PC Control Center · 多品牌电脑控制中心'
    $startShortcut.Save()
}

# 桌面快捷方式
$desktopDir = [Environment]::GetFolderPath('Desktop')
if (Test-Path -LiteralPath $desktopDir) {
    $desktopShortcut = $wsh.CreateShortcut((Join-Path $desktopDir 'PC Control Center.lnk'))
    $desktopShortcut.TargetPath = $desktopExe
    $desktopShortcut.WorkingDirectory = $targetDir
    if (Test-Path -LiteralPath $iconPath) { $desktopShortcut.IconLocation = "$iconPath,0" }
    $desktopShortcut.Description = 'PC Control Center · 多品牌电脑控制中心'
    $desktopShortcut.Save()
}

# 注册控制面板“安装的应用”
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PCControlCenter'
if (-not (Test-Path -LiteralPath $uninstallKey)) {
    New-Item -Path $uninstallKey -Force | Out-Null
}
$props = @{
    DisplayName         = 'PC Control Center'
    DisplayVersion     = '0.1.0-alpha.15'
    Publisher           = 'PC Control Center'
    DisplayIcon         = $iconPath
    InstallLocation     = $targetDir
    UninstallString     = "powershell.exe -ExecutionPolicy Bypass -File `"$uninstallTarget`""
    QuietUninstallString= "powershell.exe -ExecutionPolicy Bypass -File `"$uninstallTarget`" -Quiet"
    HelpLink            = 'https://github.com/202200800669-commits/PCControlCenter'
    URLInfoAbout        = 'https://github.com/202200800669-commits/PCControlCenter'
    NoModify            = 1
    NoRepair            = 1
}
foreach ($name in $props.Keys) {
    Set-ItemProperty -Path $uninstallKey -Name $name -Value $props[$name] -Force
}

Write-Output "安装成功完成！已创建桌面与开始菜单快捷方式，并已注册系统卸载信息。"
