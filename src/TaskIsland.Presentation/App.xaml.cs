using System.IO;
using System.Diagnostics;
using System.Windows;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using TaskIsland.WindowsIntegration;
using TaskIsland.Infrastructure;
using TaskIsland.Application;
using Microsoft.Win32;

namespace TaskIsland.Presentation;

public partial class App : System.Windows.Application
{
    private SingleInstance? instance;
    private NotificationService? notifications;
    private TrayService? tray;
    private IslandWindow? island;
    private SqliteTaskStore? taskStore;
    private TaskIsland.Application.TaskScheduler? scheduler;
    private AttachmentService? attachments;
    private bool tasksReady;
    private string? pendingActivation;
    private string? integrationWarning;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var startupWatch = Stopwatch.StartNew();
        try
        {
            instance = new SingleInstance();
            WriteEvent($"startup.instance {startupWatch.ElapsedMilliseconds}ms");
            if (instance.IsPrimary)
            {
                // Begin accepting second launches before notification registration and XAML initialization.
                instance.Activated += value => Dispatcher.BeginInvoke(() => HandleActivation(value));
                instance.Failed += ex => Dispatcher.BeginInvoke(() => { WriteDiagnostic("ipc", ex); island?.ShowError("启动转发不可用，请退出后重新启动。"); });
                instance.Listen();
            }
            var comActivation = e.Args.Any(value => value.StartsWith("----AppNotificationActivated:", StringComparison.Ordinal));
            if (!instance.IsPrimary && !comActivation)
            {
                await instance.ForwardAsync("show", CancellationToken.None);
                Shutdown();
                return;
            }
            notifications = new NotificationService();
            notifications.Diagnostic += WriteEvent;
            notifications.Activated += value => Dispatcher.BeginInvoke(() => RouteNotificationAsync(value));
            notifications.Register();
            WriteEvent($"startup.notifications {startupWatch.ElapsedMilliseconds}ms");
            var argument = "show";
            try
            {
                var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
                WriteEvent($"startup.activation {startupWatch.ElapsedMilliseconds}ms");
                if (activation.Kind == ExtendedActivationKind.AppNotification && activation.Data is AppNotificationActivatedEventArgs data)
                    argument = data.Argument;
            }
            catch (Exception ex) { WriteDiagnostic("activation", ex); }
            if (!instance.IsPrimary)
            {
                if (argument != "show") await RouteNotificationAsync(argument);
                else
                {
                    // COM may initially report Launch; keep the secondary alive for NotificationInvoked.
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    if (!Dispatcher.HasShutdownStarted) await RouteNotificationAsync("show");
                }
                return;
            }
            island = new IslandWindow(notifications);
            MainWindow = island;
            tray = new TrayService(() => Dispatch(() => island.OpenTasks(true)),
                () => Dispatch(() => island.RequestExit()), () => Dispatch(OpenBackupDirectory));
            island.Show();
            try { island.StartFullscreenMonitoring(); } catch (Exception ex) { WriteDiagnostic("fullscreen", ex); integrationWarning = "全屏检测不可用"; }
            WriteEvent($"startup.window {startupWatch.ElapsedMilliseconds}ms");
            if (argument != "show") HandleActivation(argument);
            taskStore = new SqliteTaskStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskIsland"));
            await taskStore.InitializeAsync();
            if (Dispatcher.HasShutdownStarted) return;
            var mutationGate = new TaskMutationGate();
            var taskService = new TaskService(taskStore, TimeProvider.System, mutationGate);
            attachments = new AttachmentService(taskStore, new ManagedAttachmentFiles(Path.GetDirectoryName(taskStore.DatabasePath)!), TimeProvider.System, mutationGate);
            island.AttachAttachments(attachments);
            if (!await attachments.RetryCleanupAsync()) integrationWarning = "部分附件仍在等待清理";
            await taskService.ReconcileTimeAsync();
            await island.AttachTasksAsync(taskService);
            if (Dispatcher.HasShutdownStarted) return;
            await island.AttachSettingsAsync(new SettingsService(taskStore));
            if (Dispatcher.HasShutdownStarted) return;
            tasksReady = true;
            _ = notifications.InspectAsync();
            if (pendingActivation is { } pending) { pendingActivation = null; HandleActivation(pending); }
            if (notifications.Error is { } notificationError) island.ShowError(notificationError);
            else if (integrationWarning is { } warning) island.ShowError(warning);
            var reminderDispatcher = new ReminderDispatcher(taskService, taskStore, notifications, TimeProvider.System);
            scheduler = new(taskService, TimeProvider.System, reminderDispatcher);
            attachments.Changed += scheduler.RequestReconcile;
            scheduler.CycleCompleted += () => Dispatcher.BeginInvoke(CheckReminderStatus);
            scheduler.Failed += ex => Dispatcher.BeginInvoke(() => { WriteDiagnostic("scheduler", ex); island.ShowError("时间校准暂时失败，将在下次校准时重试。"); });
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.TimeChanged += OnSystemTimeChanged;
            scheduler.Start();
        }
        catch (Exception ex)
        {
            WriteDiagnostic("startup", ex);
            // Keep feedback in the island if the main window was already created.
            if (island is not null) { island.ShowError("本地日程加载失败，原数据已保留。请检查日志并重启。"); island.OpenTasks(false); }
            else Shutdown(1);
        }
    }

    private void Dispatch(Action action) => Dispatcher.BeginInvoke(action);
    private void OpenBackupDirectory()
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskIsland", "backups");
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception ex) { WriteDiagnostic("open-backups", ex); island?.ShowError("备份文件目录打开失败，请重试"); island?.OpenTasks(false); }
    }
    private async Task RouteNotificationAsync(string argument)
    {
        if (instance?.IsPrimary != false) { HandleActivation(argument); return; }
        try { await instance.ForwardAsync(argument, CancellationToken.None); }
        catch (Exception ex) { WriteDiagnostic("notification-forward", ex); }
        finally { Shutdown(); }
    }
    private async void HandleActivation(string argument)
    {
        if (!tasksReady || island is null)
        {
            if (pendingActivation is null || ActivationRequest.Parse(argument).TaskId is not null || ActivationRequest.Parse(pendingActivation).TaskId is null)
                pendingActivation = argument;
            return;
        }
        try
        {
            var request = TaskIsland.Application.ActivationRequest.Parse(argument);
            await island.FocusNotificationAsync(request.TaskId);
            if (request.TaskId is null && argument is not "show" and not "action=show") island.ShowError("通知参数无效，已打开日程岛");
        }
        catch (Exception ex) { WriteDiagnostic("notification-route", ex); island.ShowError("日程定位失败，请重试"); }
    }
    private async void CheckReminderStatus()
    {
        if (taskStore is null || Dispatcher.HasShutdownStarted) return;
        try
        {
            if (attachments is not null && !await attachments.RetryCleanupAsync()) island?.ShowError("部分附件副本仍在等待清理");
            var failed = await taskStore.ReadAsync(tx => tx.ListActive().Any(item =>
                tx.FindReminder(item.Id, item.ReminderRevision) is { Status: ReminderStatus.Failed, Attempts: >= 3 }));
            if (failed) island?.ShowError("部分提醒提交失败，请检查系统通知设置");
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { WriteDiagnostic("notification-status", ex); }
    }

    internal static void WriteDiagnostic(string operation, Exception ex)
        => WriteEvent($"{operation} {ex.GetType().Name} 0x{ex.HResult:X8}" +
            (ex is Microsoft.Data.Sqlite.SqliteException sqlite ? $" sqlite={sqlite.SqliteErrorCode}/{sqlite.SqliteExtendedErrorCode}" : ""));

    private static void WriteEvent(string message)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskIsland", "logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "integration.log"), $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.TimeChanged -= OnSystemTimeChanged;
        tray?.Dispose();
        instance?.Dispose();
        try { scheduler?.DisposeAsync().AsTask().GetAwaiter().GetResult(); attachments?.WaitForIdleAsync().GetAwaiter().GetResult(); taskStore?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        catch (Exception ex) { WriteDiagnostic("data-shutdown", ex); }
        try { notifications?.Dispose(); } catch (Exception ex) { WriteDiagnostic("notification-unregister", ex); }
        base.OnExit(e);
    }
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) scheduler?.RequestReconcile(); }
    private void OnSystemTimeChanged(object? sender, EventArgs e) => scheduler?.RequestReconcile();
}
