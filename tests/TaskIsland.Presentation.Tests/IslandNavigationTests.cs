using System.Windows;
using System.Windows.Media;
using Xunit;

public class IslandNavigationTests
{
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
