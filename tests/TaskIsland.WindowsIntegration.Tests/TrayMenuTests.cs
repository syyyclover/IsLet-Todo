using TaskIsland.WindowsIntegration;
using Xunit;

public class TrayMenuTests
{
    [Fact] public void MenuContainsOnlyExitAndBackupsAndInvokesCorrectActions()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var exited = 0; var opened = 0;
                using var menu = new TrayMenu(() => exited++, () => opened++);
                Assert.Equal(2, menu.Items.Count);
                Assert.Equal("退出", menu.Items[0].Text);
                Assert.Equal("打开备份文件目录", menu.Items[1].Text);
                menu.Items[0].PerformClick();
                Assert.Equal(1, exited); Assert.Equal(0, opened);
                menu.Items[1].PerformClick();
                Assert.Equal(1, exited); Assert.Equal(1, opened);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact] public void EmbeddedIconCanLoadAtTraySizesAndStaysSmall()
    {
        using var stream = typeof(TrayService).Assembly.GetManifestResourceStream("TaskIsland.WindowsIntegration.Assets.islet.ico");
        Assert.NotNull(stream); Assert.InRange(stream.Length, 1, 16 * 1024);
        using var small = new System.Drawing.Icon(stream, 16, 16);
        Assert.Equal(16, small.Width);
        stream.Position = 0;
        using var large = new System.Drawing.Icon(stream, 32, 32);
        Assert.Equal(32, large.Width);
    }
}
