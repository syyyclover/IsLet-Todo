namespace TaskIsland.WindowsIntegration;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon icon;
    private readonly TrayMenu menu;
    private readonly System.Drawing.Icon ownedIcon;

    public TrayService(Action show, Action exit, Action openBackups)
    {
        menu = new TrayMenu(exit, openBackups);
        using var stream = typeof(TrayService).Assembly.GetManifestResourceStream("TaskIsland.WindowsIntegration.Assets.islet.ico")!;
        ownedIcon = new System.Drawing.Icon(stream, SystemInformation.SmallIconSize);
        icon = new NotifyIcon { Icon = ownedIcon, Text = "", ContextMenuStrip = menu, Visible = true };
        icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) show(); };
    }

    public void Dispose() { icon.Visible = false; icon.Dispose(); menu.Dispose(); ownedIcon.Dispose(); }
}
