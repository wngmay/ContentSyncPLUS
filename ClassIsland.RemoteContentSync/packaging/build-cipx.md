# 打包 .cipx

`.cipx` 本质是一个 zip 包，包根必须包含 `manifest.yml`（主机的 `PluginService.ProcessPluginsInstall()`
会读取包内 `manifest.yml` 的 `id`，把整包解压到 `{AppRoot}/Plugins/{id}/`）。

## 方式一：MSBuild 目标（推荐）

csproj 内置了 `CreateCipx` 目标，只会把「本插件 DLL + manifest.yml + icon.png」打进包：

```powershell
dotnet build -c Release -p:CreateCipx=true
```

产物：`cipx/ClassIsland.RemoteContentSync-1.0.0.cipx`

## 方式二：独立脚本

```powershell
# 先正常构建
dotnet build -c Release

# 再打包（脚本会自行收集 bin/Release 下的插件 DLL 与清单文件）
./packaging/build-cipx.ps1 -Configuration Release -Version 1.0.0
```

产物同样输出到 `cipx/`。

## 手动打包

```powershell
Compress-Archive -Path bin/Release/ClassIsland.RemoteContentSync.dll, manifest.yml, icon.png `
  -DestinationPath cipx/ClassIsland.RemoteContentSync-1.0.0.cipx -Force
```

## 包内容检查

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead('cipx/ClassIsland.RemoteContentSync-1.0.0.cipx')
$zip.Entries | Select-Object FullName, Length
$zip.Dispose()
```

必须能看到：

```
manifest.yml
icon.png
ClassIsland.RemoteContentSync.dll
```
