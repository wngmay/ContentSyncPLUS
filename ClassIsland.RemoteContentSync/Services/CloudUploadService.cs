using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClassIsland.Core;
using ClassIsland.RemoteContentSync.Models;
using ClassIsland.RemoteContentSync.Utils;

namespace ClassIsland.RemoteContentSync.Services;

/// <summary>
/// 一键上传：把 ClassIsland 数据目录下的全部配置文件（Settings.json 与 Config 目录的所有 JSON，
/// 覆盖集控本体与各插件——即「集控 + 集控 PLUS」涉及的全部配置）以单个提交上传到 GitHub 仓库备份。
/// </summary>
/// <remarks>
/// 使用 GitHub Git Data API（blob → tree → commit → ref），全部文件打成一个 commit，
/// 不会在仓库历史里产生几十个零散提交。分支不存在时自动创建。
/// </remarks>
public sealed class CloudUploadService
{
    private const long MaxFileBytes = 4 * 1024 * 1024; // 单文件上限，正常配置远小于此

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly LogService _log;

    public CloudUploadService(LogService log)
    {
        _log = log;
    }

    /// <summary>上传结果。</summary>
    public sealed record UploadResult(bool Success, int FileCount, string Message);

    /// <summary>
    /// 执行上传。收集本机全部配置文件并推送到配置的 GitHub 仓库。
    /// </summary>
    public async Task<UploadResult> UploadAsync(SyncSettings settings, CancellationToken cancellationToken = default)
    {
        var repo = (settings.GitHubRepo ?? "").Trim();
        var token = SecretProtector.Unprotect(settings.GitHubToken).Trim();
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(repo) || repo.Split('/') is not { Length: 2 })
        {
            return new UploadResult(false, 0, "云端备份未配置：请先在设置页填写 GitHub 令牌与仓库（形如 owner/repo）。");
        }

        var branch = string.IsNullOrWhiteSpace(settings.GitHubBranch) ? "main" : settings.GitHubBranch.Trim();
        var remoteDir = BuildRemoteDirectory(settings.GitHubUploadPath, Environment.MachineName);

        var files = CollectFiles(CommonDirectories.AppRootFolderPath, CommonDirectories.AppConfigPath);
        if (files.Count == 0)
        {
            return new UploadResult(false, 0, "云端备份中止：没有找到可上传的配置文件。");
        }

        var entries = new List<(string Path, byte[] Content)>();
        foreach (var (local, relative) in files)
        {
            try
            {
                var text = await File.ReadAllTextAsync(local, cancellationToken).ConfigureAwait(false);
                entries.Add(($"{remoteDir}/{relative}", Encoding.UTF8.GetBytes(RedactSecrets(text))));
            }
            catch (Exception ex)
            {
                _log.Warn($"云端备份：读取 {local} 失败，已跳过：{ex.Message}");
            }
        }

        if (entries.Count == 0)
        {
            return new UploadResult(false, 0, "云端备份中止：所有配置文件都读取失败。");
        }

