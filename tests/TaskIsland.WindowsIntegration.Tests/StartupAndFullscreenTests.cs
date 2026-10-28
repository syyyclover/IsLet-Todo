using System.Drawing;
using Microsoft.Win32;
using TaskIsland.WindowsIntegration;
using Xunit;

public class StartupAndFullscreenTests
{
    [Fact]
    public void StartupWritesQuotedExecutableAndRemovesOnlyItsValue()
    {
        var path = @"Software\TaskIsland.Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path);
            key.SetValue("Unrelated", "preserve");
            var startup = new StartupRegistration(@"C:\Program Files\Task Island\TaskIsland.exe", key);
            Assert.False(startup.IsEnabled);
            key.SetValue("TaskIsland", "\"C:\\Old\\TaskIsland.exe\"");
            Assert.False(startup.IsEnabled);
            startup.SetEnabled(true);
            Assert.Equal("\"C:\\Program Files\\Task Island\\TaskIsland.exe\"", key.GetValue("TaskIsland"));
            Assert.True(startup.IsEnabled);
            startup.SetEnabled(false);
            Assert.False(startup.IsEnabled);
            Assert.Equal("preserve", key.GetValue("Unrelated"));
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
    }

    [Theory]
    [InlineData("TaskIsland.exe")]
    [InlineData("C:\\App\\bad\"name.exe")]
    [InlineData("C:\\App\\TaskIsland.exe\r\n")]
    [InlineData("C:\\App\\TaskIsland.dll")]
    public void InvalidExecutablePathIsRejected(string path) => Assert.Throws<ArgumentException>(() => new StartupRegistration(path));

    [Theory]
    [InlineData(0, 0, 1920, 1080, true)]
    [InlineData(0, 0, 1920, 1040, false)]
    [InlineData(1920, 0, 1920, 1080, false)]
    [InlineData(-1920, 0, 3840, 1080, true)]
    [InlineData(0, 0, 0, 1080, false)]
    public void FullscreenRequiresCoverageOfEntireTargetMonitor(int x, int y, int width, int height, bool expected) =>
        Assert.Equal(expected, FullscreenMonitor.CoversMonitor(new Rectangle(x, y, width, height), new Rectangle(0, 0, 1920, 1080)));

    [Fact]
    public void FullscreenSupportsNegativeMonitorCoordinates() => Assert.True(FullscreenMonitor.CoversMonitor(
        new Rectangle(-1920, -200, 1920, 1080), new Rectangle(-1920, -200, 1920, 1080)));
}
