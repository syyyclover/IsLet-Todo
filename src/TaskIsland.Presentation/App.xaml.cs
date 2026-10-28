using System.IO;
using System.Diagnostics;
using System.Windows;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using TaskIsland.WindowsIntegration;

namespace TaskIsland.Presentation;

public partial class App : System.Windows.Application
{
    private SingleInstance? instance;
    private NotificationService? notifications;
    private TrayService? tray;
    private IslandWindow? island;

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
                instance.Failed += ex => Dispatcher.BeginInvoke(() => { WriteDiagnostic("ipc", ex); island?.ShowStatus("启动转发不可用，请退出后重新启动。"); });
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
                () => Dispatch(() => island.OpenSettings()), () => Dispatch(() => island.OpenArchive()),
                () => Dispatch(Shutdown));
            island.Show();
            WriteEvent($"startup.window {startupWatch.ElapsedMilliseconds}ms");
            if (argument != "show") HandleActivation(argument);
        }
        catch (Exception ex)
        {
            WriteDiagnostic("startup", ex);
            // Keep feedback in the island if the main window was already created.
            if (island is not null) island.ShowStatus("启动集成失败，请查看日志后重试。");
            else Shutdown(1);
        }
    }

    private void Dispatch(Action action) => Dispatcher.BeginInvoke(action);
    private async Task RouteNotificationAsync(string argument)
    {
        if (instance?.IsPrimary != false) { HandleActivation(argument); return; }
        try { await instance.ForwardAsync(argument, CancellationToken.None); }
        catch (Exception ex) { WriteDiagnostic("notification-forward", ex); }
        finally { Shutdown(); }
    }
    private void HandleActivation(string argument)
    {
        if (island is null) return;
        island.OpenTasks(true);
        if (argument != "show")
        {
            var request = TaskIsland.Application.ActivationRequest.Parse(argument);
            island.ShowStatus(request.TaskId.HasValue ? "通知激活成功；任务定位将在任务阶段接入。" : "通知参数无效，已打开日程岛。");
        }
    }

    internal static void WriteDiagnostic(string operation, Exception ex)
        => WriteEvent($"{operation} {ex.GetType().Name} 0x{ex.HResult:X8}");

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
        tray?.Dispose();
        instance?.Dispose();
        try { notifications?.Dispose(); } catch (Exception ex) { WriteDiagnostic("notification-unregister", ex); }
        base.OnExit(e);
    }
}
