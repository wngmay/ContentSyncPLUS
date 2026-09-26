using System.Text.Json;
using System.Text.Json.Serialization;
using ClassIsland.RemoteContentSync.Models;
using ClassIsland.RemoteContentSync.Utils;

namespace ClassIsland.RemoteContentSync.Services;

/// <summary>
/// 读写插件配置目录下的 settings.json 与 state.json。
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        // 让 HashMismatchAction 写成 "Abort" 而不是 0，便于管理员手改配置
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _configFolder;

    public SettingsService(string configFolder)
    {
        _configFolder = configFolder;
        Directory.CreateDirectory(_configFolder);
    }

    public string ConfigFolder => _configFolder;

    private string SettingsPath => Path.Combine(_configFolder, "settings.json");

    private string StatePath => Path.Combine(_configFolder, "state.json");

    /// <summary>
    /// 读取配置。不存在时写入默认配置文件并返回默认值。
    /// </summary>
    public SyncSettings LoadSettings()
    {
        var path = SettingsPath;
        if (!File.Exists(path))
        {
            var defaults = new SyncSettings();
            SaveSettings(defaults);
            return defaults;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<SyncSettings>(File.ReadAllText(path), JsonOptions);
            var result = settings ?? new SyncSettings();
            result.Normalize();
            return result;
        }
        catch (Exception)
        {
            // 配置损坏时回退默认值，但保留损坏文件供排障
            var broken = path + ".broken";
            try
            {
                File.Move(path, broken, true);
            }
            catch
            {
                // 忽略
            }

            var defaults = new SyncSettings();
            SaveSettings(defaults);
            return defaults;
        }
    }

    public void SaveSettings(SyncSettings settings)
    {
        settings.Normalize();
        AtomicFile.Write(SettingsPath, temp => File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions)));
    }

    /// <summary>读取状态，不存在或损坏时返回空状态。</summary>
    public SyncState LoadState()
    {
        var path = StatePath;
        if (!File.Exists(path))
        {
            return new SyncState();
        }

        try
        {
            return JsonSerializer.Deserialize<SyncState>(File.ReadAllText(path), JsonOptions) ?? new SyncState();
        }
        catch (Exception)
        {
            return new SyncState();
        }
    }

    public void SaveState(SyncState state)
    {
        AtomicFile.Write(StatePath, temp => File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions)));
    }
}
