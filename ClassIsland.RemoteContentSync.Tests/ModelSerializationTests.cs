using System.Text.Json;
using System.Text.Json.Serialization;
using ClassIsland.RemoteContentSync.Models;
using Xunit;

namespace ClassIsland.RemoteContentSync.Tests;

public class ModelSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void SyncManifest_DeserializesFromJson()
    {
        const string json = """
        {
          "Version": 2,
          "Plugins": [
            { "Id": "a.b", "Version": "1.2.0", "Url": "https://x/a.cipx",
              "Hash": "sha256:00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff",
              "Mirrors": ["https://y/a.cipx"] }
          ],
          "Automation": { "Version": 5, "Url": "https://x/rules.json", "Mirrors": [] }
        }
        """;

        var manifest = JsonSerializer.Deserialize<SyncManifest>(json, JsonOptions);

        Assert.NotNull(manifest);
        Assert.Equal(2, manifest!.Version);
        Assert.Single(manifest.Plugins);
        Assert.Equal("a.b", manifest.Plugins[0].Id);
        Assert.NotNull(manifest.Automation);
        Assert.Equal(5, manifest.Automation!.Version);
    }

    [Fact]
    public void SyncManifest_AutomationCanBeOmitted()
    {
        const string json = """{ "Version": 1, "Plugins": [] }""";
        var manifest = JsonSerializer.Deserialize<SyncManifest>(json, JsonOptions);

        Assert.NotNull(manifest);
        Assert.Null(manifest!.Automation);
        Assert.Empty(manifest.Plugins);
    }

    [Fact]
    public void HashMismatchAction_SerializesAsString()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        };

        var settings = new SyncSettings { HashMismatchAction = HashMismatchAction.Ignore };
        var json = JsonSerializer.Serialize(settings, options);

        Assert.Contains("\"Ignore\"", json);
        Assert.DoesNotContain("\"HashMismatchAction\":1", json);
    }
}
