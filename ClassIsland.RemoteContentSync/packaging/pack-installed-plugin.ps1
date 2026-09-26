<#
.SYNOPSIS
把 ClassIsland 已安装的插件目录打包成可分发的 .cipx。

.DESCRIPTION
.cipx 本质是一个 zip 包，包根必须含 manifest.yml。
主机的 PluginService.ProcessPluginsInstall() 会读取包内 manifest.yml 的 id，
把整包解压到 {AppRoot}/Plugins/{id}/ —— 所以「把插件目录原样 zip」就是标准的 .cipx。

.PARAMETER PluginDir
已安装插件目录，例如 {AppRoot}/Plugins/uachope.classisland

.PARAMETER OutDir
输出目录，默认当前目录下的 plugins/

.PARAMETER Version
覆盖 manifest.yml 里读到的版本号（一般不需要）

.EXAMPLE
# 打包本机已装的 UACHope
./pack-installed-plugin.ps1 -PluginDir "D:\toolapp\ClassIsland_app_windows_x64_selfContained_folder\data\Plugins\uachope.classisland"

.EXAMPLE
# 一次性打包全部已安装插件
./pack-installed-plugin.ps1 -All -AppRoot "D:\...\data" -OutDir "E:\ClassIsland-Control\plugins"
#>
param(
    [string]$PluginDir,
    [string]$OutDir = (Join-Path (Get-Location) "plugins"),
    [string]$Version,

    [switch]$All,
    [string]$AppRoot,
    [string[]]$ExcludeId = @()
)

$ErrorActionPreference = "Stop"

function Get-ManifestField([string]$ManifestPath, [string]$Field) {
    foreach ($line in (Get-Content $ManifestPath)) {
        $trimmed = $line.Trim()
        if ($trimmed.StartsWith('#')) { continue }
        if ($trimmed -match "^$($Field)\s*:\s*(.+)$") {
            return $Matches[1].Trim().Trim('"').Trim("'")
        }
    }
    return $null
}

function Pack-One([string]$Dir, [string]$Destination, [string]$OverrideVersion) {
    $manifest = Join-Path $Dir "manifest.yml"
    if (-not (Test-Path $manifest)) {
        throw "目录中没有 manifest.yml：$Dir"
    }

    $id = Get-ManifestField $manifest "id"
    $ver = if ($OverrideVersion) { $OverrideVersion } else { Get-ManifestField $manifest "version" }
    if (-not $id) { throw "无法从 manifest.yml 读取 id：$manifest" }
    if (-not $ver) { $ver = "0.0.0" }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $outFile = Join-Path $Destination "$id-$ver.cipx"
    if (Test-Path $outFile) { Remove-Item $outFile -Force }

    Compress-Archive -Path (Join-Path $Dir '*') -DestinationPath $outFile -CompressionLevel Optimal

    $sizeKb = [math]::Round((Get-Item $outFile).Length / 1KB, 1)
    $hash = (Get-FileHash $outFile -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host ("已生成 {0}  ({1} KB)" -f (Split-Path $outFile -Leaf), $sizeKb)
    Write-Host ("  sha256:{0}" -f $hash)
    return $outFile
}

if ($All) {
    if (-not $AppRoot) { throw "使用 -All 时必须提供 -AppRoot（ClassIsland 数据根目录）" }
    $pluginsRoot = Join-Path $AppRoot "Plugins"
    if (-not (Test-Path $pluginsRoot)) { throw "找不到插件目录：$pluginsRoot" }

    Get-ChildItem -Path $pluginsRoot -Directory | ForEach-Object {
        if ($ExcludeId -contains $_.Name) {
            Write-Host ("跳过 {0}" -f $_.Name)
            return
        }
        Pack-One -Dir $_.FullName -Destination $OutDir
    }
}
else {
    if (-not $PluginDir) { throw "需要提供 -PluginDir，或使用 -All -AppRoot <路径>" }
    Pack-One -Dir $PluginDir -Destination $OutDir -OverrideVersion $Version
}
