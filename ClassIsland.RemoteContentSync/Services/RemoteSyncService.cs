using System.Text.Json;
using ClassIsland.RemoteContentSync.Models;
using ClassIsland.RemoteContentSync.Utils;

namespace ClassIsland.RemoteContentSync.Services;

/// <summary>
/// 同步主流程编排。
/// </summary>
/// <remarks>
/// 原则：任何一步抛异常都只记日志并继续下一个条目，绝不向上抛出、绝不阻塞 ClassIsland 启动。
/// </remarks>
public sealed class RemoteSyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SettingsService _settingsService;
    private readonly CipxInstallService _cipxInstallService;
    private readonly AutomationSyncService _automationSyncService;
    private readonly CloudUploadService _cloudUploadService;
    private readonly LogService _log;

    private RemoteSyncNotificationProvider? _notificationProvider;
    private bool _notificationProviderResolved;

    public RemoteSyncService(string configFolder, LogService log)
    {
        _log = log;
        _settingsService = new SettingsService(configFolder);
        _cipxInstallService = new CipxInstallService(log);
        _automationSyncService = new AutomationSyncService(log);
        _cloudUploadService = new CloudUploadService(log);
    }

    /// <summary>最近一次实际使用的配置，供外部读取定时同步间隔。</summary>
    public SyncSettings? CurrentSettings { get; private set; }

    /// <summary>设置读写服务，供设置页直接读写 settings.json。</summary>
    public SettingsService SettingsService => _settingsService;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _log.Info("========== 开始远程内容同步 ==========");

        SyncSettings settings;
        SyncState state;
        try
        {
            settings = _settingsService.LoadSettings();
            _log.RetentionDays = settings.LogRetentionDays;
            CurrentSettings = settings;
            state = _settingsService.LoadState();
        }
        catch (Exception ex)
        {
            _log.Error("读取本地配置/状态失败，本次同步中止。", ex);
            return;
        }

        // 上次遗留的待应用内容已在本次启动由主机处理，这里重置标记
        if (state.PendingRestart)
        {
            _log.Info("检测到上次遗留的待应用内容，已在本次启动时由主机处理。");
            state.PendingRestart = false;
        }

        var manifest = await FetchManifestAsync(settings, cancellationToken).ConfigureAwait(false);
        if (manifest == null)
        {
            SaveState(state);
            return;
        }

        if (manifest.Version == state.LastManifestVersion)
        {
            _log.Info($"远程清单版本未变化（Version={manifest.Version}），无需同步。");
            SaveState(state);
            return;
        }

        var pendingRestart = false;
        var updatedPlugins = 0;
        var updatedAutomation = false;

        foreach (var item in manifest.Plugins ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!IsAllowed(item.Id, settings))
                {
                    _log.Info($"插件 {item.Id} 不在 AllowedPluginIds 白名单内，已跳过。");
                    continue;
                }

                if (!state.Plugins.TryGetValue(item.Id, out var localVersion))
                {
                    localVersion = _cipxInstallService.GetLocalVersion(item.Id);
                }

                if (!VersionHelper.IsRemoteNewer(localVersion, item.Version))
                {
                    _log.Info($"插件 {item.Id} 本地版本 {localVersion ?? "(未安装)"} 不低于远程版本 {item.Version}，跳过。");
                    continue;
                }

                if (!settings.AutoInstall)
                {
                    _log.Info($"AutoInstall=false，插件 {item.Id} 仅记录不下载。");
                    continue;
                }

                var ok = await _cipxInstallService
                    .PreparePackageAsync(item, settings.HashMismatchAction, cancellationToken)
                    .ConfigureAwait(false);
                if (!ok)
                {
                    continue;
                }

                state.Plugins[item.Id] = item.Version;
                pendingRestart = true;
                updatedPlugins++;
            }
            catch (Exception ex)
            {
                _log.Error($"处理插件 {item.Id} 时出错，已跳过该条目。", ex);
            }
        }

        var automation = manifest.Automation;
        if (automation != null)
        {
            try
            {
                if (automation.Version != state.AutomationVersion)
                {
                    updatedAutomation = await _automationSyncService
                        .SyncAsync(automation, settings.AutomationConfigName, settings.HashMismatchAction,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (updatedAutomation)
                    {
                        state.AutomationVersion = automation.Version;
                        pendingRestart = true;
                    }
                }
                else
                {
                    _log.Info($"自动化规则版本未变化（Version={automation.Version}），跳过。");
                }
            }
            catch (Exception ex)
            {
                _log.Error("同步自动化规则时出错，已跳过。", ex);
            }
        }

        state.LastManifestVersion = manifest.Version;
        state.PendingRestart = pendingRestart;
        SaveState(state);

        if (pendingRestart)
        {
            var message = BuildRemindMessage(updatedPlugins, updatedAutomation);
            _log.Info(message);
            Notify(message);
        }
        else
        {
            _log.Info("本次同步没有需要下次启动应用的内容。");
        }

        _log.Info("========== 远程内容同步结束 ==========");
    }

    /// <summary>
    /// 一键上传：把本机全部配置文件（Settings.json + Config 目录所有 JSON，含集控与各插件配置）
    /// 以单个提交上传到配置的 GitHub 仓库，并通过顶部提醒反馈结果。
    /// </summary>
    public async Task<CloudUploadService.UploadResult> UploadBackupAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.LoadSettings();
        var result = await _cloudUploadService.UploadAsync(settings, cancellationToken).ConfigureAwait(false);
        Notify(result.Message);
        return result;
    }

    /// <summary>
    /// 显示顶部提醒；注册失败时降级为仅写日志。
    /// </summary>
    /// <remarks>
    /// NotificationProviderBase 的构造函数会创建 Avalonia 控件（FluentIcon），
    /// 必须在 UI 线程执行，否则会抛 "Call from invalid thread"。
    /// </remarks>
    public void Notify(string text)
    {
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (!_notificationProviderResolved)
                    {
                        _notificationProviderResolved = true;
                        _notificationProvider = RemoteSyncNotificationProvider.TryCreate(_log);
                    }

                    _notificationProvider?.Notify(text);
                }
                catch (Exception ex)
                {
                    _log.Error("显示顶部提醒失败。", ex);
                }
            });
        }
        catch (Exception ex)
        {
            _log.Error("投递顶部提醒到 UI 线程失败。", ex);
        }
    }

    private static string BuildRemindMessage(int updatedPlugins, bool updatedAutomation)
    {
        var parts = new List<string>();
        if (updatedPlugins > 0)
        {
            parts.Add($"{updatedPlugins} 个插件更新");
        }

        if (updatedAutomation)
        {
            parts.Add("自动化规则");
        }
        
        var content = string.Join(" 与 ", parts);
        return $"远程{content}已准备好，将在 ClassIsland 下次启动时应用。";
    }

    private static bool IsAllowed(string pluginId, SyncSettings settings)
    {
        return settings.AllowedPluginIds.Count == 0 ||
               settings.AllowedPluginIds.Contains(pluginId, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<SyncManifest?> FetchManifestAsync(SyncSettings settings, CancellationToken cancellationToken)
    {
        var urls = HttpHelper.BuildUrlChain(settings.SyncManifestUrl, settings.SyncManifestMirrors).ToList();
        if (urls.Count == 0)
        {
            _log.Error("SyncManifestUrl 为空且没有镜像地址，无法同步。");
            return null;
        }

        string text;
        try
        {
            text = await HttpHelper.GetStringAsync(urls, m => _log.Warn(m), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error("拉取远程同步清单失败（全部分源均不可用），本次同步结束，不影响 ClassIsland 正常使用。", ex);
            return null;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<SyncManifest>(text, JsonOptions);
            if (manifest == null)
            {
                _log.Error("远程同步清单反序列化结果为空。");
                return null;
            }

            return manifest;
        }
        catch (Exception ex)
        {
            _log.Error("远程同步清单解析失败，本次同步结束。", ex);
            return null;
        }
    }

    private void SaveState(SyncState state)
    {
        try
        {
            _settingsService.SaveState(state);
        }
        catch (Exception ex)
        {
            _log.Error("写入 state.json 失败。", ex);
        }
    }
}
