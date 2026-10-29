using Microsoft.Win32;

namespace TaskIsland.WindowsIntegration;

public static class NotificationPreferenceGuard
{
    public static bool IsLegacyDisabled(string executablePath)
    {
        using var identities = Registry.CurrentUser.OpenSubKey(@"Software\Classes\AppUserModelId");
        using var settings = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings");
        return identities is not null && settings is not null && IsLegacyDisabled(executablePath, identities, settings);
    }

    // Read only: creating a stable identity must not bypass an earlier application opt-out.
    public static bool IsLegacyDisabled(string executablePath, RegistryKey identities, RegistryKey settings)
    {
        using var stable = settings.OpenSubKey("TaskIsland.Desktop");
        if (stable?.GetValue("Enabled") is int chosen) return chosen == 0;
        using var legacy = identities.OpenSubKey(Path.GetFullPath(executablePath).Replace('\\', '.'));
        if (legacy?.GetValue("NotificationGUID") is not string oldId) return false;
        using var previous = settings.OpenSubKey(oldId);
        return previous?.GetValue("Enabled") is int enabled && enabled == 0;
    }
}
