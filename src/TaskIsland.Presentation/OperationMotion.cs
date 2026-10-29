using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TaskIsland.Presentation;

public enum OperationCue { Complete, Archive, Renew, Pin, Restore, Save, Delete }

/// <summary>Short render-only acknowledgement of an operation that has already succeeded.</summary>
public static class OperationMotion
{
    private static readonly ConditionalWeakTable<FrameworkElement, Session> Running = new();

    public static Task PlayAsync(FrameworkElement target, OperationCue cue, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.VerifyAccess();
        if (Running.TryGetValue(target, out var previous)) previous.Finish();
        if (!enabled || !SystemParameters.ClientAreaAnimation || !target.IsLoaded || !target.IsVisible)
            return Task.CompletedTask;
        var session = new Session(target, cue);
        Running.Add(target, session);
        try { session.Start(); }
        catch { session.Finish(); throw; }
        return session.Completion.Task;
    }

    private sealed class Session
    {
        private readonly FrameworkElement target;
        private readonly OperationCue cue;
        private readonly Transform? originalTransform;
        private readonly Point originalOrigin;
        private readonly object localTransform;
        private readonly object localOrigin;
        private readonly TransformGroup temporary = new();
        private readonly ScaleTransform scale = new();
        private readonly TranslateTransform offset = new();
        private readonly DispatcherTimer timeout;
        private bool finished;
        private bool fades;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Session(FrameworkElement target, OperationCue cue)
        {
            this.target = target;
            this.cue = cue;
            originalTransform = target.RenderTransform;
            originalOrigin = target.RenderTransformOrigin;
            localTransform = target.ReadLocalValue(UIElement.RenderTransformProperty);
            localOrigin = target.ReadLocalValue(UIElement.RenderTransformOriginProperty);
            if (originalTransform is not null) temporary.Children.Add(originalTransform);
            temporary.Children.Add(scale);
            temporary.Children.Add(offset);
            // A watchdog also completes an interrupted/replaced animation clock.
            timeout = new DispatcherTimer(DispatcherPriority.Normal, target.Dispatcher) { Interval = TimeSpan.FromMilliseconds(240) };
            timeout.Tick += OnTimeout;
        }

        public void Start()
        {
            target.SetCurrentValue(UIElement.RenderTransformProperty, temporary);
            target.SetCurrentValue(UIElement.RenderTransformOriginProperty, new Point(.5, .5));
            target.Unloaded += OnUnloaded;
            target.IsVisibleChanged += OnVisibilityChanged;
            fades = cue is OperationCue.Complete or OperationCue.Archive or OperationCue.Delete;
            if (fades)
            {
                // Return smoothly before the row is reconciled; never restore a fully faded row in one frame.
                target.BeginAnimation(UIElement.OpacityProperty, Pulse(target.Opacity, target.Opacity * .6));
                if (cue is OperationCue.Archive or OperationCue.Delete)
                    offset.BeginAnimation(TranslateTransform.XProperty, Pulse(0, cue == OperationCue.Archive ? 8 : -6));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, Pulse(1, .96));
            }
            else if (cue is OperationCue.Pin or OperationCue.Restore)
                offset.BeginAnimation(TranslateTransform.XProperty, Pulse(0, cue == OperationCue.Pin ? 3 : -4));
            else
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, Pulse(1, cue == OperationCue.Save ? .99 : .96));

            var master = Pulse(1, cue == OperationCue.Save ? .99 : .96);
            master.Completed += (_, _) => Finish();
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, master);
            timeout.Start();
        }

        private static DoubleAnimation Animate(double from, double to) => new(from, to, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop
        };
        private static DoubleAnimation Pulse(double from, double to)
        {
            var animation = Animate(from, to);
            animation.Duration = TimeSpan.FromMilliseconds(80);
            animation.AutoReverse = true;
            return animation;
        }
        private void OnTimeout(object? sender, EventArgs args) => Finish();
        private void OnUnloaded(object sender, RoutedEventArgs args) => Finish();
        private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
        {
            if (!target.IsVisible) Finish();
        }

        public void Finish()
        {
            if (finished) return;
            finished = true;
            timeout.Stop();
            timeout.Tick -= OnTimeout;
            target.Unloaded -= OnUnloaded;
            target.IsVisibleChanged -= OnVisibilityChanged;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            offset.BeginAnimation(TranslateTransform.XProperty, null);
            if (fades) target.BeginAnimation(UIElement.OpacityProperty, null);
            if (ReferenceEquals(target.RenderTransform, temporary))
            {
                if (localTransform == DependencyProperty.UnsetValue) target.ClearValue(UIElement.RenderTransformProperty);
                else target.SetCurrentValue(UIElement.RenderTransformProperty, originalTransform);
                if (localOrigin == DependencyProperty.UnsetValue) target.ClearValue(UIElement.RenderTransformOriginProperty);
                else target.SetCurrentValue(UIElement.RenderTransformOriginProperty, originalOrigin);
            }
            if (Running.TryGetValue(target, out var current) && ReferenceEquals(current, this)) Running.Remove(target);
            Completion.TrySetResult();
        }
    }
}
