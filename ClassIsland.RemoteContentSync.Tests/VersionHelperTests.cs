using ClassIsland.RemoteContentSync.Utils;
using Xunit;

namespace ClassIsland.RemoteContentSync.Tests;

public class VersionHelperTests
{
    [Theory]
    [InlineData(null, "1.0.0", true)]      // 本地缺失 → 需要更新
    [InlineData("", "1.0.0", true)]        // 本地空字符串 → 需要更新
    [InlineData("1.0.0", null, false)]     // 远程缺失 → 不更新
    [InlineData("1.0.0", "", false)]
    [InlineData("1.0.0", "1.0.1", true)]   // 远程更新
    [InlineData("1.0.1", "1.0.0", false)]  // 远程更旧
    [InlineData("1.0.0", "1.0.0", false)]  // 相同
    [InlineData("1.0.0", "2.0.0", true)]
    [InlineData("2.0.0", "1.9.9", false)]
    public void IsRemoteNewer_WithVersionStrings(string? local, string? remote, bool expected)
    {
        Assert.Equal(expected, VersionHelper.IsRemoteNewer(local, remote));
    }

    [Fact]
    public void IsRemoteNewer_PadsMissingSegments()
    {
        // Version("1.0.0.0") 与 Version("1.0.0") 补齐后相等，不应误判为需要更新
        Assert.False(VersionHelper.IsRemoteNewer("1.0.0", "1.0.0.0"));
        Assert.False(VersionHelper.IsRemoteNewer("1.0.0.0", "1.0.0"));
        Assert.True(VersionHelper.IsRemoteNewer("1.0.0", "1.0.0.1"));
    }

    [Fact]
    public void IsRemoteNewer_FallsBackToStringCompare()
    {
        // 无法解析为 Version 时，退化为「字符串不同即更新」
        Assert.True(VersionHelper.IsRemoteNewer("abc", "abd"));
        Assert.False(VersionHelper.IsRemoteNewer("abc", "abc"));
        Assert.False(VersionHelper.IsRemoteNewer("1.0.0-beta", "1.0.0-beta"));
    }

    [Fact]
    public void IsRemoteNewer_IgnoresCaseAndWhitespace()
    {
        Assert.False(VersionHelper.IsRemoteNewer("1.2.0", " 1.2.0 "));
    }
}
