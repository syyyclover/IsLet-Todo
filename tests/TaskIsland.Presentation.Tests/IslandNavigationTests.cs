using System.Windows;
using System.Windows.Media;
using Xunit;

public class IslandNavigationTests
{
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
