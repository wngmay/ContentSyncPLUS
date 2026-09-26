namespace ClassIsland.RemoteContentSync.Models;

/// <summary>
/// 本地同步状态 state.json，用于判断「是否已经处理过某个版本」。
/// </summary>
public sealed class SyncState
{
    /// <summary>上次成功处理的清单版本号。</summary>
    public int LastManifestVersion { get; set; }

    /// <summary>已处理的插件版本表：插件 Id -> 版本。</summary>
    public Dictionary<string, string> Plugins { get; set; } = [];

    /// <summary>已处理的自动化规则版本号。</summary>
    public int AutomationVersion { get; set; }

    /// <summary>是否存在等待下次启动生效的内容。</summary>
    public bool PendingRestart { get; set; }
}
