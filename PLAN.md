# ClassIsland 远程内容同步插件（RemoteContentSync）实施计划

> 本文档由 `.md` 需求文档 + 对 ClassIsland 2.1.0.1 / 2.1.1.1 源码的实际调研整合而成。
> 所有标注「已核实」的结论都来自官方仓库源码，可据此直接编码。

---

## 0. 已确认的决策（用户 2026-09-18 确认）

| 决策项 | 结论 |
|---|---|
| 编译目标 | `net8.0` + `ClassIsland.Core 2.1.0.1`（同时兼容 2.1.0.x 与 2.1.1.x 主机） |
| 构建环境 | 本机安装 .NET SDK，实际编译验证通过后再交付 |
| 插件安装时机 | **不自动重启**。放包到待安装目录后，弹顶部提醒告知「将在下次重启时应用」 |
| 自动化规则生效 | **写文件 + `RefreshConfigs()`，同样只提醒下次重启生效**，不做内存热替换 |
| 执行时间 | 用户次日回来后再开始编码 |

---

## 1. 关键调研结论（已核实，编码直接依据）

### 1.1 版本与目标框架的陷阱（最重要）

| ClassIsland.Core 版本 | 目标框架 |
|---|---|
| 2.0.4 / 2.1.0 / **2.1.0.1** | **net8.0** |
| **2.1.1 / 2.1.1.1** | **net10.0** |

- 插件必须按主机的运行时选择目标框架，否则 `PluginLoadContext` 无法加载。
- `net8.0` 程序集可以跑在 .NET 10 主机上（向前兼容），所以 **选 net8.0 可同时覆盖 2.1.0.x 和 2.1.1.x**。
- 插件**不要**把 `ClassIsland.Core.dll` 打进包里：`<ExcludeAssets>runtime</ExcludeAssets>`。加载时 `PluginLoadContext.Load()` 返回 null → 回退到默认 ALC → 使用主机已加载的 Core，版本差异被自动抹平。

### 1.2 `.cipx` 安装机制（回答需求文档 13 的「待确认问题 1」）

已核实 `ClassIsland/Services/PluginService.cs`（2.1.0.1 与 2.1.1.1 完全一致）：

```csharp
public static readonly string PluginsRootPath      = Path.Combine(CommonDirectories.AppRootFolderPath, "Plugins");
public static readonly string PluginsPkgRootPath   = Path.Combine(CommonDirectories.AppCacheFolderPath, "PluginPackages");
public static readonly string PluginConfigsFolderPath = Path.Combine(CommonDirectories.AppConfigPath, "Plugins");
public static readonly string PluginManifestFileName = "manifest.yml";

public static void ProcessPluginsInstall()
{
    foreach (var pkgPath in Directory.EnumerateFiles(PluginsPkgRootPath)
                 .Where(x => Path.GetExtension(x) == IPluginService.PluginPackageExtension))  // ".cipx"
    {
        using var pkg = ZipFile.OpenRead(pkgPath);
        var mf = pkg.GetEntry("manifest.yml");            // 包内必须有 manifest.yml
        var manifest = 反序列化 YAML(mf);                  // YamlDotNet + camelCase
        var targetPath = Path.Combine(PluginsRootPath, manifest.Id);
        if (Directory.Exists(targetPath)) Directory.Delete(targetPath, true);   // 覆盖安装
        ZipFile.ExtractToDirectory(pkgPath, targetPath);
        File.Delete(pkgPath);                              // 安装后删除包
    }
}
```

**结论**：
- 待安装目录 = `{AppRoot}/Cache/PluginPackages`，文件名必须是 `*.cipx`。
- 扩展名常量用 `IPluginService.PluginPackageExtension`（值为 `".cipx"`）。
- 包内必须含 `manifest.yml`（YAML，camelCase，至少要有 `id`）。
- 已安装插件目录 = `{AppRoot}/Plugins/{插件Id}/`，其下也有 `manifest.yml`，可用于读取本地已装版本。
- `ProcessPluginsInstall()` 在 `App.axaml.cs` 第 690 行被调用，**在构建 Host 之前**，所以放包后必须重启才生效 —— 与用户决策一致。

### 1.3 自动化规则（回答「待确认问题 2」，并修正了需求文档的说法）

已核实 `ClassIsland/Services/AutomationService.cs`：

