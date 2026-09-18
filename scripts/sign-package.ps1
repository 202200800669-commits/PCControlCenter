#requires -Version 5.1
param(
    [string]$PackageDirectory,
    [string]$PfxPath = $env:CODE_SIGNING_PFX_PATH,
    [string]$PfxPassword = $env:CODE_SIGNING_PASSWORD,
    [string]$CertificateThumbprint = $env:CODE_SIGNING_THUMBPRINT
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

Write-Output "=== PC Control Center 代码签名工作流 ==="
Write-Output "目标包目录: $PackageDirectory"

# 1. 检测 signtool.exe
$signtool = $null
$sdkCandidates = @(
    Get-ChildItem -LiteralPath 'C:\Program Files (x86)\Windows Kits\10\bin' -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match 'x64' } |
        Sort-Object FullName -Descending
)
if ($sdkCandidates.Count -gt 0) {
    $signtool = $sdkCandidates[0].FullName
    Write-Output "已定位 Windows SDK SignTool: $signtool"
} else {
    Write-Output "未检测到 SignTool.exe，将使用 PowerShell Set-AuthenticodeSignature 方案。"
}

# 2. 准备代码签名证书
$tempPfxCreated = $false
$tempPfxPath = $null
$tempPassword = $null
$certObject = $null

if ($PfxPath -and (Test-Path -LiteralPath $PfxPath)) {
    Write-Output "使用指定 PFX 证书: $PfxPath"
    $tempPfxPath = $PfxPath
    $tempPassword = $PfxPassword
    if ($tempPassword) {
        $certObject = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($tempPfxPath, $tempPassword)
    } else {
        $certObject = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($tempPfxPath)
    }
} elseif ($CertificateThumbprint) {
    Write-Output "使用证书指纹检索当前用户证书库: $CertificateThumbprint"
    $certObject = Get-Item -LiteralPath ("Cert:\CurrentUser\My\" + $CertificateThumbprint) -ErrorAction SilentlyContinue
    if (-not $certObject) { throw "未在当前用户证书库中找到指纹为 $CertificateThumbprint 的代码签名证书" }
} else {
    Write-Output "未指定外部证书，正在生成本地自签名开发测试证书（仅用于本地验证，不入库）..."
    $devCert = New-SelfSignedCertificate -Type CodeSigningCert `
        -Subject "CN=PC Control Center (Dev Test), O=PC Control Center" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -HashAlgorithm SHA256 `
        -NotAfter (Get-Date).AddDays(7)
    $certObject = $devCert
    $tempPfxCreated = $true
    $tempPfxPath = Join-Path $env:TEMP ("pccc-test-sign-" + [Guid]::NewGuid().ToString('N') + ".pfx")
    $tempPassword = [Guid]::NewGuid().ToString('N')
    $securePass = ConvertTo-SecureString -String $tempPassword -AsPlainText -Force
    Export-PfxCertificate -Cert $devCert -FilePath $tempPfxPath -Password $securePass | Out-Null
    Write-Output "自签名测试证书已生成: $($devCert.Thumbprint) (临时导出: $tempPfxPath)"
}

try {
    # 3. 执行二进制签名
    $filesToSign = @('pc-control.exe', 'pc-control-desktop.exe', 'pc-control-broker.exe')
    foreach ($fileName in $filesToSign) {
        $filePath = Join-Path $PackageDirectory $fileName
        if (-not (Test-Path -LiteralPath $filePath)) {
            Write-Warning "未在包中找到文件: $fileName，跳过。"
            continue
        }

        Write-Output "正在签署: $fileName..."
        if ($signtool -and $tempPfxPath) {
            $timestampServers = @('http://timestamp.digicert.com', 'http://timestamp.sectigo.com')
            $signed = $false
            foreach ($ts in $timestampServers) {
                $args = @('sign', '/f', $tempPfxPath)
                if ($tempPassword) { $args += @('/p', $tempPassword) }
                $args += @('/fd', 'SHA256', '/tr', $ts, '/td', 'SHA256', $filePath)
                
                $p = Start-Process -FilePath $signtool -ArgumentList $args -Wait -PassThru -NoNewWindow
                if ($p.ExitCode -eq 0) {
                    Write-Output "  -> SignTool 签名成功（含时间戳: $ts）"
                    $signed = $true
                    break
                }
            }
            if (-not $signed) {
                # 离线降级签名（不含时间戳）
                $args = @('sign', '/f', $tempPfxPath)
                if ($tempPassword) { $args += @('/p', $tempPassword) }
                $args += @('/fd', 'SHA256', $filePath)
                $p = Start-Process -FilePath $signtool -ArgumentList $args -Wait -PassThru -NoNewWindow
                if ($p.ExitCode -ne 0) { throw "SignTool 签名失败: $fileName (ExitCode: $($p.ExitCode))" }
                Write-Output "  -> SignTool 离线签名成功（无时间戳）"
            }
        } else {
            # PowerShell Set-AuthenticodeSignature 降级
            $sig = Set-AuthenticodeSignature -FilePath $filePath -Certificate $certObject -HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com' -ErrorAction SilentlyContinue
            if ($sig.Status -ne 'Valid' -and $sig.Status -ne 'UnknownError') {
                # 离线不带时间戳再试一次
                $sig = Set-AuthenticodeSignature -FilePath $filePath -Certificate $certObject -HashAlgorithm SHA256
            }
            Write-Output "  -> Authenticode 签名状态: $($sig.Status)"
        }
    }

    # 4. 签名后更新哈希清单
    Write-Output "二进制已签署，正在重新计算并更新包内 SHA256.json..."
    $hashes = Get-ChildItem -LiteralPath $PackageDirectory -File |
        Where-Object { $_.Name -ne 'SHA256.json' } |
        ForEach-Object { [ordered]@{file = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } }
    $hashes | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PackageDirectory 'SHA256.json') -Encoding utf8

    # 5. 校验包完整性
    Write-Output "正在重新运行完整性校验..."
    & (Join-Path $PSScriptRoot 'verify-package.ps1') -PackageDirectory $PackageDirectory
    Write-Output "=== 代码签名与清单更新全部完成！ ==="
}
finally {
    # 清理临时导出的 PFX 文件
    if ($tempPfxCreated -and $tempPfxPath -and (Test-Path -LiteralPath $tempPfxPath)) {
        Remove-Item -LiteralPath $tempPfxPath -Force -ErrorAction SilentlyContinue
    }
}
