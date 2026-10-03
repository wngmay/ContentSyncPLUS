using ClassIsland.Core;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.RemoteContentSync.Services;
using ClassIsland.RemoteContentSync.Views.SettingsPages;
using ClassIsland.Shared;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClassIsland.RemoteContentSync;

/// <summary>
/// 远程内容同步插件入口。
/// </summary>
[PluginEntrance]
public class Plugin : PluginBase
{
    private const string DefaultPluginId = "school.remote.content.sync";

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private Timer? _timer;
    private int _isRunning;

    private LogService? _log;
    private RemoteSyncService? _syncService;

    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        var configFolder = PluginConfigFolder;
        if (string.IsNullOrWhiteSpace(configFolder))
        {
            configFolder = Path.Combine(CommonDirectories.AppConfigPath, "Plugins", DefaultPluginId);
        }

        Directory.CreateDirectory(configFolder);

        _log = new LogService(Path.Combine(configFolder, "Logs"));
        _syncService = new RemoteSyncService(configFolder, _log);
        services.AddSingleton(_syncService);

        // 注册设置页，让所有配置项都能在 ClassIsland 设置窗口可视化编辑，
        // 并提供「立即同步」「预览提醒」两个测试入口。
        services.AddSettingsPage<RemoteSyncSettingsPage>();

        // 主机完成启动后再开始同步，避免拖慢启动、也确保 IAppHost 已可用
        AppBase.Current.AppStarted += OnAppStarted;
    }

    private void OnAppStarted(object? sender, EventArgs e)
    {
        RegisterTrayMenu();

        _ = Task.Run(async () =>
        {
            // 启动后稍等，把网络 IO 让开给本体的其他初始化
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), _cancellationTokenSource.Token).ConfigureAwait(false);
            }
            catch
            {
                // 取消时直接退出
                return;
            }

            await RunOnceAsync().ConfigureAwait(false);
            ScheduleTimer();
        });
    }

    /// <summary>
    /// 在主托盘图标的「更多选项」菜单中注册「一键上传配置到云端」入口。
    /// </summary>
    private void RegisterTrayMenu()
    {
        try
        {
            var tray = IAppHost.TryGetService<ITaskBarIconService>();
            var sync = _syncService;
            if (tray == null || sync == null)
            {
                _log?.Warn("未获取到托盘图标服务，云端上传入口不可用（仍可在设置页触发）。");
                return;
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var item = new NativeMenuItem
                    {
                        Header = "一键上传配置到云端",
                        ToolTip = "把本机 ClassIsland 全部配置上传到 GitHub 仓库备份",
                        Command = new SimpleCommand(() => sync.UploadBackupAsync())
                    };
                    tray.MoreOptionsMenuItems.Add(item);
                    _log?.Info("已注册托盘菜单：一键上传配置到云端。");
                }
                catch (Exception ex)
                {
                    _log?.Error("注册托盘菜单项失败。", ex);
                }
            });
        }
        catch (Exception ex)
        {
            _log?.Error("获取托盘图标服务失败。", ex);
        }
    }

    private void ScheduleTimer()
    {
        var minutes = _syncService?.CurrentSettings?.SyncIntervalMinutes ?? 0;
        if (minutes <= 0)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(minutes);
        _timer?.Dispose();
        _timer = new Timer(_ => _ = RunOnceAsync(), null, interval, interval);
    }

    private async Task RunOnceAsync()
    {
        if (_syncService == null)
        {
            return;
        }

        if (Interlocked.Exchange(ref _isRunning, 1) == 1)
        {
            return;
        }

        try
        {
            await _syncService.RunAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 兜底：同步逻辑自身已逐条捕获，这里只防止意外异常影响宿主
            _log?.Error("同步过程出现未处理异常。", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, 0);
        }
    }
}
