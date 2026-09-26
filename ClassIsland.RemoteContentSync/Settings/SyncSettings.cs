using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ClassIsland.RemoteContentSync.Settings;

public class SyncSettings : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void SetProperty<T>(ref T backingField, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(backingField, value))
            return;
        backingField = value;
        OnPropertyChanged(propertyName);
    }

    protected virtual void OnPropertyChanged(string? propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }


    public static SyncSettings Load(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<SyncSettings>(json) ?? new SyncSettings();
        }
        catch
        {
            return new SyncSettings(); // 文件不存在或损坏时返回默认值
        }
    }

    public void Save(string filePath)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }
}