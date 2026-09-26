using System.Reflection;
using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using ClassIsland.Core.Services.Registry;

namespace ClassIsland.RemoteContentSync.Services;

/// <summary>
/// 顶部提醒提供方，用于告知「已准备好更新内容，将在下次启动时应用」。
/// </summary>
[NotificationProviderInfo("B7E1C4A2-5F31-4E8B-9A76-3D0C6E5F1A22", "远程同步", "\uE72C", "远程内容同步提醒")]
public sealed class RemoteSyncNotificationProvider : NotificationProviderBase
{
    /// <summary>
    /// 尝试创建提醒提供方。主机未扫描到本插件类型时会自行补注册；
    /// 仍然失败则返回 null，由调用方降级为仅写日志。
    /// </summary>
    public static RemoteSyncNotificationProvider? TryCreate(LogService log)
    {
        try
        {
            EnsureRegistered();
            return new RemoteSyncNotificationProvider();
        }
        catch (Exception ex)
        {
            log.Error("无法注册顶部提醒提供方，本次将仅记录日志。", ex);
            return null;
        }
    }

    /// <summary>
    /// 主机的 NotificationProviderRegistryService 由主机在插件扫描阶段填充。
    /// 若主机没有把本插件的类型加进去，这里通过反射补齐（ProviderType 的 setter 是 internal）。
    /// </summary>
    private static void EnsureRegistered()
    {
        var registered = NotificationProviderRegistryService.RegisteredProviders;
        var type = typeof(RemoteSyncNotificationProvider);
        if (registered.Any(x => x.ProviderType == type))
        {
            return;
        }

        var attribute = (NotificationProviderInfo?)Attribute.GetCustomAttribute(type, typeof(NotificationProviderInfo));
        if (attribute == null)
        {
            return;
        }

        typeof(NotificationProviderInfo)
            .GetProperty(nameof(NotificationProviderInfo.ProviderType), BindingFlags.Public | BindingFlags.Instance)
            ?.SetValue(attribute, type);
        registered.Add(attribute);
    }

    /// <summary>显示一条顶部提醒。</summary>
    public void Notify(string text)
    {
        ShowNotification(new NotificationRequest
        {
            MaskContent = NotificationContent.CreateSimpleTextContent(text),
        });
    }
}
