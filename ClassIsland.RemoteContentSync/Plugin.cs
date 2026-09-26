using ClassIsland.Core;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.RemoteContentSync.Services;
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

        // 主机完成启动后再开始同步，避免拖慢启动、也确保 IAppHost 已可用
        AppBase.Current.AppStarted += OnAppStarted;
    }

    private void OnAppStarted(object? sender, EventArgs e)
    {
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
