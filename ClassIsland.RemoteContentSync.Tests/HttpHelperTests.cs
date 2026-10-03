using ClassIsland.RemoteContentSync.Utils;
using Xunit;

namespace ClassIsland.RemoteContentSync.Tests;

public class HttpHelperTests
{
    [Fact]
    public void BuildUrlChain_ReturnsEmpty_WhenNoSources()
    {
        Assert.Empty(HttpHelper.BuildUrlChain(null, null));
        Assert.Empty(HttpHelper.BuildUrlChain("", []));
        Assert.Empty(HttpHelper.BuildUrlChain(null, ["", "  "]));
    }

    [Fact]
    public void BuildUrlChain_OrdersPrimaryBeforeMirrors()
    {
        Assert.Equal(new[] { "a", "b", "c" },
            HttpHelper.BuildUrlChain("a", ["b", "c"]));
        Assert.Equal(new[] { "b" },
            HttpHelper.BuildUrlChain(null, ["b"]));
    }

    [Fact]
    public void BuildUrlChain_TrimsAndDeduplicates()
    {
        Assert.Equal(new[] { "a", "b" },
            HttpHelper.BuildUrlChain(" a ", ["a", " b ", ""]));
    }

    [Fact]
    public void BuildUrlChain_IgnoresBlankMirrors()
    {
        Assert.Equal(new[] { "a", "b" },
            HttpHelper.BuildUrlChain("a", [null!, "", " ", "b"]));
    }
}