```csharp
public static readonly string AutomationConfigsFolderPath =
    Path.Combine(CommonDirectories.AppConfigPath, "Automations");   // 注意是复数 Automations

public string CurrentConfigPath => Path.Combine(AutomationConfigsFolderPath,
    SettingsService.Settings.CurrentAutomationConfig + ".json");    // 当前使用的配置文件

public void RefreshConfigs()
{
    Configs = Directory.GetFiles(AutomationConfigsFolderPath, "*.json")
        .Select(x => Path.GetFileNameWithoutExtension(x)).ToList();
}
```

**结论与差异**：
- 目录是 **`Config/Automations`**（复数），不是需求文档里写的 `Automation/`。
- 目录下的每个 `.json` 是一个「自动化配置文件」，文件内容是 `ObservableCollection<Workflow>`（触发器 + 规则集 + 行动组）。
- 实际生效的文件由 `Settings.CurrentAutomationConfig` 决定（默认一般是 `Default`，即 `Automations/Default.json`）。
- `IAutomationService` **没有**暴露当前配置名，`Core` 中也不存在 `ISettingsService` 接口 → 插件无法直接知道当前配置名。
  → 因此采用「写文件 + `RefreshConfigs()` + 提醒重启」，配置文件名做成可配置项（默认 `Default`）。
- `RefreshConfigs()` 只刷新「文件列表」，不会重新挂载正在运行的工作流 → 真正生效要重启，与用户决策一致。

### 1.4 插件生命周期与常用 API

| 用途 | API | 出处 |
|---|---|---|
| 插件入口 | `[PluginEntrance] public class Plugin : PluginBase`，重写 `Initialize(HostBuilderContext, IServiceCollection)` | `ClassIsland.Core/Abstractions/PluginBase.cs` |
| 启动后钩子 | `AppBase.Current.AppStarted += (_, _) => {}` | 官方插件模板 `Plugin.cs` 即如此 |
| 插件配置目录 | `PluginBase.PluginConfigFolder`（= `{AppRoot}/Config/Plugins/{插件Id}`） | `PluginService.cs` 第 207 行赋值 |
| 取服务 | `IAppHost.TryGetService<T>()` / `IAppHost.GetService<T>()`（`ClassIsland.Shared` 命名空间） | `ClassIsland.Shared/IAppHost.cs` |
| 后台任务 | `IAppHost.Host.StartAsync()` 在 `App.axaml.cs` 第 832 行被调用 → `IHostedService` 可用 | `App.axaml.cs` |
| 应用重启（备用） | `AppBase.Current.Restart(bool quiet)` | `ClassIsland.Core/AppBase.cs` |
| 日志 | `IAppLogService`（`ClassIsland.Core.Abstractions.Services.Logging`） | 可用；本插件同时自己写文件日志 |

`CommonDirectories`（`ClassIsland.Core`）：
- `AppRootFolderPath` 应用数据根目录；`AppConfigPath` = `{AppRoot}/Config`；`AppCacheFolderPath` = `{AppRoot}/Cache`

### 1.5 顶部提醒（用户要求的「提醒将在下次重启时应用」）

`INotificationHostService.ShowNotification(...)` 是 **internal**，插件不能直接调用。官方途径：

1. 定义 `class RemoteSyncNotificationProvider : NotificationProviderBase`
2. 类上加特性 `[NotificationProviderInfo("<固定GUID>", "远程同步", "\uEXXX", "远程内容同步提醒")]`
   （`ClassIsland.Core.Attributes.NotificationProviderInfo`，公开构造函数 `(string guid, string name, string iconGlyph, string description = "")`）
3. 在 App 启动后实例化，`NotificationProviderBase` 构造函数会自动 `RegisterNotificationProvider(this)`（它在 `NotificationProviderRegistryService.RegisteredProviders` 中按 `ProviderType` 查找信息，找不到会抛异常 → 实例化时机必须在主机完成扫描之后）
4. 调用 `ShowNotification(new NotificationRequest { MaskContent = NotificationContent.CreateSimpleTextContent("...") })`

> **实现期需要验证的点**：`RegisteredProviders` 由主机在插件 `Initialize` 期间扫描程序集填充。计划先做，实例化放在 `AppStarted` 之后；若抛「没有找到与 X 对应的提醒提供方」，回退方案为：写入 `IAppLogService` + 在插件设置页显示待应用状态。

