namespace ClassIsland.RemoteContentSync.Models;

/// <summary>
/// 哈希校验失败时的处理方式。
/// </summary>
public enum HashMismatchAction
{
    /// <summary>放弃本次更新。</summary>
    Abort,

    /// <summary>忽略校验失败，仍然放入待安装目录。</summary>
    Ignore
}

/// <summary>
/// 插件配置 settings.json，存放在插件配置目录，首次启动自动生成，管理员可直接改文件分发。
/// </summary>
public sealed class SyncSettings
{
    public string SyncManifestUrl { get; set; } =
        "https://raw.githubusercontent.com/wngmay/ZhongXianMiddleSchool-ClassislandControl/main/sync-plugins.json";

    public List<string> SyncManifestMirrors { get; set; } =
    [
        "https://gcore.jsdelivr.net/gh/wngmay/ZhongXianMiddleSchool-ClassislandControl@main/sync-plugins.json"
    ];

    /// <summary>定时同步间隔（分钟）。0 表示仅启动时同步。</summary>
    public int SyncIntervalMinutes { get; set; }

    /// <summary>是否自动把下载到的 .cipx 放入待安装目录。</summary>
    public bool AutoInstall { get; set; } = true;

    /// <summary>插件白名单，为空表示允许全部。</summary>
    public List<string> AllowedPluginIds { get; set; } = [];

    /// <summary>自动化配置文件名（不含扩展名），写入 Config/Automations/{name}.json。</summary>
    public string AutomationConfigName { get; set; } = "Default";

    public HashMismatchAction HashMismatchAction { get; set; } = HashMismatchAction.Abort;

    public int LogRetentionDays { get; set; } = 14;

    /// <summary>
    /// GitHub 个人访问令牌（需要目标仓库的写入权限），用于一键上传配置备份。
    /// 存储为 DPAPI 加密密文（SecretProtector.Protect/Unprotect），绝不落盘明文。
    /// </summary>
    public string GitHubToken { get; set; } = "";

    /// <summary>目标仓库，形如 owner/repo。</summary>
    public string GitHubRepo { get; set; } = "";

    /// <summary>目标分支，不存在时会自动创建。</summary>
    public string GitHubBranch { get; set; } = "main";

    /// <summary>仓库内上传目录，可含 {Machine} 占位符（替换为本机名）。</summary>
    public string GitHubUploadPath { get; set; } = "backups/{Machine}";

    /// <summary>GitHub API 基地址，默认官方；不可直连时可换成加速代理。</summary>
    public string GitHubApiBase { get; set; } = "https://api.github.com";

    /// <summary>补齐缺失字段的默认值，防止管理员手改配置时漏项导致空引用。</summary>
    public void Normalize()
    {
        SyncManifestUrl ??= "";
        SyncManifestMirrors ??= [];
        AllowedPluginIds ??= [];
        AutomationConfigName = string.IsNullOrWhiteSpace(AutomationConfigName) ? "Default" : AutomationConfigName.Trim();
        if (SyncIntervalMinutes < 0) SyncIntervalMinutes = 0;
        if (LogRetentionDays < 1) LogRetentionDays = 1;
        GitHubToken ??= "";
        GitHubRepo ??= "";
        GitHubBranch = string.IsNullOrWhiteSpace(GitHubBranch) ? "main" : GitHubBranch.Trim();
        GitHubUploadPath = string.IsNullOrWhiteSpace(GitHubUploadPath) ? "backups/{Machine}" : GitHubUploadPath.Trim();
        GitHubApiBase = string.IsNullOrWhiteSpace(GitHubApiBase) ? "https://api.github.com" : GitHubApiBase.Trim().TrimEnd('/');
    }
}
