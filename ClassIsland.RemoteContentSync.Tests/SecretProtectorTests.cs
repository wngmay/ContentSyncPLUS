using ClassIsland.RemoteContentSync.Utils;
using Xunit;

namespace ClassIsland.RemoteContentSync.Tests;

public class SecretProtectorTests
{
    [Fact]
    public void ProtectThenUnprotect_RoundTrips()
    {
        const string plain = "unit-test-secret-placeholder-1234567890";
        var encrypted = SecretProtector.Protect(plain);

        Assert.False(string.IsNullOrEmpty(encrypted));
        Assert.True(SecretProtector.IsEncrypted(encrypted));
        Assert.DoesNotContain("github_pat_", encrypted); // 密文不得泄漏原文
        Assert.Equal(plain, SecretProtector.Unprotect(encrypted));
    }

    [Fact]
    public void Protect_Empty_ReturnsEmpty()
    {
        Assert.Equal("", SecretProtector.Protect(""));
        Assert.Equal("", SecretProtector.Protect(null));
        Assert.Equal("", SecretProtector.Unprotect(""));
    }

    [Fact]
    public void Unprotect_Plaintext_ReturnsAsIs()
    {
        // 兼容旧版明文配置：不抛异常、原样返回，且被判定为未加密
        Assert.Equal("legacy-token", SecretProtector.Unprotect("legacy-token"));
        Assert.False(SecretProtector.IsEncrypted("legacy-token"));
    }
}
