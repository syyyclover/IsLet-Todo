using System.Runtime.InteropServices;
using System.Windows.Interop;
using Xunit;

public class TrayWindowTests
{
    [Fact] public void NativeWindowUsesToolWindowStyleWithoutAppWindowStyle() => IslandMotionTests.RunSta(window =>
    {
        var style = GetWindowLongPtr(new WindowInteropHelper(window).Handle, -20).ToInt64();
        Assert.True((style & 0x80) != 0, "Tool windows are excluded from Alt+Tab");
        Assert.Equal(0, style & 0x40000);
        Assert.False(window.ShowInTaskbar);
    });

    [Fact] public void ShellCloseCollapsesInsteadOfClosingResidentWindow() => IslandMotionTests.RunSta(window =>
    {
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.OpenTasks(false); IslandMotionTests.Pump(300);
        SendMessage(new WindowInteropHelper(window).Handle, 0x0010, 0, 0);
        IslandMotionTests.Pump(300);
        Assert.False(closed);
        Assert.True(window.IsVisible);
        Assert.False(((System.Windows.FrameworkElement)window.FindName("ContentPanel")).IsHitTestVisible);
    });

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessage(nint hwnd, int message, nint wParam, nint lParam);
}