### 1.6 其它核实过的事实

- 插件加载要求 `manifest.yml` 的 `apiVersion >= 2.0.0.0`（否则 `PluginLoadStatus.Error`）。
- 插件卸载标记：在插件目录放 `.uninstall` 文件；禁用标记：`.disabled`。
- `IPluginService.LoadedPlugins`（`IReadOnlyList<PluginInfo>`）可取当前已加载插件的 `Manifest.Id` / `Manifest.Version`。

---

## 2. 协议设计

### 2.1 远程清单 `sync-plugins.json`

沿用需求文档 5.1 的设计，不变：

```json
{
  "Version": 1,
  "Plugins": [
    {
      "Id": "example.plugin.clock",
      "Version": "1.2.0",
      "Url": "https://raw.githubusercontent.com/wngmay/ZhongXianMiddleSchool-ClassislandControl/main/plugins/clock-1.2.0.cipx",
      "Hash": "sha256:0011...ff",
      "Mirrors": ["https://gcore.jsdelivr.net/gh/wngmay/...@main/plugins/clock-1.2.0.cipx"]
    }
  ],
  "Automation": {
    "Version": 1,
    "Url": "https://.../automation/rules.json",
    "Hash": "sha256:ffff...",
    "Mirrors": ["https://.../automation/rules.json"]
  }
}
```

- 每次改动清单必须 `Version + 1`
- `Hash` 可选，形如 `sha256:<hex>`；提供则强制校验，不一致直接放弃并记日志
- `Mirrors` 为备用源，主源失败后依次尝试（国内网络建议放 jsDelivr）

### 2.2 本地状态 `state.json`

存放在插件配置目录 `{AppRoot}/Config/Plugins/{插件Id}/state.json`（由 `PluginBase.PluginConfigFolder` 得到，不再另建 `RemoteSync/` 目录，避免多写一处）：

```json
{
  "LastManifestVersion": 1,
  "Plugins": { "example.plugin.clock": "1.2.0" },
  "AutomationVersion": 1,
  "PendingRestart": true
}
```

新增 `PendingRestart`：用于启动时若上次有残留待应用内容，可再次提醒。

### 2.3 插件配置 `settings.json`

同样放插件配置目录，**首次启动自动生成默认文件**，便于管理员直接改文件分发：

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

- `SyncIntervalMinutes`：`0` = 仅启动时同步（默认）
- `AllowedPluginIds`：白名单，空数组 = 全部允许
- `AutomationConfigName`：写入 `Config/Automations/{name}.json`

---

## 3. 代码结构

```
ClassIsland.RemoteContentSync/
├── ClassIsland.RemoteContentSync.csproj   # net8.0，ClassIsland.Core 2.1.0.1（ExcludeAssets=runtime）
├── manifest.yml                           # id: school.remote.content.sync，apiVersion: 2.0.0.0
├── icon.png
├── README.md
├── Plugin.cs                              # [PluginEntrance] 入口：注册服务 + AppStarted 钩子
├── Models/
│   ├── SyncManifest.cs                    # sync-plugins.json
│   ├── PluginItem.cs
│   ├── AutomationItem.cs
│   ├── SyncSettings.cs                    # settings.json
│   └── SyncState.cs                       # state.json
├── Services/
│   ├── RemoteSyncService.cs               # 主流程编排
│   ├── CipxInstallService.cs              # 下载 .cipx → 校验 → 放入 Cache/PluginPackages
│   ├── AutomationSyncService.cs           # 下载 rules.json → 写入 Config/Automations
│   ├── SettingsService.cs                 # 读写 settings.json / state.json
│   ├── RemoteSyncNotificationProvider.cs  # 顶部提醒
│   └── LogService.cs                      # 文件日志 + 保留天数清理
├── Utils/
│   ├── HttpHelper.cs                      # 主源+镜像依次尝试，临时文件下载
│   ├── HashHelper.cs                      # SHA256 校验
│   └── VersionHelper.cs                   # Version 比较（TryParse，失败降级为字符串不等即更新）
└── packaging/
    └── build-cipx.md / .ps1               # 打包脚本（zip 输出目录为 .cipx）
```

构建产物打包：csproj 内置 `AfterTargets="Build"` 的 `ZipDirectory` 目标，把 `$(OutputPath)` 打成 `cipx/{项目名}.cipx`（对齐官方 `ClassIsland.PluginSdk` 的做法），条件为 `-p:CreateCipx=true`，避免每次调试都打包。

