using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.ExceptionServices;
using System.Text;

namespace TaskIsland.WindowsIntegration;

/// <summary>Current-user Shell identity. Does not change Windows notification preferences.</summary>
public static class NotificationIdentity
{
    public const string AppUserModelId = "TaskIsland.Desktop";
    private static readonly PropertyKey AppIdKey = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
    public sealed record ShortcutMetadata(string TargetPath, string? AppUserModelId);

    public static string EnsureRegistered(string executablePath, string? shortcutDirectory = null, bool setProcessIdentity = true)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || executablePath.Contains('"') || executablePath.Any(char.IsControl) ||
            !Path.IsPathFullyQualified(executablePath) || !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("通知身份需要有效的应用程序绝对路径。", nameof(executablePath));
        executablePath = Path.GetFullPath(executablePath);
        if (!File.Exists(executablePath)) throw new FileNotFoundException("应用程序不存在。");
        var directory = shortcutDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("快捷方式目录必须为绝对路径。", nameof(shortcutDirectory));
        var shortcut = Path.Combine(directory, "IsLet-Todo.lnk");
        RunSta(() =>
        {
            Directory.CreateDirectory(directory);
            var existed = File.Exists(shortcut);
            if (existed && ReadShortcutCore(shortcut).AppUserModelId != AppUserModelId)
                throw new InvalidOperationException("同名快捷方式不属于日程岛，未覆盖。");
            var link = CreateLink();
            var temporary = Path.Combine(directory, $"TaskIsland-{Guid.NewGuid():N}.lnk");
            try
            {
                link.SetPath(executablePath); link.SetWorkingDirectory(Path.GetDirectoryName(executablePath)!);
                link.SetDescription("IsLet-Todo 日程岛"); link.SetIconLocation(executablePath, 0);
                var value = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(AppUserModelId) };
                try { var store = (IPropertyStore)link; var key = AppIdKey; store.SetValue(ref key, ref value); store.Commit(); }
                finally { PropVariantClear(ref value); }
                ((IPersistFile)link).Save(temporary, true);
                File.Move(temporary, shortcut, existed);
            }
            finally { Marshal.FinalReleaseComObject(link); if (File.Exists(temporary)) File.Delete(temporary); }
            return true;
        });
        // Must run before AppNotificationManager.Default captures the calling process identity.
        if (setProcessIdentity) Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppUserModelId));
        return shortcut;
    }
    public static ShortcutMetadata ReadShortcut(string path) => RunSta(() => ReadShortcutCore(path));
    private static ShortcutMetadata ReadShortcutCore(string path)
    {
        var link = CreateLink();
        try
        {
            ((IPersistFile)link).Load(path, 0);
            var target = new StringBuilder(32768); link.GetPath(target, target.Capacity, IntPtr.Zero, 4);
            var key = AppIdKey; ((IPropertyStore)link).GetValue(ref key, out var value);
            try { return new(target.ToString(), value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : null); }
            finally { PropVariantClear(ref value); }
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }
    private static IShellLinkW CreateLink() => (IShellLinkW)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), true)!)!;
    private static T RunSta<T>(Func<T> action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) return action();
        T result = default!; Exception? failure = null;
        var thread = new Thread(() => { try { result = action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey(Guid format, uint id) { public Guid Format = format; public uint Id = id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count); void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value); void SetValue(ref PropertyKey key, ref PropVariant value); void Commit();
    }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int count, IntPtr findData, uint flags);
        void GetIDList(out IntPtr id); void SetIDList(IntPtr id);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey); void SetHotkey(short hotkey); void GetShowCmd(out int show); void SetShowCmd(int show);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags); void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
