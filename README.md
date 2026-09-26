# ClassIsland 远程内容同步插件（RemoteContentSync）

为多间教室电脑自动分发 **ClassIsland 插件（.cipx）** 与 **自动化规则（Automations）**。

ClassIsland 自带的「静态集控」只能分发课表、时间表、科目等档案，无法分发插件和自动化规则；官方集控服务器又处于预览/停更状态。本插件补上这一缺口：教室电脑每次启动 ClassIsland 时自动拉取远程清单，准备好更新内容，并提示「将在下次重启时应用」。

---

## 1. 工作方式（重要）

插件 **不会** 在运行时热插拔插件，也不会自动重启 ClassIsland —— 这是刻意的产品决策：

| 环节 | 做法 |
|---|---|
| 拉取清单 | ClassIsland 启动完成 15 秒后执行；失败只记日志，绝不阻塞启动 |
| 有新插件 | 下载 `.cipx` → 校验 → 放进 `{AppRoot}/Cache/PluginPackages/` |
| 何时安装 | **主机下次启动时**，`PluginService.ProcessPluginsInstall()` 扫到该包并解压到 `{AppRoot}/Plugins/{id}/` |
| 有新规则 | 下载 JSON → 备份原文件为 `.bak` → 写入 `{AppRoot}/Config/Automations/{name}.json` → 调 `RefreshConfigs()` |
| 何时生效 | 同样 **下次重启**，因为 `RefreshConfigs()` 只刷新文件列表，不重挂正在运行的工作流 |
| 提示用户 | 有内容待应用时，弹顶部提醒「已下载远程 N 个插件更新 / 自动化规则，将在 ClassIsland 下次启动时应用」 |

---

## 2. 目录结构

```
ClassIsland.RemoteContentSync/
├── ClassIsland.RemoteContentSync.csproj   # net8.0 + ClassIsland.Core 2.1.0.1（ExcludeAssets=runtime）
├── manifest.yml                           # id: school.remote.content.sync，apiVersion: 2.0.0.0
├── icon.png
├── Plugin.cs                              # [PluginEntrance] 入口：注册服务 + AppStarted 钩子
├── Models/                                # 清单 / 配置 / 状态的数据模型
├── Services/
│   ├── RemoteSyncService.cs               # 主流程编排
│   ├── CipxInstallService.cs              # 下载 .cipx、校验、放入待安装目录
│   ├── AutomationSyncService.cs           # 下载规则、备份、写入 Config/Automations
│   ├── SettingsService.cs                 # 读写 settings.json / state.json
│   ├── RemoteSyncNotificationProvider.cs  # 顶部提醒
│   └── LogService.cs                      # 文件日志 + 保留天数清理
├── Utils/                                 # HttpHelper / HashHelper / VersionHelper / AtomicFile
├── packaging/                             # 打包脚本与说明
└── cipx/                                  # 构建产物（.cipx）
```

---

## 3. 编译与打包

环境要求：.NET SDK 10.x（可向下构建 net8.0）。NuGet 源已在仓库根目录 `nuget.config` 中固定为 `nuget.azure.cn` 镜像。

```powershell
cd ClassIsland.RemoteContentSync

# 仅编译
dotnet build -c Release

# 编译并打出 .cipx
dotnet build -c Release -p:CreateCipx=true
```

产物：`cipx/ClassIsland.RemoteContentSync-1.0.0.cipx`

包内只有三个文件，**不含** Avalonia 等依赖，避免与主机程序集冲突：

```
ClassIsland.RemoteContentSync.dll
manifest.yml
icon.png
```

也可以只打包不编译：`./packaging/build-cipx.ps1 -Configuration Release`

**目标框架为什么是 net8.0**：`ClassIsland.Core 2.1.0.x` 目标框架是 net8.0，`2.1.1.x` 是 net10.0。net8.0 程序集可跑在 .NET 10 主机上，因此选择 net8.0 能同时覆盖两个大版本。插件不把 `ClassIsland.Core.dll` 打进包，加载时 `PluginLoadContext.Load()` 返回 null 会回退到默认 ALC，自动使用主机已有的 Core，版本差异被抹平。

---

## 4. 部署（教室电脑首装）

ClassIsland 无法在没有本插件的情况下安装本插件，所以首装需要人工一次：

**方式 A（推荐）：放到待安装目录，重启本体**

1. 把 `ClassIsland.RemoteContentSync-1.0.0.cipx` 复制到 `{AppRoot}/Cache/PluginPackages/`
2. 重启 ClassIsland，本体会自动把包解压到 `{AppRoot}/Plugins/school.remote.content.sync/`
3. 再重启一次，插件才开始工作

**方式 B：直接解压**

把包解压到 `{AppRoot}/Plugins/school.remote.content.sync/`，重启 ClassIsland 一次即可。

> `{AppRoot}` 的位置取决于安装方式：
> - 便携版（本次实机验证的环境）：`D:\toolapp\ClassIsland_app_windows_x64_selfContained_folder\data`
> - 安装版：`%LOCALAPPDATA%\ClassIsland`（以实际为准）
>
> 判断方法：该目录下应当同时有 `Config/`、`Cache/`、`Plugins/`。

### 分发配置文件

插件首次启动会在 `{AppRoot}/Config/Plugins/school.remote.content.sync/settings.json` 生成默认配置。管理员可以先在一台机器上改好，再连同插件一起批量下发。