---

## 4. 主流程

```
[AppStarted]
  └─ 读 settings.json（不存在则写默认值）
  └─ 读 state.json（不存在则空）
  └─ 拉 sync-plugins.json（主源 → Mirrors；全失败 → 记日志并静默返回，绝不影响启动）
  └─ 若 manifest.Version 与 state 相同 → 跳过
  └─ 遍历 Plugins[]：
        白名单过滤（AllowedPluginIds 非空时）
        本地版本判定：state 优先；state 缺失则读 {AppRoot}/Plugins/{id}/manifest.yml 的 version
        远程版本 > 本地版本（或本地缺失）→ 下载到临时文件
            → 校验 SHA256（有 Hash 时；不符则删除并按 HashMismatchAction 处理）
            → 校验是合法 zip 且含 manifest.yml
            → 原子移动到 {AppRoot}/Cache/PluginPackages/{id}-{version}.cipx
            → 记 pendingRestart = true
  └─ Automation：版本变化 → 下载 rules.json
            → 校验可解析为 JSON
            → 备份现有 {name}.json 为 .bak
            → 原子写入 Config/Automations/{name}.json
            → IAutomationService.RefreshConfigs()
            → 记 pendingRestart = true
  └─ 写回 state.json
  └─ 若 pendingRestart → 顶部提醒「已下载 N 个插件更新 / 自动化规则，将在 ClassIsland 下次启动时应用」
  └─ 写日志
  └─ 若 SyncIntervalMinutes > 0 → 启动定时器
```

失败处理原则：**任何一步抛异常都只记日志并继续下一个条目，绝不向上抛出、绝不阻塞启动**。

---

## 5. 实施步骤

1. **装 .NET SDK**：本机只有 runtime，装 SDK（10.x，可向下构建 net8.0）。NuGet 走 `nuget.azure.cn` 镜像（api.nuget.org 在本机会 302 跳到该镜像且常超时）。
2. 建 `net8.0` 类库项目，引用 `ClassIsland.Core 2.1.0.1`（`ExcludeAssets=runtime`），写 `manifest.yml`、`icon.png`。
3. 模型层 + `Utils`（HTTP 带镜像切换、SHA256、版本比较）。
4. `SettingsService` / `LogService`。
5. `CipxInstallService`、`AutomationSyncService`。
6. `RemoteSyncService` 编排 + `Plugin.cs` 接入 `AppStarted`。
7. `RemoteSyncNotificationProvider` 顶部提醒（含回退方案）。
8. 打包目标 + 打包脚本。
9. 编译验证：`dotnet build -c Release -p:CreateCipx=true`，产出 `.cipx`。
10. 交付物文档：`README.md`（部署与排障）、`sync-plugins.json` 示例 + 测试用 `.cipx` 说明。

---

## 6. 验证方式

- **编译**：`dotnet build` 零警告零错误，产出 `.cipx`。
- **单测/手动**：清单反序列化、版本比较、Hash 校验、镜像切换（可做成小控制台自测，或人工造数据验证）。
- **实机**：
  1. 本机装 ClassIsland（当前机器上已有 2.1.0.1 的数据痕迹，需确认安装位置）
  2. 首装本插件：把 `.cipx` 放进 `Cache/PluginPackages` 重启，或直接解压到 `{AppRoot}/Plugins/{id}/`
  3. 造一个测试 `sync-plugins.json` + 测试插件包，确认下次启动后出现顶部提醒、重启后插件被装上
  4. 断网启动，确认 ClassIsland 正常、插件不报错
  5. 自动化规则：确认 `Config/Automations/Default.json` 被更新且 `.bak` 生成，重启后规则生效

---

## 7. 环境备忘（本机）

- 无 .NET SDK，只有 runtime 6/8/9/10。
- `github.com` 与 `raw.githubusercontent.com` **直连不通**；可用 `https://gcore.jsdelivr.net/gh/{owner}/{repo}@{ref}/{path}` 取源码。
- NuGet：`api.nuget.org` 会跳 `nuget.azure.cn`，用后者直连更快。
- PowerShell 的 `Add-Type` 被安全策略拦截，读取程序集元数据需另想办法（已装 .NET SDK 后可用小工具反射）。
