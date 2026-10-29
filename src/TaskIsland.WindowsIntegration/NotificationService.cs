using TaskIsland.Application;
using TaskIsland.Domain;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace TaskIsland.WindowsIntegration;

public sealed class NotificationService : IReminderSender, IDisposable
{
    private bool registered;
    public event Action<string>? Activated;
    public event Action<string>? Diagnostic;
    public string? Error { get; private set; }

    public void Register()
    {
        if (registered) return;
        try
        {
            var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定通知应用路径");
            if (NotificationPreferenceGuard.IsLegacyDisabled(executablePath))
            {
                Error = "系统通知此前已关闭，请在系统设置中确认通知偏好";
                Report(() => "notification.registration_skipped previous_preference_disabled");
                return;
            }
            NotificationIdentity.EnsureRegistered(executablePath);
            AppNotificationManager.Default.NotificationInvoked += OnInvoked;
            AppNotificationManager.Default.Register();
            registered = true; Error = null;
            Report(() => $"notification.register identity={NotificationIdentity.AppUserModelId} supported={AppNotificationManager.IsSupported()} setting={AppNotificationManager.Default.Setting}");
        }
        catch (Exception ex)
        {
            Error = $"通知注册失败（0x{ex.HResult:X8}），日程岛仍可使用。";
            AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
        }
    }

    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) => Activated?.Invoke(args.Argument);

    public async Task SendAsync(IReadOnlyList<TaskItem> tasks, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!registered)
        {
            Error ??= "系统通知未注册，暂时无法发送提醒。";
            throw new InvalidOperationException(Error);
        }
        try
        {
            var content = ReminderNotification.Create(tasks);
            var builder = new AppNotificationBuilder().AddText(content.Title).AddText(content.Body);
            if (tasks.Count == 1) builder.AddArgument("task", tasks[0].Id.ToString("D"));
            else builder.AddArgument("action", "show");
            var notification = builder.BuildNotification();
            notification.Tag = content.Tag;
            notification.Group = content.Group;
            cancellationToken.ThrowIfCancellationRequested();
            // Windows owns banners and Do Not Disturb. Never elevate priority or create fallback alerts.
            AppNotificationManager.Default.Show(notification);
            Report(() => $"notification.submit id={notification.Id} setting={AppNotificationManager.Default.Setting} suppressed={notification.SuppressDisplay}");
            if (notification.Id == 0) throw new InvalidOperationException("系统未接受此提醒。");
            Error = null;
            await InspectAsync(notification.Id);
            return;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Error = $"系统通知提交失败（0x{ex.HResult:X8}）。";
            throw new InvalidOperationException(Error);
        }
    }

    public async Task InspectAsync(uint submittedId = 0)
    {
        if (!registered) return;
        try
        {
            if (submittedId != 0) await Task.Delay(300).ConfigureAwait(false);
            var history = await AppNotificationManager.Default.GetAllAsync();
            Report(() => $"notification.history setting={AppNotificationManager.Default.Setting} count={history.Count} submittedPresent={history.Any(x => x.Id == submittedId)}");
        }
        catch (Exception ex) { Report(() => $"notification.history_failed 0x{ex.HResult:X8}"); }
    }
    private void Report(Func<string> message)
    {
        try { Diagnostic?.Invoke(message()); } catch { /* Diagnostics cannot turn an accepted submission into a retry. */ }
    }

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
