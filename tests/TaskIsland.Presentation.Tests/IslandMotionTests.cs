using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
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
    public void OpeningAndClosingKeepNativeSurfaceSizeStable() => RunSta(window =>
    {
        var initial = NativeBounds(window);
        var changes = 0;
        window.SizeChanged += (_, _) => changes++;
        window.OpenTasks(false);
        Pump(300);
        Escape(window);
        Pump(350);
        Assert.Equal(0, changes);
        Assert.Equal(initial, NativeBounds(window));
    });

    [Theory]
    [InlineData("Left", 0)]
    [InlineData("Center", 60)]
    [InlineData("Right", 120)]
    public void CollapsedAnchorsStayInPlaceAcrossOpenClose(string anchor, double expectedInset) => RunSta(window =>
    {
        window.OpenSettings();
        var panel = (System.Windows.Controls.StackPanel)window.FindName("SettingsPanel");
        var buttons = (System.Windows.Controls.StackPanel)panel.Children[1];
        var button = buttons.Children.OfType<System.Windows.Controls.Button>().Single(item => (string)item.Tag == anchor);
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, button));
        window.OpenTasks(false);
        Pump(300);
        Escape(window);
        Pump(300);
        var clip = (System.Windows.Media.RectangleGeometry)window.FindName("IslandClip");
        Assert.Equal(expectedInset, clip.Rect.X);
        var before = window.PointToScreen(clip.Rect.TopLeft);
        window.OpenTasks(false);
        Pump(300);
        Escape(window);
        Pump(300);
        Assert.Equal(before, window.PointToScreen(clip.Rect.TopLeft));
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompletingMotionNeverPublishesOppositeShape(bool expanding) => RunSta(window =>
    {
        if (!expanding) { window.OpenTasks(false); Pump(300); }
        var clip = (System.Windows.Media.RectangleGeometry)window.FindName("IslandClip");
        var previous = clip.Rect.Width;
        var reversed = false;
        clip.Changed += (_, _) =>
        {
            var current = clip.Rect.Width;
            if (expanding ? current + 0.01 < previous : current - 0.01 > previous) reversed = true;
            previous = current;
        };
        if (expanding) window.OpenTasks(false); else Escape(window);
        Pump(400);
        Assert.False(reversed, "Animation endpoint briefly exposed the old base shape");
    });

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
    public void CollapseLeavesOnlySmallVisibleFootprint() => RunSta(window =>
    {
        window.OpenTasks(false);
        Pump(300);
        Escape(window);
        Pump(350);
        Assert.Equal(360, window.Width);
        Assert.Equal(295, window.Height);
        var clip = (System.Windows.Media.RectangleGeometry)window.FindName("IslandClip");
        Assert.Equal(new Rect(60, 0, 240, 40), clip.Rect);
        var bitmap = Render(window);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(5, 150, 1, 1), pixel, 4, 0);
        Assert.Equal(0, pixel[3]); // Unpainted margin remains alpha-zero for layered hit testing.
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
        Assert.Equal(new Rect(60, 0, 240, 40), clip.Rect);
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
        var clip = (System.Windows.Media.RectangleGeometry)window.FindName("IslandClip");
        Assert.Equal(new Rect(0, 0, 360, 295), clip.Rect);
        var content = (FrameworkElement)window.FindName("ContentPanel");
        Assert.Equal(Visibility.Visible, content.Visibility);
        Assert.Equal(1, content.Opacity);
    });

    [Fact]
    public void ReversingExpansionFinishesCollapsedWithoutStaleReveal() => RunSta(window =>
    {
        window.OpenTasks(false);
        Pump(70);
        Escape(window);
        Pump(350);
        var clip = (System.Windows.Media.RectangleGeometry)window.FindName("IslandClip");
        Assert.Equal(new Rect(60, 0, 240, 40), clip.Rect);
        var content = (FrameworkElement)window.FindName("ContentPanel");
        Assert.Equal(Visibility.Hidden, content.Visibility);
        Assert.Equal(0, content.Opacity);
    });

    internal static void RunSta(Action<IslandWindow> check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            IslandWindow? window = null;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                window = new IslandWindow(new NotificationService());
                window.Show();
                // Native pointer traffic from the user's desktop must not reverse synthetic test motions.
                // Explicit routed events and native close/style tests remain available below.
                HwndSource.FromHwnd(new WindowInteropHelper(window).Handle).AddHook(IgnoreExternalPointer);
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

    private static nint IgnoreExternalPointer(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0084) { handled = true; return -1; }
        return 0;
    }

    internal static void Pump(int milliseconds)
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
        var bitmap = Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(System.IO.Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }

    private static System.Windows.Media.Imaging.RenderTargetBitmap Render(IslandWindow window)
    {
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,
            (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        return bitmap;
    }

    private static Rect NativeBounds(IslandWindow window)
    {
        Assert.True(GetWindowRect(new WindowInteropHelper(window).Handle, out var bounds));
        return new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint handle, out NativeRect bounds);
}
