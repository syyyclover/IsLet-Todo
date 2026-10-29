using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TaskIsland.Presentation;
using Xunit;

public class OperationMotionTests
{
    [Theory]
    [InlineData(OperationCue.Complete)]
    [InlineData(OperationCue.Archive)]
    [InlineData(OperationCue.Renew)]
    [InlineData(OperationCue.Pin)]
    [InlineData(OperationCue.Restore)]
    [InlineData(OperationCue.Save)]
    [InlineData(OperationCue.Delete)]
    public void CompletionRestoresOriginalVisualState(OperationCue cue) => RunSta(() =>
    {
        var original = new TranslateTransform(2, 3);
        var target = new Border { Width = 80, Height = 30, Opacity = .7, RenderTransform = original, RenderTransformOrigin = new Point(.2, .3) };
        var host = new Window { Width = 150, Height = 100, Content = target, ShowInTaskbar = false };
        try
        {
            host.Show(); IslandMotionTests.Pump(20);
            var task = OperationMotion.PlayAsync(target, cue, true);
            if (SystemParameters.ClientAreaAnimation) Assert.NotSame(original, target.RenderTransform);
            IslandMotionTests.Pump(300);
            Assert.True(task.IsCompletedSuccessfully);
            Assert.Same(original, target.RenderTransform);
            Assert.Equal(new Point(.2, .3), target.RenderTransformOrigin);
            Assert.Equal(.7, target.Opacity, 8);
            Assert.False(target.HasAnimatedProperties);
            Assert.Equal(150, host.Width);
            Assert.Equal(100, host.Height);
        }
        finally { host.Close(); }
    });

    [Fact]
    public void DisabledMotionIsImmediateAndLeavesVisualsUntouched() => RunSta(() =>
    {
        var original = new TranslateTransform(4, 1);
        var target = new Border { RenderTransform = original, Opacity = .6 };
        var task = OperationMotion.PlayAsync(target, OperationCue.Delete, false);
        Assert.True(task.IsCompletedSuccessfully);
        Assert.Same(original, target.RenderTransform);
        Assert.Equal(.6, target.Opacity);
        Assert.False(target.HasAnimatedProperties);
    });

    [Fact]
    public void UnloadAndInterruptionCompleteOldTasksAndRestoreState() => RunSta(() =>
    {
        var original = new ScaleTransform(.9, .9);
        var target = new Border { RenderTransform = original, Opacity = .8 };
        var host = new Window { Width = 150, Height = 100, Content = target, ShowInTaskbar = false };
        try
        {
            host.Show(); IslandMotionTests.Pump(20);
            var first = OperationMotion.PlayAsync(target, OperationCue.Archive, true);
            var second = OperationMotion.PlayAsync(target, OperationCue.Renew, true);
            Assert.True(first.IsCompletedSuccessfully);
            host.Content = null; IslandMotionTests.Pump(30);
            Assert.True(second.IsCompletedSuccessfully);
            Assert.Same(original, target.RenderTransform);
            Assert.Equal(.8, target.Opacity, 8);
            Assert.False(target.HasAnimatedProperties);
        }
        finally { host.Close(); }
    });

    [Fact]
    public void MotionPreservesBindingsAndDefaultPropertySources() => RunSta(() =>
    {
        var source = new Border { RenderTransform = new TranslateTransform(2, 1), Opacity = .75 };
        var target = new Border();
        target.SetBinding(UIElement.RenderTransformProperty, new System.Windows.Data.Binding("RenderTransform") { Source = source });
        target.SetBinding(UIElement.OpacityProperty, new System.Windows.Data.Binding("Opacity") { Source = source });
        var host = new Window { Width = 150, Height = 100, Content = target, ShowInTaskbar = false };
        try
        {
            host.Show(); IslandMotionTests.Pump(20);
            var task = OperationMotion.PlayAsync(target, OperationCue.Complete, true);
            IslandMotionTests.Pump(300);
            Assert.True(task.IsCompletedSuccessfully);
            Assert.True(System.Windows.Data.BindingOperations.IsDataBound(target, UIElement.RenderTransformProperty));
            Assert.True(System.Windows.Data.BindingOperations.IsDataBound(target, UIElement.OpacityProperty));
            Assert.Equal(DependencyProperty.UnsetValue, target.ReadLocalValue(UIElement.RenderTransformOriginProperty));
            source.RenderTransform = new TranslateTransform(8, 4);
            source.Opacity = .4;
            IslandMotionTests.Pump(20);
            Assert.Same(source.RenderTransform, target.RenderTransform);
            Assert.Equal(.4, target.Opacity);
        }
        finally { host.Close(); }
    });

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
