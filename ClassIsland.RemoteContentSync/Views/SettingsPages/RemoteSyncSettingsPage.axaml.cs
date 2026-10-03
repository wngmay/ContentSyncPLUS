using Avalonia.Interactivity;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.RemoteContentSync.Models;
using ClassIsland.RemoteContentSync.Services;
using ClassIsland.RemoteContentSync.Utils;
using ClassIsland.Shared;

namespace ClassIsland.RemoteContentSync.Views.SettingsPages;

/// <summary>
/// 远程内容同步设置页：可视化配置 settings.json，并提供「立即同步」「预览提醒」「一键上传配置到云端」入口。
/// </summary>
/// <remarks>
/// 必须保留 public 无参构造函数：Avalonia 运行时 XAML 加载器要求
/// （否则编译警告 AVLN3001，运行时加载设置页会失败、设置窗口中不显示）。
/// 依赖通过 IAppHost 服务定位获取。
/// </remarks>
[SettingsPageInfo("school.remote.content.sync.settings", "远程内容同步")]
public partial class RemoteSyncSettingsPage : SettingsPageBase
{
    private readonly RemoteSyncService _syncService;
    private bool _uploading;

    public RemoteSyncSettingsPage()
    {
        _syncService = IAppHost.TryGetService<RemoteSyncService>()
            ?? throw new InvalidOperationException("远程内容同步服务尚未就绪，无法打开设置页。");
        InitializeComponent();
        LoadSettingsIntoView();
    }

    /// <summary>把当前 settings.json 的内容填充到控件。</summary>
    private void LoadSettingsIntoView()
    {
        var s = _syncService.SettingsService.LoadSettings();
        ManifestUrlBox.Text = s.SyncManifestUrl;
        MirrorsBox.Text = string.Join(Environment.NewLine, s.SyncManifestMirrors);
        IntervalBox.Value = s.SyncIntervalMinutes;
        AutoInstallSwitch.IsChecked = s.AutoInstall;
        AllowedIdsBox.Text = string.Join(Environment.NewLine, s.AllowedPluginIds);
        AutomationConfigNameBox.Text = s.AutomationConfigName;
        MismatchActionBox.SelectedIndex = s.HashMismatchAction == HashMismatchAction.Ignore ? 1 : 0;
        LogRetentionBox.Value = s.LogRetentionDays;

        GitHubTokenBox.Text = SecretProtector.Unprotect(s.GitHubToken);
        GitHubRepoBox.Text = s.GitHubRepo;
        GitHubBranchBox.Text = s.GitHubBranch;
        GitHubUploadPathBox.Text = s.GitHubUploadPath;
        GitHubApiBaseBox.Text = s.GitHubApiBase;
    }

    /// <summary>从控件读取值并写回 settings.json。</summary>
    private void SaveSettings()
    {
        var s = _syncService.SettingsService.LoadSettings();
        s.SyncManifestUrl = ManifestUrlBox.Text?.Trim() ?? "";
        s.SyncManifestMirrors = SplitLines(MirrorsBox.Text);
        s.SyncIntervalMinutes = (int)(IntervalBox.Value ?? 0);
        s.AutoInstall = AutoInstallSwitch.IsChecked ?? true;
        s.AllowedPluginIds = SplitLines(AllowedIdsBox.Text);
        s.AutomationConfigName = string.IsNullOrWhiteSpace(AutomationConfigNameBox.Text)
            ? "Default"
            : AutomationConfigNameBox.Text.Trim();
        s.HashMismatchAction = MismatchActionBox.SelectedIndex == 1
            ? HashMismatchAction.Ignore
            : HashMismatchAction.Abort;
        s.LogRetentionDays = (int)(LogRetentionBox.Value ?? 14);

        s.GitHubToken = SecretProtector.Protect(GitHubTokenBox.Text?.Trim());
        s.GitHubRepo = GitHubRepoBox.Text?.Trim() ?? "";
        s.GitHubBranch = string.IsNullOrWhiteSpace(GitHubBranchBox.Text) ? "main" : GitHubBranchBox.Text.Trim();
        s.GitHubUploadPath = string.IsNullOrWhiteSpace(GitHubUploadPathBox.Text)
            ? "backups/{Machine}"
            : GitHubUploadPathBox.Text.Trim();
        s.GitHubApiBase = string.IsNullOrWhiteSpace(GitHubApiBaseBox.Text)
            ? "https://api.github.com"
            : GitHubApiBaseBox.Text.Trim();

        _syncService.SettingsService.SaveSettings(s);
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        SaveSettings();
        _syncService.Notify("远程内容同步设置已保存。");
    }

    private async void OnSyncNowClick(object? sender, RoutedEventArgs e)
    {
        SaveSettings();
        await _syncService.RunAsync();
    }

    private void OnPreviewClick(object? sender, RoutedEventArgs e)
    {
        _syncService.Notify("【预览】已准备好远程更新内容，将在 ClassIsland 下次启动时应用。");
    }

    private async void OnUploadClick(object? sender, RoutedEventArgs e)
    {
        if (_uploading)
        {
            return;
        }

        _uploading = true;
        UploadButton.IsEnabled = false;
        UploadButton.Content = "正在上传…";
        try
        {
            SaveSettings();
            await _syncService.UploadBackupAsync();
        }
        finally
        {
            _uploading = false;
            UploadButton.IsEnabled = true;
            UploadButton.Content = "上传配置";
        }
    }

    private static List<string> SplitLines(string? text)
    {
        return (text ?? "")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();
    }
}
