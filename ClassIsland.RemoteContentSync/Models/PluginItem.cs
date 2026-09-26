namespace ClassIsland.RemoteContentSync.Models;

/// <summary>
/// 远程清单中的一个插件包条目。
/// </summary>
public sealed class PluginItem
{
    /// <summary>插件唯一标识，与 cipx 内 manifest.yml 的 id 一致。</summary>
    public string Id { get; set; } = "";

    /// <summary>远程版本号。</summary>
    public string Version { get; set; } = "";

    /// <summary>主下载地址。</summary>
    public string Url { get; set; } = "";

    /// <summary>可选的 "sha256:&lt;hex&gt;" 校验值。</summary>
    public string? Hash { get; set; }

    /// <summary>备用下载地址，主源失败后依次尝试。</summary>
    public List<string> Mirrors { get; set; } = [];
}
