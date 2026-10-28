using Microsoft.Win32;

namespace TaskIsland.WindowsIntegration;

/// <summary>Current-user login registration. A supplied key is borrowed and remains caller-owned.</summary>
public sealed class StartupRegistration
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TaskIsland";
    private readonly string command;
    private readonly RegistryKey? registrationKey;

    public StartupRegistration(string executablePath, RegistryKey? registrationKey = null)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || executablePath.Any(char.IsControl) || executablePath.Contains('"') ||
            !Path.IsPathFullyQualified(executablePath) || !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("自启动需要有效的应用程序绝对路径。", nameof(executablePath));
        command = $"\"{Path.GetFullPath(executablePath)}\"";
        // Windows documents a 260-character limit for Run command lines.
        if (command.Length > 260) throw new ArgumentException("应用程序路径过长，无法注册自启动。", nameof(executablePath));
        this.registrationKey = registrationKey;
    }

    public bool IsEnabled
    {
        get
        {
            if (registrationKey is not null) return Matches(registrationKey.GetValue(ValueName));
            using var key = Registry.CurrentUser.OpenSubKey(RunPath);
            return Matches(key?.GetValue(ValueName));
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (registrationKey is not null) { Update(registrationKey, enabled); return; }
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunPath, true);
            Update(key, true);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunPath, true);
            if (key is not null) Update(key, false);
        }
    }

    private bool Matches(object? value) => value is string text && string.Equals(text, command, StringComparison.OrdinalIgnoreCase);
    private void Update(RegistryKey key, bool enabled)
    {
        if (enabled) key.SetValue(ValueName, command, RegistryValueKind.String);
        else key.DeleteValue(ValueName, false);
    }
}
