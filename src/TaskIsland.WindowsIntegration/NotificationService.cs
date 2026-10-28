using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace TaskIsland.WindowsIntegration;

public sealed class NotificationService : IDisposable
{
    private bool registered;
    public event Action<string>? Activated;
    public string? Error { get; private set; }

    public void Register()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnInvoked;
            AppNotificationManager.Default.Register();
            registered = true;
        }
        catch (Exception ex)
        {
            Error = $"通知注册失败（0x{ex.HResult:X8}），日程岛仍可使用。";
            AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
        }
    }

    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) => Activated?.Invoke(args.Argument);

#if DEBUG
    public void SendTest()
    {
        if (!registered) return;
        try
        {
            var notification = new AppNotificationBuilder().AddArgument("task", "bdab384c-0821-4b36-b1ed-543c19e38b77")
                .AddText("IsLet-Todo 通知验证").AddText("点击返回日程岛。此入口仅在 Debug 构建提供。")
                .BuildNotification();
            notification.Tag = "m1-test";
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex) { Error = $"通知发送失败（0x{ex.HResult:X8}）。"; }
    }
#endif

    public void Dispose()
    {
        if (!registered) return;
        AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
        AppNotificationManager.Default.Unregister();
        registered = false;
    }
}