```json
{
  "SyncManifestUrl": "https://raw.githubusercontent.com/wngmay/ZhongXianMiddleSchool-ClassislandControl/main/sync-plugins.json",
  "SyncManifestMirrors": [
    "https://gcore.jsdelivr.net/gh/wngmay/ZhongXianMiddleSchool-ClassislandControl@main/sync-plugins.json"
  ],
  "SyncIntervalMinutes": 0,
  "AutoInstall": true,
  "AllowedPluginIds": [],
  "AutomationConfigName": "Default",
  "HashMismatchAction": "Abort",
  "LogRetentionDays": 14
}
```

| 字段 | 说明 |
|---|---|
| `SyncManifestUrl` | 远程清单地址，这是唯一的必配项 |
| `SyncManifestMirrors` | 备用地址，主源失败后依次尝试。**国内网络务必配 jsDelivr 镜像** |
| `SyncIntervalMinutes` | 定时同步间隔（分钟），`0` = 仅启动时同步 |
| `AutoInstall` | `false` 时只记录不下载（调试用） |
| `AllowedPluginIds` | 白名单，空数组 = 全部允许 |
| `AutomationConfigName` | 写入 `Config/Automations/{name}.json` |
| `HashMismatchAction` | `Abort`（默认）放弃本次更新；`Ignore` 仍然写入 |
| `LogRetentionDays` | 日志保留天数，默认 14 |

改完后重启 ClassIsland 生效。

---

## 5. 远程清单格式 `sync-plugins.json`

示例见仓库根目录 [`sync-plugins.example.json`](sync-plugins.example.json)。

```json
{
  "Version": 1,
  "Plugins": [
    {
      "Id": "example.plugin.clock",
      "Version": "1.2.0",
      "Url": "https://raw.githubusercontent.com/.../plugins/clock-1.2.0.cipx",
      "Hash": "sha256:00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff",
      "Mirrors": ["https://gcore.jsdelivr.net/gh/.../plugins/clock-1.2.0.cipx"]
    }
  ],
  "Automation": {
    "Version": 1,
    "Url": "https://.../automation/rules.json",
    "Hash": "sha256:...",
    "Mirrors": ["https://.../automation/rules.json"]
  }
}
```

- **每次改动清单必须 `Version + 1`**，否则客户端会直接跳过（不去逐个比对插件版本）
- `Hash` 可选，形如 `sha256:<hex>`；提供则强制校验，不一致按 `HashMismatchAction` 处理
- `Automation` 可省略，表示不同步规则
- `Mirrors` 为备用源，主失败后依次尝试

### 本地状态 `state.json`

存放在 `{AppRoot}/Config/Plugins/school.remote.content.sync/state.json`，一般不需要手工改动：

```json
{
  "LastManifestVersion": 1,
  "Plugins": { "example.plugin.clock": "1.2.0" },
  "AutomationVersion": 1,
  "PendingRestart": true
}
```

`PendingRestart` 用于启动时判断「上次遗留的待应用内容已在本次被主机处理」。

---

## 6. 日志与排障

日志在 `{AppRoot}/Config/Plugins/school.remote.content.sync/Logs/yyyy-MM-dd.log`，按 `LogRetentionDays` 自动清理。

| 现象 | 排查方向 |
|---|---|
| 完全没有同步迹象 | 看当天日志是否存在；日志文件都没生成说明插件未被加载，检查 `{AppRoot}/Plugins/school.remote.content.sync/` 下是否有 DLL 和 manifest.yml，以及 manifest.yml 的 `apiVersion` 是否 ≥ `2.0.0.0` |
| 日志显示「拉取远程同步清单失败」 | 主源和镜像都不通。换用国内可达的镜像，或直接在浏览器验证 URL |
| 有更新但重启后没装上 | 确认 `.cipx` 确实落在 `{AppRoot}/Cache/PluginPackages/`；启动后该目录应被清空（主机安装完会删包），若仍在说明包损坏或缺少 manifest.yml |
| 自动化规则没变 | 确认写入的是 `{AppRoot}/Config/Automations/Default.json`（注意是 **Automations 复数**），且 `Settings.CurrentAutomationConfig` 指向该文件 |
| 没弹顶部提醒 | 提醒被主机设置里该提供方关闭，或注册失败（日志会有「无法注册顶部提醒提供方」）。无论如何都不影响同步本身 |

---

## 7. 本地冒烟测试

`packaging/test-server/` 是一个可直接 `python -m http.server` 托管的静态目录，用于在没有真实远端仓库时验证完整链路：

```powershell
cd packaging/test-server
python -m http.server 18099 --bind 127.0.0.1
```

然后把测试机的 `settings.json` 临时改成：

```json
{ "SyncManifestUrl": "http://127.0.0.1:18099/sync-plugins.json", ... }
```

建议同时把 `AutomationConfigName` 改成 `RemoteSyncTest` 之类的独立名字，避免覆盖正在使用的 `Default.json`；测完删掉 `state.json` 与 `settings.json` 让插件重新生成默认值即可。

## 8. 已知限制

- 插件自身无法自我更新（本插件把自己装进 `Plugins/` 后，更新仍需人工替换包）
- 「必须重启才生效」是 ClassIsland 的既有机制，插件不改变这一点
- 清单版本不变时不重试；若某次插件下载失败，需要在服务端把清单 `Version + 1` 才会再次尝试
- `Automations` 的热刷新只刷新文件列表，正在运行的工作流要等下次启动

---

## 9. 许可证

本项目引用 LGPL-3.0-only 的 `ClassIsland.Core`，故整体同样遵循 LGPL-3.0-only。
