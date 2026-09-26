<#
.SYNOPSIS
自检：确认集控仓库里 sync-plugins.json 列出的地址在客户端网络上真的可用。

.DESCRIPTION
逐个请求清单里的主源与镜像，检查：
  1. HTTP 状态码是否为 200
  2. 下载内容的 sha256 是否与清单里的 Hash 一致
两项都过，教室机才能正常同步。

.EXAMPLE
./verify-remote.ps1 -ManifestPath "E:\ClassIsland-Control\sync-plugins.json"
#>
param(
    [string]$ManifestPath = "E:\ClassIsland-Control\sync-plugins.json"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ManifestPath)) {
    throw "找不到清单：$ManifestPath"
}

$manifest = Get-Content $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
Write-Host ("清单 {0}  Version={1}" -f (Split-Path $ManifestPath -Leaf), $manifest.Version)
Write-Host ("已发布确认：https://gcore.jsdelivr.net/gh/wngmay/ZhongXianMiddleSchool-ClassislandControl@main/sync-plugins.json")
Write-Host ""

function Test-Entry([string]$Label, [string]$Url, [string]$ExpectedHash) {
    try {
        $temp = Join-Path $env:TEMP ("cis-verify-" + [guid]::NewGuid().ToString("N"))
        $response = Invoke-WebRequest -Uri $Url -OutFile $temp -TimeoutSec 120 -UseBasicParsing
        $actual = (Get-FileHash $temp -Algorithm SHA256).Hash.ToLowerInvariant()
        Remove-Item $temp -Force -ErrorAction SilentlyContinue

        $expected = if ($ExpectedHash -match '^(?:sha256:)?([0-9a-fA-F]{64})$') { $Matches[1].ToLowerInvariant() } else { $null }
        if ($expected -and $actual -ne $expected) {
            Write-Host ("  [MISMATCH] {0}" -f $Label) -ForegroundColor Red
            Write-Host ("     期望 {0}" -f $expected)
            Write-Host ("     实际 {0}" -f $actual)
            return $false
        }

        $size = [math]::Round((Get-Item $temp -ErrorAction SilentlyContinue).Length / 1KB, 1)
        Write-Host ("  [OK] {0}  ({1})" -f $Label, $Url) -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host ("  [FAIL] {0}  ({1})" -f $Label, $Url) -ForegroundColor Red
        Write-Host ("     {0}" -f $_.Exception.Message)
        return $false
    }
}

$allOk = $true
foreach ($plugin in $manifest.Plugins) {
    Write-Host ("插件 {0} {1}" -f $plugin.Id, $plugin.Version)
    $allOk = (Test-Entry "主源" $plugin.Url $plugin.Hash) -and $allOk
    foreach ($mirror in $plugin.Mirrors) {
        $allOk = (Test-Entry "镜像" $mirror $plugin.Hash) -and $allOk
    }
}

if ($manifest.Automation) {
    Write-Host ("自动化规则 Version={0}" -f $manifest.Automation.Version)
    $allOk = (Test-Entry "主源" $manifest.Automation.Url $manifest.Automation.Hash) -and $allOk
    foreach ($mirror in $manifest.Automation.Mirrors) {
        $allOk = (Test-Entry "镜像" $mirror $manifest.Automation.Hash) -and $allOk
    }
}

Write-Host ""
if ($allOk) {
    Write-Host "全部地址可用且哈希一致，教室机可以正常同步。" -ForegroundColor Green
}
else {
    Write-Host "存在不可用或哈希不一致的地址，请检查是否已 push 到 main 分支。" -ForegroundColor Yellow
}
