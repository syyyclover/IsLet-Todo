using Microsoft.Win32;
using TaskIsland.WindowsIntegration;
using Xunit;

public class NotificationPreferenceGuardTests
{
    [Fact] public void StableIdentityDoesNotBypassDisabledLegacyNotificationPreference()
    {
        var keyPath = @"Software\TaskIsland.Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            using var root = Registry.CurrentUser.CreateSubKey(keyPath);
            using var ids = root.CreateSubKey("Identities"); using var settings = root.CreateSubKey("Settings");
            var exe = @"C:\App\TaskIsland.exe";
            Assert.False(NotificationPreferenceGuard.IsLegacyDisabled(exe, ids, settings));
            using var legacy = ids.CreateSubKey(exe.Replace('\\', '.')); legacy.SetValue("NotificationGUID", "legacy-id");
            using var oldSetting = settings.CreateSubKey("legacy-id"); oldSetting.SetValue("Enabled", 0);
            Assert.True(NotificationPreferenceGuard.IsLegacyDisabled(exe, ids, settings));
            using var stable = settings.CreateSubKey("TaskIsland.Desktop"); stable.SetValue("Enabled", 1);
            Assert.False(NotificationPreferenceGuard.IsLegacyDisabled(exe, ids, settings));
            stable.SetValue("Enabled", 0);
            Assert.True(NotificationPreferenceGuard.IsLegacyDisabled(exe, ids, settings));
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(keyPath, false); }
    }
}
