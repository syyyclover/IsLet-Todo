using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskIsland.WindowsIntegration;

/// <summary>
/// Foreground fullscreen tracking without polling. Construct, refresh and dispose on the same
/// UI thread with a running Windows message loop (required by WINEVENT_OUTOFCONTEXT).
/// </summary>
public sealed class FullscreenMonitor : IDisposable
{
    private readonly nint islandWindow;
    private readonly WinEventProc callback;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private nint foregroundHook;
    private nint objectHook;
    private bool disposed;
    public bool IsFullscreen { get; private set; }
    public event EventHandler? FullscreenChanged;

    public FullscreenMonitor(nint islandWindow)
    {
        if (islandWindow == 0) throw new ArgumentException("需要有效的岛窗口句柄。", nameof(islandWindow));
        this.islandWindow = islandWindow;
        callback = OnWinEvent;
        // Do not skip our process: activating the island must clear an old fullscreen state.
        foregroundHook = SetWinEventHook(0x0003, 0x0003, 0, callback, 0, 0, 0);
        if (foregroundHook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        // Destroy/show/hide/location events for top-level windows; child events are filtered below.
        objectHook = SetWinEventHook(0x8001, 0x800B, 0, callback, 0, 0, 0);
        if (objectHook == 0)
        {
            var error = Marshal.GetLastWin32Error();
            UnhookWinEvent(foregroundHook);
            foregroundHook = 0;
            throw new Win32Exception(error);
        }
        Refresh();
    }

    public void Refresh()
    {
        VerifyThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        var fullscreen = EvaluateForeground();
        if (IsFullscreen == fullscreen) return;
        IsFullscreen = fullscreen;
        FullscreenChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool EvaluateForeground()
    {
        var foreground = GetForegroundWindow();
        if (foreground == 0 || foreground == islandWindow || foreground == GetShellWindow() || foreground == GetDesktopWindow() ||
            !IsWindowVisible(foreground) || IsIconic(foreground)) return false;
        GetWindowThreadProcessId(foreground, out var process);
        if (process == (uint)Environment.ProcessId) return false;
        var className = new StringBuilder(256);
        GetClassName(foreground, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        var monitor = MonitorFromWindow(islandWindow, 2); // nearest monitor, also while island is hidden
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info)) return false;
        // DWM visible frame excludes resize borders that could otherwise make a maximized app look fullscreen.
        if (DwmGetWindowAttribute(foreground, 9, out var rect, Marshal.SizeOf<NativeRect>()) < 0 && !GetWindowRect(foreground, out rect)) return false;
        return CoversMonitor(rect.ToRectangle(), info.Monitor.ToRectangle());
    }

    public static bool CoversMonitor(Rectangle windowBounds, Rectangle monitorBounds) =>
        windowBounds.Width > 0 && windowBounds.Height > 0 && monitorBounds.Width > 0 && monitorBounds.Height > 0 &&
        windowBounds.Left <= monitorBounds.Left && windowBounds.Top <= monitorBounds.Top &&
        windowBounds.Right >= monitorBounds.Right && windowBounds.Bottom >= monitorBounds.Bottom;

    private void OnWinEvent(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint timestamp)
    {
        if (disposed) return;
        if (eventType != 0x0003)
        {
            if (objectId != 0 || childId != 0 || (window != GetForegroundWindow() && window != islandWindow)) return;
            if (eventType is not (0x8001 or 0x8002 or 0x8003 or 0x800B)) return;
        }
        // An exception must never unwind through the unmanaged accessibility callback.
        try { Refresh(); }
        catch (Exception) { System.Diagnostics.Trace.TraceError("Fullscreen state callback failed."); }
    }

    public void Dispose()
    {
        VerifyThread();
        if (disposed) return;
        disposed = true;
        if (foregroundHook != 0) { UnhookWinEvent(foregroundHook); foregroundHook = 0; }
        if (objectHook != 0) { UnhookWinEvent(objectHook); objectHook = 0; }
        FullscreenChanged = null;
        GC.KeepAlive(callback);
    }

    private void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != ownerThread) throw new InvalidOperationException("全屏监听必须在创建它的 UI 线程操作。");
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void WinEventProc(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint timestamp);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern nint GetDesktopWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder className, int max);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, uint attribute, out NativeRect rect, int size);
}
