using ClassIsland.RemoteContentSync.Models;
using ClassIsland.RemoteContentSync.Services;
using Xunit;

namespace ClassIsland.RemoteContentSync.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"cis-settings-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void LoadSettings_CreatesDefaultFile_WhenMissing()
    {
        var service = new SettingsService(_dir);
        var settings = service.LoadSettings();

        Assert.NotNull(settings);
        Assert.True(File.Exists(Path.Combine(_dir, "settings.json")));
        Assert.False(string.IsNullOrWhiteSpace(settings.SyncManifestUrl));
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var service = new SettingsService(_dir);
        var original = new SyncSettings
        {
            SyncManifestUrl = "https://example.com/sync.json",
            SyncIntervalMinutes = 30,
            AutoInstall = false,
            AutomationConfigName = "MyRules",
            HashMismatchAction = HashMismatchAction.Ignore,
            LogRetentionDays = 7
        };
        service.SaveSettings(original);

        var loaded = service.LoadSettings();
        Assert.Equal("https://example.com/sync.json", loaded.SyncManifestUrl);
        Assert.Equal(30, loaded.SyncIntervalMinutes);
        Assert.False(loaded.AutoInstall);
        Assert.Equal("MyRules", loaded.AutomationConfigName);
        Assert.Equal(HashMismatchAction.Ignore, loaded.HashMismatchAction);
        Assert.Equal(7, loaded.LogRetentionDays);
    }

    [Fact]
    public void LoadSettings_FallsBackToDefaults_WhenCorrupt()
    {
        var service = new SettingsService(_dir);
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ this is not valid json");

        var settings = service.LoadSettings();

        Assert.NotNull(settings);
        // 损坏文件被移走，生成新的默认配置
        Assert.True(File.Exists(Path.Combine(_dir, "settings.json.broken")));
        Assert.False(string.IsNullOrWhiteSpace(settings.SyncManifestUrl));
    }

    [Fact]
    public void LoadState_ReturnsEmpty_WhenMissing()
    {
        var service = new SettingsService(_dir);
        var state = service.LoadState();

        Assert.NotNull(state);
        Assert.Empty(state.Plugins);
        Assert.False(state.PendingRestart);
    }

    [Fact]
    public void SaveThenLoadState_RoundTrips()
    {
        var service = new SettingsService(_dir);
        var state = new SyncState
        {
            LastManifestVersion = 3,
            AutomationVersion = 2,
            PendingRestart = true
        };
        state.Plugins["example.plugin"] = "1.2.0";
        service.SaveState(state);

        var loaded = service.LoadState();
        Assert.Equal(3, loaded.LastManifestVersion);
        Assert.Equal(2, loaded.AutomationVersion);
        Assert.True(loaded.PendingRestart);
        Assert.Equal("1.2.0", loaded.Plugins["example.plugin"]);
    }
}
