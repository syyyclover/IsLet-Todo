using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskIsland.Presentation;
using Xunit;

public class ImeInputTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompositionKeepsContentOpenUntilCommitThenChecksPointer(bool outside) => IslandMotionTests.RunSta(window =>
    {
        window.OpenTasks(false); IslandMotionTests.Pump(300);
        var input = (TextBox)window.FindName("TitleInput");
        var content = (FrameworkElement)window.FindName("ContentPanel");
        var composition = new TextComposition(InputManager.Current, input, "中文");
        input.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputStartEvent });
        window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
        Assert.True(content.IsHitTestVisible);
        input.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
        IslandMotionTests.Pump(350);
        Assert.Equal(!outside, content.IsHitTestVisible);
    }, pointerOutside: outside);

    [Fact] public void NativeCancellationRestoresLeaveBehaviorAndEscapeDoesNotCollapseComposition() => IslandMotionTests.RunSta(window =>
    {
        window.OpenTasks(false); IslandMotionTests.Pump(300);
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        SendMessage(handle, 0x010D, 0, 0);
        window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.True(((FrameworkElement)window.FindName("ContentPanel")).IsHitTestVisible);
        SendMessage(handle, 0x010E, 0, 0);
        IslandMotionTests.Pump(350);
        Assert.False(((FrameworkElement)window.FindName("ContentPanel")).IsHitTestVisible);
    });

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);

    [Fact] public void SyntheticLeaveWithPointerInsideDoesNotCollapse() => IslandMotionTests.RunSta(window =>
    {
        window.OpenTasks(false); IslandMotionTests.Pump(300);
        window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
        Assert.True(((FrameworkElement)window.FindName("ContentPanel")).IsHitTestVisible);
    }, pointerOutside: false);
}
