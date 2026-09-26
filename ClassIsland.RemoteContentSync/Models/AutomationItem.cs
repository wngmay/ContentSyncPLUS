namespace ClassIsland.RemoteContentSync.Models;

/// <summary>
/// 远程清单中的自动化规则同步项。
/// </summary>
public sealed class AutomationItem
{
    /// <summary>规则版本号，变化才触发下载。</summary>
    public int Version { get; set; }

    /// <summary>主下载地址。</summary>
    public string Url { get; set; } = "";

    /// <summary>可选的 "sha256:&lt;hex&gt;" 校验值。</summary>
    public string? Hash { get; set; }

    /// <summary>备用下载地址。</summary>
    public List<string> Mirrors { get; set; } = [];
}
