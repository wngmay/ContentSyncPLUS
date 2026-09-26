namespace ClassIsland.RemoteContentSync.Utils;

internal static class VersionHelper
{
    /// <summary>
    /// 判断远程版本是否应当覆盖本地版本。本地缺失一律视为需要更新。
    /// 版本号无法用 <see cref="Version"/> 解析时，退化为「字符串不同即更新」。
    /// </summary>
    public static bool IsRemoteNewer(string? local, string? remote)
    {
        if (string.IsNullOrWhiteSpace(local))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(remote))
        {
            return false;
        }

        if (Version.TryParse(local.Trim(), out var localVersion) &&
            Version.TryParse(remote.Trim(), out var remoteVersion))
        {
            // Version 会把缺失的 Build/Revision 记为 -1，导致 Version("1.0.0.0") > Version("1.0.0")。
            // 补齐为 0 后比较，否则清单里少写一段就会永远推不上去。
            var l = Pad(localVersion);
            var r = Pad(remoteVersion);
            for (var i = 0; i < 4; i++)
            {
                if (r[i] != l[i])
                {
                    return r[i] > l[i];
                }
            }

            return false;
        }

        return !string.Equals(local.Trim(), remote.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static int[] Pad(Version version) =>
    [
        version.Major,
        version.Minor,
        version.Build < 0 ? 0 : version.Build,
        version.Revision < 0 ? 0 : version.Revision
    ];
}
