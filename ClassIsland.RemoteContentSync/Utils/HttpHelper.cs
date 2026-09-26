using System.Net;
using System.Net.Http.Headers;

namespace ClassIsland.RemoteContentSync.Utils;

/// <summary>
/// 带主源/镜像回退的下载器。所有异常都会向上抛给调用方，由调用方决定只记日志还是中止。
/// </summary>
internal static class HttpHelper
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true
        })
        {
            Timeout = TimeSpan.FromSeconds(120)
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ClassIsland.RemoteContentSync", "1.0"));
        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
        return client;
    }

    /// <summary>把主源与镜像拼成有序的尝试链（去重）。</summary>
    public static IEnumerable<string> BuildUrlChain(string? primary, IEnumerable<string>? mirrors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var url in Enumerate(primary, mirrors))
        {
            if (seen.Add(url))
            {
                list.Add(url);
            }
        }

        return list;
    }

    private static IEnumerable<string> Enumerate(string? primary, IEnumerable<string>? mirrors)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            yield return primary.Trim();
        }

        foreach (var mirror in mirrors ?? [])
        {
            if (!string.IsNullOrWhiteSpace(mirror))
            {
                yield return mirror.Trim();
            }
        }
    }

    /// <summary>依次尝试每个地址，成功返回文本内容；全部失败抛出最后一个异常。</summary>
    public static async Task<string> GetStringAsync(IEnumerable<string> urls, Action<string>? onFailed = null,
        CancellationToken cancellationToken = default)
    {
        Exception? last = null;
        foreach (var url in urls)
        {
            try
            {
                using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseContentRead,
                    cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                last = ex;
                onFailed?.Invoke($"下载 {url} 失败：{ex.Message}");
            }
        }

        throw new IOException("所有下载地址均失败。", last);
    }

    /// <summary>依次尝试每个地址，成功把内容写入 <paramref name="destination"/>。</summary>
    public static async Task DownloadFileAsync(IEnumerable<string> urls, string destination,
        Action<string>? onFailed = null, CancellationToken cancellationToken = default)
    {
        Exception? last = null;
        foreach (var url in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using (var httpStream = await response.Content
                                 .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                {
                    await using var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write,
                        FileShare.None, 1 << 16, true);
                    await httpStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                }

                return;
            }
            catch (Exception ex)
            {
                last = ex;
                // 失败时清掉半截文件，避免留下损坏内容
                TryDelete(destination);
                onFailed?.Invoke($"下载 {url} 失败：{ex.Message}");
            }
        }

        throw new IOException("所有下载地址均失败。", last);
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 清理失败不影响主流程
        }
    }
}
