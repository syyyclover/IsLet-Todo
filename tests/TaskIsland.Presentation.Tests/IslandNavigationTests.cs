using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Input;
using TaskIsland.Presentation;
using Xunit;

public class IslandNavigationTests
{
    private static void Hover(FrameworkElement element) => element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });

    [Fact] public void HoverTabsSwitchImmediatelyAndLatestPageWinsDuringSlides() => IslandMotionTests.RunSta(window =>
    {
        window.OpenTasks(false); IslandMotionTests.Pump(300);
        Hover((FrameworkElement)window.FindName("ArchiveTab"));
        var archive = (FrameworkElement)window.FindName("ArchivePanel");
        Assert.Equal(Visibility.Visible, archive.Visibility);
        IslandMotionTests.Pump(45);
        Assert.True(((TranslateTransform)archive.RenderTransform).X > 0);
        Hover((FrameworkElement)window.FindName("SettingsTab"));
        IslandMotionTests.Pump(45);
        Hover((FrameworkElement)window.FindName("TasksTab"));
        IslandMotionTests.Pump(300);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("TasksPanel")).Visibility);
        Assert.Equal(Visibility.Collapsed, archive.Visibility);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("SettingsPanel")).Visibility);
    });

    [Fact] public void TabClickStillWorksWithoutConfirmationOrDiscardingDraft() => IslandMotionTests.RunSta(window =>
    {
        var model = (IslandViewModel)window.DataContext;
        model.EditorTitle = "保留的草稿";
        window.OpenTasks(false); IslandMotionTests.Pump(300);
        var tab = (Button)window.FindName("SettingsTab");
        tab.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        IslandMotionTests.Pump(300);
        Assert.Equal("保留的草稿", model.EditorTitle);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("SettingsPanel")).Visibility);
    });

    [Fact] public async Task HoverAwayFromEditorKeepsDraftAndTimeSelectionForResume()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TaskIsland.Hover.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = new TaskIsland.Infrastructure.SqliteTaskStore(root);
            await store.InitializeAsync();
            var service = new TaskIsland.Application.TaskService(store, TimeProvider.System);
            IslandMotionTests.RunSta(window =>
            {
                var loading = window.AttachTasksAsync(service);
                var timeout = Environment.TickCount64 + 5000;
                while (!loading.IsCompleted && Environment.TickCount64 < timeout) IslandMotionTests.Pump(10);
                Assert.True(loading.IsCompleted); loading.GetAwaiter().GetResult();
                var model = (IslandViewModel)window.DataContext;
                model.AddCommand.Execute(null); model.EditorTitle = "尚未保存";
                model.EditorTime.OpenCommand.Execute(null);
                model.EditorTime.Minute = 47;
                IslandMotionTests.Pump(300);
                Hover((FrameworkElement)window.FindName("ArchiveTab"));
                IslandMotionTests.Pump(300);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("ArchivePanel")).Visibility);
                Assert.Equal("尚未保存", model.EditorTitle);
                Assert.Equal(47, model.EditorTime.Minute);
                Assert.False(model.ConfirmDiscard);
                Hover((FrameworkElement)window.FindName("TasksTab"));
                IslandMotionTests.Pump(300);
                model.AddCommand.Execute(null); IslandMotionTests.Pump(300);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("EditorViewport")).Visibility);
                Assert.Equal("尚未保存", model.EditorTitle);
                Assert.Equal(47, model.EditorTime.Minute);
            });
        }
        finally { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LeavingPeerPageReopensTasks(bool settings) => IslandMotionTests.RunSta(window =>
    {
        if (settings) window.OpenSettings(); else window.OpenArchive();
        IslandMotionTests.Pump(300);
        window.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
        Assert.False(((FrameworkElement)window.FindName("ContentPanel")).IsHitTestVisible);
        IslandMotionTests.Pump(300);
        window.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
        IslandMotionTests.Pump(300);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("TasksPanel")).Visibility);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("SettingsPanel")).Visibility);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("ArchivePanel")).Visibility);
    });
    [Fact] public void PointerStartsOpenAndCloseImmediatelyWithoutChangingMotionDuration() => IslandMotionTests.RunSta(window =>
    {
        var content = (FrameworkElement)window.FindName("ContentPanel");
        window.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
        Assert.True(content.IsHitTestVisible); Assert.Equal(Visibility.Visible, content.Visibility);
        IslandMotionTests.Pump(300);
        window.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
        Assert.False(content.IsHitTestVisible);
        Assert.Equal(Visibility.Visible, content.Visibility);
        IslandMotionTests.Pump(300); Assert.Equal(Visibility.Hidden, content.Visibility);
    });
    [Fact] public void PeerPagesKeepTabsAndSlideWithoutBackHierarchy() => IslandMotionTests.RunSta(window =>
    {
        window.OpenTasks(false); IslandMotionTests.Pump(300);
        window.OpenArchive(); IslandMotionTests.Pump(60);
        var archive = (FrameworkElement)window.FindName("ArchivePanel");
        Assert.IsType<TranslateTransform>(archive.RenderTransform);
        Assert.True(((TranslateTransform)archive.RenderTransform).X > 0);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("PageTabs")).Visibility);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("BackButton")).Visibility);
        window.OpenSettings(); IslandMotionTests.Pump(300);
        Assert.Equal(Visibility.Collapsed, archive.Visibility);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("SettingsPanel")).Visibility);
        window.OpenTasks(false); IslandMotionTests.Pump(300);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("TasksPanel")).Visibility);
    });
}
