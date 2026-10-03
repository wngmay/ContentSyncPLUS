using ClassIsland.RemoteContentSync.Models;
using Xunit;

namespace ClassIsland.RemoteContentSync.Tests;

public class SyncSettingsTests
{
    [Fact]
    public void Defaults_AreSane()
    {
        var s = new SyncSettings();

        Assert.False(string.IsNullOrWhiteSpace(s.SyncManifestUrl));
        Assert.NotEmpty(s.SyncManifestMirrors);
        Assert.True(s.AutoInstall);
        Assert.Equal("Default", s.AutomationConfigName);
        Assert.Equal(HashMismatchAction.Abort, s.HashMismatchAction);
        Assert.Equal(14, s.LogRetentionDays);
        Assert.Empty(s.AllowedPluginIds);
    }

    [Fact]
    public void Normalize_ClampsNegativeInterval()
    {
        var s = new SyncSettings { SyncIntervalMinutes = -5 };
        s.Normalize();
        Assert.Equal(0, s.SyncIntervalMinutes);
    }

    [Fact]
    public void Normalize_ClampsLogRetentionDays()
    {
        var s = new SyncSettings { LogRetentionDays = 0 };
        s.Normalize();
        Assert.Equal(1, s.LogRetentionDays);
    }

    [Fact]
    public void Normalize_FillsBlankAutomationConfigName()
    {
        var s = new SyncSettings { AutomationConfigName = "   " };
        s.Normalize();
        Assert.Equal("Default", s.AutomationConfigName);
    }

    [Fact]
    public void Normalize_RepairsNullCollections()
    {
        var s = new SyncSettings
        {
            SyncManifestUrl = null!,
            SyncManifestMirrors = null!,
            AllowedPluginIds = null!
        };
        s.Normalize();

        Assert.NotNull(s.SyncManifestUrl);
        Assert.NotNull(s.SyncManifestMirrors);
        Assert.NotNull(s.AllowedPluginIds);
    }
}
