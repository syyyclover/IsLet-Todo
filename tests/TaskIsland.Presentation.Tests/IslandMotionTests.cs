using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using TaskIsland.Presentation;
using TaskIsland.WindowsIntegration;
using Xunit;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

public class IslandMotionTests(ITestOutputHelper output)
{
    [Fact]
    public void ExpandingDoesNotResizeNativeWindowEveryFrame() => RunSta(window =>
    {
        CaptureIfRequested(window, "collapsed");
        var sizeChanges = 0;
        window.SizeChanged += (_, _) => sizeChanges++;
        window.OpenTasks(false);
        Pump(90);
        CaptureIfRequested(window, "expanding");
        Pump(360);
        CaptureIfRequested(window, "expanded");
        output.WriteLine($"Expand native/layout size changes: {sizeChanges}");
        // WPF may report the width and height boundary allocation as separate notifications.
        Assert.InRange(sizeChanges, 0, 2);
    });

    [Fact]
    public void CollapseReleasesExpandedTransparentFootprint() => RunSta(window =>
    {
        window.OpenTasks(false);
        Pump(300);
        Escape(window);
        Pump(350);
        Assert.Equal(240, window.Width);
        Assert.Equal(40, window.Height);
        var clip = (System.Windows.Media.RectangleGeometry)window.FindName("IslandClip");
        Assert.Equal(new Rect(0, 0, 240, 40), clip.Rect);
    });

    [Fact]
    public void DisablingAnimationDuringTransitionSettlesImmediately() => RunSta(window =>
    {
        window.OpenTasks(false);
        Pump(60);
        ((IslandViewModel)window.DataContext).AnimationsEnabled = false;
        var clip = (System.Windows.Media.RectangleGeometry)window.FindName("IslandClip");
        Assert.Equal(new Rect(0, 0, 360, 295), clip.Rect);
        Escape(window);
        Assert.Equal(240, window.Width);
        Assert.Equal(40, window.Height);
    });

    private static void Escape(IslandWindow window) => window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(window)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    [Fact]
    public void CollapsingKeepsPageVisibleUntilContentHasFaded() => RunSta(window =>
    {
        window.OpenTasks(false);
        Pump(300);
        Escape(window);
        var page = (FrameworkElement)window.FindName("TasksPanel");
        Assert.Equal(Visibility.Visible, page.Visibility);
        Pump(50);
        var content = (FrameworkElement)window.FindName("ContentPanel");
        Assert.InRange(content.Opacity, 0.001, 0.999);
        Pump(300);
        Assert.Equal(Visibility.Hidden, content.Visibility);
    });

    [Fact]
    public void ReversingCollapseFinishesExpandedWithoutStaleShrink() => RunSta(window =>
    {
        window.OpenTasks(false);
        Pump(300);
        window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(window)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Pump(70);
        window.OpenTasks(false);
        Pump(350);
        Assert.Equal(360, window.Width);
        Assert.Equal(295, window.Height);
    });

    private static void RunSta(Action<IslandWindow> check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            IslandWindow? window = null;
            try
            {
                window = new IslandWindow(new NotificationService());
                window.Show();
                Pump(300);
                check(window);
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                // There is no Application instance in the isolated WPF test host.
                window?.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(15))) throw new TimeoutException("WPF test dispatcher did not finish");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void CaptureIfRequested(IslandWindow window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("TASKISLAND_CAPTURE_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        System.IO.Directory.CreateDirectory(directory);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,
            (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(System.IO.Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }
}
