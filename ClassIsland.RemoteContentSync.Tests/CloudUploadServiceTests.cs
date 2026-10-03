using ClassIsland.RemoteContentSync.Services;
using Xunit;

namespace ClassIsland.RemoteContentSync.Tests;

public class CloudUploadServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"cis-upload-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Theory]
    [InlineData("backups/{Machine}", "PC-01", "backups/PC-01")]
    [InlineData("backups/{machine}", "PC-01", "backups/PC-01")]      // 占位符大小写不敏感
    [InlineData("/backups/foo/", "PC-01", "backups/foo")]            // 去掉首尾斜杠
    [InlineData("backups\\foo", "PC-01", "backups/foo")]             // 反斜杠规范化
    [InlineData("", "PC-01", "")]
    public void BuildRemoteDirectory_ReplacesPlaceholderAndNormalizes(string template, string machine, string expected)
    {
        Assert.Equal(expected, CloudUploadService.BuildRemoteDirectory(template, machine));
    }

    [Fact]
    public void BuildRemoteDirectory_SanitizesMachineName()
    {
        var result = CloudUploadService.BuildRemoteDirectory("backups/{Machine}", "a:b<c>");
        Assert.DoesNotContain(":", result);
        Assert.DoesNotContain("<", result);
    }

    [Fact]
    public void CollectFiles_IncludesSettingsAndConfigJson()
    {
        var config = Path.Combine(_root, "Config");
        Directory.CreateDirectory(Path.Combine(config, "Automations"));
        Directory.CreateDirectory(Path.Combine(config, "Plugins", "demo"));

        File.WriteAllText(Path.Combine(_root, "Settings.json"), "{}");
        File.WriteAllText(Path.Combine(config, "Automations", "Default.json"), "[]");
        File.WriteAllText(Path.Combine(config, "Plugins", "demo", "settings.json"), "{}");
        File.WriteAllText(Path.Combine(config, "note.txt"), "不是 json，不应收集");

        var files = CloudUploadService.CollectFiles(_root, config);

        var relatives = files.Select(f => f.RelativePath).ToList();
        Assert.Contains("Settings.json", relatives);
        Assert.Contains("Config/Automations/Default.json", relatives);
        Assert.Contains("Config/Plugins/demo/settings.json", relatives);
        Assert.Equal(3, files.Count);
    }

    [Fact]
    public void CollectFiles_ReturnsEmpty_WhenNothingExists()
    {
        Directory.CreateDirectory(_root);
        Assert.Empty(CloudUploadService.CollectFiles(_root, Path.Combine(_root, "Config")));
    }

    [Theory]
    [InlineData("github_pat_EXAMPLE_NOT_A_REAL_TOKEN", "github_pat_REDACTED")]
    [InlineData("ghp_abcdefghijklmnopqrstuvwxyz123456", "ghp_REDACTED")]
    [InlineData("gho_abcdefghijklmnopqrstuvwxyz123456", "gho_REDACTED")]
    public void RedactSecrets_ReplacesTokens(string input, string expected)
    {
        Assert.Equal(expected, CloudUploadService.RedactSecrets(input));
    }

    [Fact]
    public void RedactSecrets_KeepsNormalContent()
    {
        const string json = """{"SyncManifestUrl":"https://example.com/a.json","AutoInstall":true}""";
        Assert.Equal(json, CloudUploadService.RedactSecrets(json));
    }
}
