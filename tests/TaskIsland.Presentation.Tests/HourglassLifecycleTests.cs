using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TaskIsland.Presentation;
using Xunit;

public class HourglassLifecycleTests
{
    [Fact]
    public void CountdownStopsOnCollapseDisableAndUnload() => RunSta(() =>
    {
        var control = new HourglassControl { IsRunning = true, ArchiveAtUtc = DateTimeOffset.UtcNow.AddHours(24) };
        var host = new Window { Width = 80, Height = 80, Content = control, ShowInTaskbar = false };
        try
        {
            host.Show();
            IslandMotionTests.Pump(30);
            Assert.True(Timer(control, "refreshTimer").IsEnabled);
            control.IsRunning = false;
            Assert.False(Timer(control, "refreshTimer").IsEnabled);
            Assert.False(Timer(control, "flipTimer").IsEnabled);
            control.IsRunning = true;
            control.Visibility = Visibility.Collapsed;
            Assert.False(Timer(control, "refreshTimer").IsEnabled);
            control.Visibility = Visibility.Visible;
            control.IsEnabled = false;
            Assert.False(Timer(control, "refreshTimer").IsEnabled);
            control.IsEnabled = true;
            host.Content = null;
            IslandMotionTests.Pump(30);
            Assert.False(Timer(control, "refreshTimer").IsEnabled);
            Assert.False(Timer(control, "flipTimer").IsEnabled);
        }
        finally { host.Close(); }
    });

    [Fact]
    public void ReducedAnimationKeepsCountdownAndRemovesRenewalClocks() => RunSta(() =>
    {
        var control = new HourglassControl { IsRunning = true, AnimationsEnabled = false, ArchiveAtUtc = DateTimeOffset.UtcNow.AddHours(24) };
        var host = new Window { Width = 80, Height = 80, Content = control, ShowInTaskbar = false };
        try
        {
            host.Show();
            IslandMotionTests.Pump(30);
            control.PulseRenewal();
            Assert.True(Timer(control, "refreshTimer").IsEnabled);
            Assert.False(Timer(control, "flipTimer").IsEnabled);
            var transforms = (TransformGroup)control.RenderTransform;
            Assert.False(((RotateTransform)transforms.Children[1]).HasAnimatedProperties);
            Assert.False(((ScaleTransform)transforms.Children[0]).HasAnimatedProperties);
        }
        finally { host.Close(); }
    });

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static DispatcherTimer Timer(HourglassControl control, string name) =>
        (DispatcherTimer)typeof(HourglassControl).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
}
