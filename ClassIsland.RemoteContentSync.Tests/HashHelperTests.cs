using ClassIsland.RemoteContentSync.Utils;
using Xunit;

namespace ClassIsland.RemoteContentSync.Tests;

public class HashHelperTests
{
    private const string ValidSha256 = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("md5:00112233445566778899aabbccddeeff", null)]          // 不支持的算法
    [InlineData("sha256:short", null)]                                  // 长度不足
    [InlineData("sha256:00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff00", null)] // 长度超出
    public void ParseSha256_ReturnsNullForInvalid(string? input, string? expected)
    {
        Assert.Equal(expected, HashHelper.ParseSha256(input));
    }

    [Theory]
    [InlineData("sha256:00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff", ValidSha256)]
    [InlineData("00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff", ValidSha256)] // 无前缀
    [InlineData("SHA256:00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff", ValidSha256)] // 大小写
    [InlineData("sha256:0011-2233-4455-6677-8899-aabb-ccdd-eeff-0011-2233-4455-6677-8899-aabb-ccdd-eeff", ValidSha256)] // 带连字符
    public void ParseSha256_NormalizesToLowercaseHex(string input, string expected)
    {
        Assert.Equal(expected, HashHelper.ParseSha256(input));
    }

    [Fact]
    public void ComputeSha256_MatchesKnownValue()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cis-hash-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "abc");
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                HashHelper.ComputeSha256(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
