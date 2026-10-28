using System.Runtime.InteropServices;
using TaskIsland.Application;

namespace TaskIsland.WindowsIntegration;

public static class DesktopPlacement
{
    public static WorkArea PrimaryWorkAreaInDips(nint hwnd)
    {
        // MonitorFromPoint(0,0) selects the primary monitor, including a nonzero work-area origin.
        var monitor = MonitorFromPoint(default, 1);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) throw new System.ComponentModel.Win32Exception();
        var scale = GetDpiForWindow(hwnd) / 96d;
        if (scale <= 0) scale = 1;
        return new(info.Work.Left / scale, info.Work.Top / scale,
            (info.Work.Right - info.Work.Left) / scale, (info.Work.Bottom - info.Work.Top) / scale);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
}
