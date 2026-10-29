namespace TaskIsland.WindowsIntegration;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon icon;
    private readonly ContextMenuStrip menu = new();

    public TrayService(Action show, Action settings, Action archive, Action exit)
    {
        menu.Items.Add("显示岛", null, (_, _) => show());
        menu.Items.Add("设置", null, (_, _) => settings());
        menu.Items.Add("查看归档", null, (_, _) => archive());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => exit());
        icon = new NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "", ContextMenuStrip = menu, Visible = true };
        icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) show(); };
    }

    public void Dispose() { icon.Visible = false; icon.Dispose(); menu.Dispose(); }
}
