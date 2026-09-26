<#
.SYNOPSIS
打包 ClassIsland 远程内容同步插件为 .cipx。

.DESCRIPTION
.cipx 本质是 zip 包，包根必须含 manifest.yml。
本脚本只收集「插件 DLL + manifest.yml + icon.png」，避免把 Avalonia 等传递依赖打进插件目录。
#>
param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0",
    [string]$ProjectDir = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

$assembly = "ClassIsland.RemoteContentSync"
$stage = Join-Path $ProjectDir "obj/cipx"
$outDir = Join-Path $ProjectDir "cipx"
$outFile = Join-Path $outDir "$assembly-$Version.cipx"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

$payload = @(
    (Join-Path $ProjectDir "bin/$Configuration/$assembly.dll"),
    (Join-Path $ProjectDir "manifest.yml"),
    (Join-Path $ProjectDir "icon.png")
)

foreach ($file in $payload) {
    if (-not (Test-Path $file)) {
        throw "缺少打包文件：$file（请先执行 dotnet build -c $Configuration）"
    }
    Copy-Item $file -Destination $stage -Force
}

if (Test-Path $outFile) { Remove-Item $outFile -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $outFile

Write-Host "已生成插件包：$outFile"
