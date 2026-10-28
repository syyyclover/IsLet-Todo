using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TaskIsland.Application;

namespace TaskIsland.Presentation;

/// <summary>Animates render-only geometry inside a stable, island-sized layered surface.</summary>
internal sealed class IslandMotion : Animatable
{
    private static readonly DependencyProperty RevealProperty = DependencyProperty.Register(
        nameof(Reveal), typeof(double), typeof(IslandMotion),
        new PropertyMetadata(0d, (sender, _) => ((IslandMotion)sender).UpdateGeometry()));

    private readonly Window window;
    private readonly FrameworkElement surface;
    private readonly RectangleGeometry clip;
    private readonly TranslateTransform headerOffset;
    private readonly FrameworkElement content;
    private readonly TranslateTransform contentOffset;
    private readonly Action positionWindow;
    private IslandAnchor anchor;
    private double expandedWidth = 360, expandedHeight = 295;
    private int transition;
    private bool targetExpanded;

    public IslandMotion(Window window, FrameworkElement surface, RectangleGeometry clip,
        TranslateTransform headerOffset, FrameworkElement content, TranslateTransform contentOffset,
        Action positionWindow)
    {
        this.window = window; this.surface = surface; this.clip = clip;
        this.headerOffset = headerOffset; this.content = content;
        this.contentOffset = contentOffset; this.positionWindow = positionWindow;
    }

    private double Reveal { get => (double)GetValue(RevealProperty); set => SetValue(RevealProperty, value); }
    public bool Contains(Point point) => clip.FillContains(point);

    public void ConfigureViewport(WorkArea area, IslandAnchor newAnchor)
    {
        anchor = newAnchor;
        expandedWidth = Math.Min(360, Math.Max(1, area.Width));
        expandedHeight = Math.Min(295, Math.Max(1, area.Height - 8));
        surface.Width = expandedWidth; surface.Height = expandedHeight;
        // Resizing a layered HWND invalidates its backing surface. Keep it stable across open/close.
        window.Width = expandedWidth; window.Height = expandedHeight;
        positionWindow();
        UpdateGeometry();
    }

    public void Transition(bool expanded, bool animate, WorkArea area, IslandAnchor newAnchor)
    {
        ConfigureViewport(area, newAnchor);
        if (targetExpanded == expanded && !HasAnimatedProperties)
        {
            return;
        }

        targetExpanded = expanded;
        var generation = ++transition;
        var from = Reveal;
        Reveal = from;
        BeginAnimation(RevealProperty, null);
        var target = expanded ? 1d : 0d;
        content.Visibility = Visibility.Visible;
        content.IsHitTestVisible = expanded;

        if (!animate)
        {
            Reveal = target;
            SetContentImmediately(expanded);
            Finish(expanded, generation);
            return;
        }

        // Gentle fast-out/slow-in shape change; text is never stretched with the capsule.
        var animation = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(220))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.HoldEnd };
        animation.Completed += (_, _) => Finish(expanded, generation);
        BeginAnimation(RevealProperty, animation, HandoffBehavior.SnapshotAndReplace);
        AnimateContent(expanded);
    }

    private void AnimateContent(bool expanded)
    {
        var opacity = content.Opacity;
        var offset = contentOffset.Y;
        content.Opacity = opacity; contentOffset.Y = offset;
        content.BeginAnimation(UIElement.OpacityProperty, null);
        contentOffset.BeginAnimation(TranslateTransform.YProperty, null);
        var duration = TimeSpan.FromMilliseconds(expanded ? 170 : 100);
        content.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(opacity, expanded ? 1 : 0, duration)
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        contentOffset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, expanded ? 0 : 8, duration)
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void SetContentImmediately(bool expanded)
    {
        content.Opacity = expanded ? 1 : 0;
        contentOffset.Y = expanded ? 0 : 8;
        content.BeginAnimation(UIElement.OpacityProperty, null);
        contentOffset.BeginAnimation(TranslateTransform.YProperty, null);
    }

    private void Finish(bool expanded, int generation)
    {
        if (generation != transition) return;
        Reveal = expanded ? 1 : 0;
        BeginAnimation(RevealProperty, null);
        SetContentImmediately(expanded);
        content.Visibility = expanded ? Visibility.Visible : Visibility.Hidden;
        UpdateGeometry();
    }

    private void UpdateGeometry()
    {
        if (clip is null) return;
        var progress = Math.Clamp(Reveal, 0, 1);
        var width = Math.Min(240, expandedWidth) + Math.Max(0, expandedWidth - 240) * progress;
        var height = Math.Min(40, expandedHeight) + Math.Max(0, expandedHeight - 40) * progress;
        var factor = anchor switch { IslandAnchor.Left => 0d, IslandAnchor.Right => 1d, _ => 0.5d };
        var left = Math.Max(0, window.Width - width) * factor;
        clip.Rect = new Rect(left, 0, width, height);
        headerOffset.X = left;
        // Only geometry and an existing RenderTransform change on animation frames.
    }

    protected override Freezable CreateInstanceCore() => throw new NotSupportedException("Window motion cannot be cloned.");
}
