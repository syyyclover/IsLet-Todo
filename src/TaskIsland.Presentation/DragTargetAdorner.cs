using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace TaskIsland.Presentation;

public sealed class DragTargetAdorner : Adorner
{
    private static readonly Pen Outline = new(new SolidColorBrush(Color.FromRgb(166, 217, 181)), 1.4);
    static DragTargetAdorner() => Outline.Freeze();
    public DragTargetAdorner(UIElement target) : base(target) { IsHitTestVisible = false; }
    protected override void OnRender(DrawingContext context)
    {
        var size = AdornedElement.RenderSize;
        if (size.Width > 2 && size.Height > 2) context.DrawRoundedRectangle(null, Outline, new Rect(1, 1, size.Width - 2, size.Height - 2), 8, 8);
    }
}
