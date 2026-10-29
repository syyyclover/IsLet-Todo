using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TaskIsland.WindowsIntegration;

public static class TrayWindowStyle
{
    public static void Apply(nint window)
    {
        var current = GetWindowLongPtr(window, -20).ToInt64();
        Marshal.SetLastPInvokeError(0);
        var previous = SetWindowLongPtr(window, -20, (nint)((current | 0x80L) & ~0x40000L));
        if (previous == 0 && Marshal.GetLastPInvokeError() is var error && error != 0) throw new Win32Exception(error);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
}
