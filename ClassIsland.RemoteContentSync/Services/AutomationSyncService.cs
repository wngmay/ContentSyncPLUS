using System.Text.Json;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.RemoteContentSync.Models;
using ClassIsland.RemoteContentSync.Utils;
using ClassIsland.Shared;

namespace ClassIsland.RemoteContentSync.Services;

/// <summary>
/// 下载远程自动化规则并写入主机的 Config/Automations 目录。
/// </summary>
/// <remarks>
/// 已核实 ClassIsland/Services/AutomationService.cs：目录是 Config/Automations（复数），
/// RefreshConfigs() 只刷新文件列表，真正生效仍需重启，与「不自动重启」的决策一致。
/// </remarks>
public sealed class AutomationSyncService
{
    private readonly LogService _log;

    public AutomationSyncService(LogService log)
    {
        _log = log;
    }

    /// <summary>主机自动化配置目录：{AppConfig}/Automations</summary>
    public static string AutomationFolder => Path.Combine(CommonDirectories.AppConfigPath, "Automations");

    /// <summary>
    /// 下载规则文件，校验为合法 JSON 后原子写入目标配置文件，并刷新配置列表。
    /// </summary>
    public async Task<bool> SyncAsync(AutomationItem item, string configName, HashMismatchAction mismatchAction,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.Url))
        {
            _log.Warn("清单中自动化规则的 Url 为空，已跳过。");
            return false;
        }

        var expected = HashHelper.ParseSha256(item.Hash);
        var urls = HttpHelper.BuildUrlChain(item.Url, item.Mirrors).ToList();
        if (urls.Count == 0)
        {
            _log.Warn("自动化规则没有可用的下载地址，已跳过。");
            return false;
        }

        var name = string.IsNullOrWhiteSpace(configName) ? "Default" : configName.Trim();
        var temp = Path.Combine(Path.GetTempPath(), $"cis-automation-{Guid.NewGuid():N}.json");
        try
        {
            await HttpHelper.DownloadFileAsync(urls, temp, m => _log.Warn(m), cancellationToken).ConfigureAwait(false);

            if (expected != null)
            {
                var actual = HashHelper.ComputeSha256(temp);
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    _log.Error($"自动化规则哈希校验失败：期望 {expected}，实际 {actual}。");
                    if (mismatchAction == HashMismatchAction.Abort)
                    {
                        return false;
                    }

                    _log.Warn("按配置 HashMismatchAction=Ignore，仍然写入规则文件。");
                }
            }

            string content;
            try
            {
                // 校验可解析为 JSON，避免把损坏内容写进配置目录
                using var document = JsonDocument.Parse(File.ReadAllText(temp));
                content = document.RootElement.GetRawText();
            }
            catch (Exception ex)
            {
                _log.Error("远程自动化规则不是合法的 JSON，已丢弃。", ex);
                return false;
            }

            Directory.CreateDirectory(AutomationFolder);
            var destination = Path.Combine(AutomationFolder, $"{AtomicFile.Sanitize(name)}.json");
            if (File.Exists(destination))
            {
                try
                {
                    File.Copy(destination, destination + ".bak", true);
                }
                catch (Exception ex)
                {
                    _log.Warn($"备份现有自动化配置失败（不影响写入）：{ex.Message}");
                }
            }

            AtomicFile.Write(destination, t => File.WriteAllText(t, content));
            _log.Info($"已更新自动化配置：{destination}");

            RefreshConfigs();
            return true;
        }
        finally
        {
            HttpHelper.TryDelete(temp);
        }
    }

    private void RefreshConfigs()
    {
        try
        {
            var automation = IAppHost.TryGetService<IAutomationService>();
            automation?.RefreshConfigs();
        }
        catch (Exception ex)
        {
            // 刷新失败不影响文件已写入的事实，只是本次运行内列表未刷新
            _log.Warn($"调用 IAutomationService.RefreshConfigs() 失败：{ex.Message}");
        }
    }
}
