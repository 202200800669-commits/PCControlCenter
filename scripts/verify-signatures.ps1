#requires -Version 5.1
param(
    [string]$PackageDirectory
)
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $PSScriptRoot
if (-not $PackageDirectory) {
    $artifacts = Join-Path $scriptRoot 'artifacts'
    if (Test-Path $artifacts) {
        $latest = Get-ChildItem -LiteralPath $artifacts -Directory -Filter 'pc-control-alpha-*' |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if ($latest) { $PackageDirectory = $latest.FullName }
    }
}
if (-not $PackageDirectory -or -not (Test-Path -LiteralPath $PackageDirectory)) {
    throw "未找到有效的发布目录: $PackageDirectory"
}

Write-Output "=== 正在核验发布包数字签名状态 ==="
Write-Output "包目录: $PackageDirectory"

$targetFiles = @('pc-control.exe', 'pc-control-desktop.exe', 'pc-control-broker.exe')
$results = @()
$unsignedCount = 0

foreach ($file in $targetFiles) {
    $path = Join-Path $PackageDirectory $file
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Warning "文件不存在: $file"
        continue
    }
    $sig = Get-AuthenticodeSignature -LiteralPath $path
    if ($sig.Status -eq 'NotSigned') {
        $unsignedCount++
    }
    $results += [PSCustomObject]@{
        File       = $file
        Status     = $sig.Status
        StatusMsg  = $sig.StatusMessage
        Subject    = if ($sig.SignerCertificate) { $sig.SignerCertificate.Subject } else { '无' }
        Thumbprint = if ($sig.SignerCertificate) { $sig.SignerCertificate.Thumbprint } else { '无' }
        Timestamp  = if ($sig.TimeStamperCertificate) { $sig.TimeStamperCertificate.Subject } else { '无时间戳' }
    }
}

$results | Format-Table -AutoSize -Property File, Status, Subject, Timestamp

if ($unsignedCount -gt 0) {
    Write-Warning "发现 $unsignedCount 个文件尚未进行代码签名。"
} else {
    Write-Output "所有核心可执行文件均已附带 Authenticode 签名记录。"
}
