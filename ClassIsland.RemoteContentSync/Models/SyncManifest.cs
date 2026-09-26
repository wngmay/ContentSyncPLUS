namespace ClassIsland.RemoteContentSync.Models;

/// <summary>
/// 远程同步清单 sync-plugins.json 的根对象。
/// </summary>
public sealed class SyncManifest
{
    /// <summary>清单版本号，每次改动清单必须 +1。</summary>
    public int Version { get; set; }

    /// <summary>需要同步的插件包列表。</summary>
    public List<PluginItem> Plugins { get; set; } = [];

    /// <summary>自动化规则同步项，可为 null（不同步规则）。</summary>
    public AutomationItem? Automation { get; set; }
}
