using System.IO.Compression;
using System.Text.RegularExpressions;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.RemoteContentSync.Models;
using ClassIsland.RemoteContentSync.Utils;

namespace ClassIsland.RemoteContentSync.Services;

/// <summary>
/// 下载 .cipx 插件包、校验，并放入主机待安装目录，让主机下次启动时完成安装。
/// </summary>
/// <remarks>
/// 主机处理逻辑（已核实 ClassIsland/Services/PluginService.cs 的 ProcessPluginsInstall）：
/// 启动时扫描 {AppCache}/PluginPackages 下的 *.cipx，解压到 {AppRoot}/Plugins/{manifest.Id}/。
/// 因此本插件不直接解压，只负责把包放进待安装目录。
/// </remarks>
public sealed class CipxInstallService
{
    private static readonly Regex VersionRegex =
        new(@"^\s*version\s*:\s*[""']?(?<v>[^""'\r\n]+)[""']?\s*$", RegexOptions.Multiline);

    private readonly LogService _log;

    public CipxInstallService(LogService log)
    {
        _log = log;
    }

    /// <summary>主机待安装插件包目录：{AppRoot}/Cache/PluginPackages</summary>
    public static string PendingPackagesPath =>
        Path.Combine(CommonDirectories.AppCacheFolderPath, "PluginPackages");

    /// <summary>已安装插件根目录：{AppRoot}/Plugins</summary>
    public static string InstalledPluginsPath =>
        Path.Combine(CommonDirectories.AppRootFolderPath, "Plugins");

    /// <summary>
    /// 读取本地已安装插件版本。优先读已安装目录下的 manifest.yml。
    /// </summary>
    public string? GetLocalVersion(string pluginId)
    {
        var manifestPath = Path.Combine(InstalledPluginsPath, AtomicFile.Sanitize(pluginId), "manifest.yml");
        try
        {
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            var text = File.ReadAllText(manifestPath);
            var match = VersionRegex.Match(text);
            return match.Success ? match.Groups["v"].Value.Trim() : null;
        }
        catch (Exception ex)
        {
            _log.Warn($"读取本地插件 {pluginId} 的 manifest.yml 失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 下载并校验插件包，成功后放入待安装目录。
    /// </summary>
    /// <returns>是否成功放入待安装目录。</returns>
    public async Task<bool> PreparePackageAsync(PluginItem item, HashMismatchAction mismatchAction,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Url))
        {
            _log.Warn("清单中存在缺少 Id 或 Url 的插件条目，已跳过。");
            return false;
        }

        var expected = HashHelper.ParseSha256(item.Hash);
        if (!string.IsNullOrWhiteSpace(item.Hash) && expected == null)
        {
            _log.Warn($"插件 {item.Id} 的 Hash 字段无法识别为 sha256，将跳过校验。");
        }

        var urls = HttpHelper.BuildUrlChain(item.Url, item.Mirrors).ToList();
        if (urls.Count == 0)
        {
            _log.Warn($"插件 {item.Id} 没有可用的下载地址，已跳过。");
            return false;
        }

        var temp = Path.Combine(Path.GetTempPath(), $"cis-{Guid.NewGuid():N}{IPluginService.PluginPackageExtension}");
        try
        {
            await HttpHelper.DownloadFileAsync(urls, temp, m => _log.Warn(m), cancellationToken).ConfigureAwait(false);

            if (expected != null)
            {
                var actual = HashHelper.ComputeSha256(temp);
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    _log.Error($"插件 {item.Id} 哈希校验失败：期望 {expected}，实际 {actual}。");
                    if (mismatchAction == HashMismatchAction.Abort)
                    {
                        return false;
                    }

                    _log.Warn($"按配置 HashMismatchAction=Ignore，仍然放入待安装目录：{item.Id}");
                }
            }

            if (!IsValidPackage(temp))
            {
                _log.Error($"插件 {item.Id} 的下载内容不是合法的插件包（非 zip 或缺少 manifest.yml），已丢弃。");
                return false;
            }

            var directory = PendingPackagesPath;
            Directory.CreateDirectory(directory);
            var destination = Path.Combine(directory,
                $"{AtomicFile.Sanitize(item.Id)}-{AtomicFile.Sanitize(item.Version)}{IPluginService.PluginPackageExtension}");
            File.Move(temp, destination, true);
            _log.Info($"已放入待安装目录：{destination}");
            return true;
        }
        finally
        {
            HttpHelper.TryDelete(temp);
        }
    }

    /// <summary>校验是否为含 manifest.yml 的合法 zip 包。</summary>
    private static bool IsValidPackage(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return zip.GetEntry("manifest.yml") != null;
        }
        catch
        {
            return false;
        }
    }
}
