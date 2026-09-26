using System.Security.Cryptography;

namespace ClassIsland.RemoteContentSync.Utils;

internal static class HashHelper
{
    /// <summary>计算文件的 SHA256 十六进制小写字符串。</summary>
    public static string ComputeSha256(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    /// <summary>
    /// 解析 "sha256:&lt;hex&gt;" 形式的声明。无法识别为 SHA256 时返回 null（视为未提供）。
    /// </summary>
    public static string? ParseSha256(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return null;
        }

        var value = hash.Trim();
        var sep = value.IndexOf(':');
        if (sep >= 0)
        {
            var algo = value[..sep].Trim().ToLowerInvariant();
            if (algo != "sha256")
            {
                return null;
            }

            value = value[(sep + 1)..];
        }

        value = value.Replace("-", "").Replace(" ", "").Replace("_", "");
        return value.Length == 64 ? value.ToLowerInvariant() : null;
    }
}