        try
        {
            using var client = CreateClient(token, settings.GitHubApiBase);
            var commitSha = await PushAsync(client, repo, branch, entries, cancellationToken).ConfigureAwait(false);
            var message = $"已把 {entries.Count} 个配置文件上传到 {repo}@{branch} 的 {remoteDir}/（提交 {commitSha[..7]}）。";
            var show_message = $"已同步配置文件至云端";
            _log.Info($"云端备份：{message}");
            return new UploadResult(true, entries.Count, show_message);
        }
        catch (Exception ex)
        {
            _log.Error("云端备份：上传失败。", ex);
            return new UploadResult(false, entries.Count, $"云端备份失败：{ex.Message}");
        }
    }

    /// <summary>收集要上传的文件：根目录 Settings.json + Config 目录递归的全部 .json。</summary>
    /// <returns>(本地路径, 仓库内相对路径) 列表。</returns>
    public static List<(string LocalPath, string RelativePath)> CollectFiles(string appRoot, string configRoot)
    {
        var list = new List<(string, string)>();

        var settingsJson = Path.Combine(appRoot, "Settings.json");
        if (File.Exists(settingsJson))
        {
            list.Add((settingsJson, "Settings.json"));
        }

        if (Directory.Exists(configRoot))
        {
            foreach (var file in Directory.EnumerateFiles(configRoot, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    if (new FileInfo(file).Length > MaxFileBytes)
                    {
                        continue;
                    }

                    var relative = Path.GetRelativePath(configRoot, file).Replace('\\', '/');
                    list.Add((file, $"Config/{relative}"));
                }
                catch
                {
                    // 单个文件访问失败不影响整体
                }
            }
        }

        return list;
    }

    /// <summary>把上传目录模板中的 {Machine} 占位符替换为本机名，并清理非法路径字符。</summary>
    public static string BuildRemoteDirectory(string template, string machineName)
    {
        var machine = AtomicFile.Sanitize(string.IsNullOrWhiteSpace(machineName) ? "unknown" : machineName).Replace(' ', '_');
        var dir = (template ?? "").Replace("{Machine}", machine, StringComparison.OrdinalIgnoreCase);
        return dir.Trim().Trim('/').Replace('\\', '/');
    }

    /// <summary>
    /// 上传前脱敏敏感信息：把 GitHub token（fine-grained `github_pat_` 与 classic `ghp_/gho_/ghu_/ghs_`）替换为占位符，
    /// 避免把密钥写进云端、也避免触发 GitHub secret scanning 的 push 拦截（表现为 422 Repository rule violations found）。
    /// </summary>
    public static string RedactSecrets(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content;
        }

        var result = System.Text.RegularExpressions.Regex.Replace(
            content, @"github_pat_[A-Za-z0-9_]+", "github_pat_REDACTED");
        result = System.Text.RegularExpressions.Regex.Replace(
            result, @"\b(ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{20,}\b", "$1_REDACTED");
        return result;
    }

    /// <summary>Git Data API：把多个文件作为一个提交推送到分支（分支不存在时自动创建）。</summary>
    private static async Task<string> PushAsync(HttpClient client, string repo, string branch,
        List<(string Path, byte[] Content)> entries, CancellationToken ct)
    {
        // 1. 取分支当前提交；404 表示分支不存在
        string? baseCommit = null;
        var refResponse = await client.GetAsync($"/repos/{repo}/git/ref/heads/{branch}", ct).ConfigureAwait(false);
        if (refResponse.IsSuccessStatusCode)
        {
            var refJson = await ReadJsonAsync(refResponse, ct).ConfigureAwait(false);
            baseCommit = refJson?["object"]?["sha"]?.GetValue<string>();
        }
        else if (refResponse.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(refResponse, ct).ConfigureAwait(false);
        }

        // 2. 创建 blob
        var treeItems = new JsonArray();
        foreach (var (path, content) in entries)
        {
            var blobRequest = new HttpRequestMessage(HttpMethod.Post, $"/repos/{repo}/git/blobs")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { content = Convert.ToBase64String(content), encoding = "base64" }),
                    Encoding.UTF8, "application/json")
            };
            using var blobResponse = await client.SendAsync(blobRequest, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(blobResponse, ct).ConfigureAwait(false);
            var blobJson = await ReadJsonAsync(blobResponse, ct).ConfigureAwait(false);

            treeItems.Add(new JsonObject
            {
                ["path"] = path,
                ["mode"] = "100644",
                ["type"] = "blob",
                ["sha"] = blobJson?["sha"]?.GetValue<string>()
            });
        }

        // 3. 创建 tree
        string? baseTree = null;
        if (baseCommit != null)
        {
            var commitResponse = await client.GetAsync($"/repos/{repo}/git/commits/{baseCommit}", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(commitResponse, ct).ConfigureAwait(false);
            var commitJson = await ReadJsonAsync(commitResponse, ct).ConfigureAwait(false);
            baseTree = commitJson?["tree"]?["sha"]?.GetValue<string>();
        }

        var treeBody = new JsonObject { ["tree"] = treeItems };
        if (baseTree != null)
        {
            treeBody["base_tree"] = baseTree;
        }

        using var treeRequest = new HttpRequestMessage(HttpMethod.Post, $"/repos/{repo}/git/trees")
        {
            Content = new StringContent(treeBody.ToJsonString(), Encoding.UTF8, "application/json")
        };
        using var treeResponse = await client.SendAsync(treeRequest, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(treeResponse, ct).ConfigureAwait(false);
        var newTree = (await ReadJsonAsync(treeResponse, ct).ConfigureAwait(false))?["sha"]?.GetValue<string>()
                      ?? throw new IOException("GitHub 未返回新 tree 的 sha。");

        // 4. 创建 commit
        var commitBody = new JsonObject
        {
            ["message"] = $"ClassIsland 配置备份（{DateTime.Now:yyyy-MM-dd HH:mm:ss}，{entries.Count} 个文件）",
            ["tree"] = newTree,
            ["parents"] = baseCommit != null ? new JsonArray(baseCommit) : new JsonArray()
        };
        using var commitRequest = new HttpRequestMessage(HttpMethod.Post, $"/repos/{repo}/git/commits")
        {
            Content = new StringContent(commitBody.ToJsonString(), Encoding.UTF8, "application/json")
        };
        using var newCommitResponse = await client.SendAsync(commitRequest, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(newCommitResponse, ct).ConfigureAwait(false);
        var newCommit = (await ReadJsonAsync(newCommitResponse, ct).ConfigureAwait(false))?["sha"]?.GetValue<string>()
                        ?? throw new IOException("GitHub 未返回新提交的 sha。");

        // 5. 更新 / 创建分支引用
        if (baseCommit != null)
        {
            var patchBody = new JsonObject { ["sha"] = newCommit, ["force"] = false };
            using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, $"/repos/{repo}/git/refs/heads/{branch}")
            {
                Content = new StringContent(patchBody.ToJsonString(), Encoding.UTF8, "application/json")
            };
            using var patchResponse = await client.SendAsync(patchRequest, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(patchResponse, ct).ConfigureAwait(false);
        }
        else
        {
            var createBody = new JsonObject { ["ref"] = $"refs/heads/{branch}", ["sha"] = newCommit };
            using var createRequest = new HttpRequestMessage(HttpMethod.Post, $"/repos/{repo}/git/refs")
            {
                Content = new StringContent(createBody.ToJsonString(), Encoding.UTF8, "application/json")
            };
            using var createResponse = await client.SendAsync(createRequest, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(createResponse, ct).ConfigureAwait(false);
        }

        return newCommit;
    }

    private static HttpClient CreateClient(string token, string apiBase)
    {
        var baseUri = new Uri((string.IsNullOrWhiteSpace(apiBase) ? "https://api.github.com" : apiBase).TrimEnd('/') + "/");
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(60)
        };
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("ClassIsland.RemoteContentSync", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static async Task<JsonObject?> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text)?.AsObject();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var detail = text;
        try
        {
            var node = JsonNode.Parse(text);
            var message = node?["message"]?.GetValue<string>() ?? "";
            var extra = node?["details"] ?? node?["errors"];
            detail = extra != null
                ? $"{message} | {extra.ToJsonString()}"
                : (string.IsNullOrWhiteSpace(message) ? text : message);
        }
        catch
        {
            // 保留原始内容做兜底
        }

        throw new IOException($"GitHub API {(int)response.StatusCode}：{Truncate(detail, 500)}");
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
