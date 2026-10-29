using System.Drawing;
using System.Drawing.Drawing2D;

namespace TaskIsland.WindowsIntegration;

public sealed class TrayMenu : ContextMenuStrip
{
    private readonly Font menuFont = new("Microsoft YaHei UI", 9.5f);
    public TrayMenu(Action exit, Action openBackups)
    {
        ShowImageMargin = false;
        ShowCheckMargin = false;
        BackColor = Color.FromArgb(23, 29, 40);
        ForeColor = Color.FromArgb(243, 244, 247);
        Font = menuFont;
        Padding = new Padding(6);
        Renderer = new IslandMenuRenderer();
        Add("退出", exit);
        Add("打开备份文件目录", openBackups);
        AccessibleName = "IsLet-Todo 托盘菜单";
    }

    private void Add(string text, Action action)
    {
        var item = new ToolStripMenuItem(text) { AutoSize = false, Size = new Size(210, 34), Padding = new Padding(12, 0, 12, 0) };
        item.Click += (_, _) => action();
        Items.Add(item);
    }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) menuFont.Dispose(); }

    private sealed class IslandMenuRenderer : ToolStripProfessionalRenderer
    {
        public IslandMenuRenderer() => RoundedEdges = false;
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(e.ToolStrip.BackColor);
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Color.FromArgb(55, 67, 76));
            e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = new GraphicsPath();
            var rect = new Rectangle(1, 1, e.Item.Width - 3, e.Item.Height - 3);
            const int diameter = 12;
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            using var brush = new SolidBrush(Color.FromArgb(43, 57, 56));
            e.Graphics.FillPath(brush, path);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? Color.FromArgb(166, 217, 181) : e.Item.ForeColor;
            base.OnRenderItemText(e);
        }
    }
}
