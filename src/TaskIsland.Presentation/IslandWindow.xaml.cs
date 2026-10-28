using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using TaskIsland.Application;
using TaskIsland.WindowsIntegration;

namespace TaskIsland.Presentation;

public partial class IslandWindow : Window
{
    private readonly IslandState state = new();
    private readonly IslandViewModel model = new();
    private readonly NotificationService notifications;
    private readonly DispatcherTimer enterTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer leaveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private HwndSource? source;
    private readonly IslandMotion motion;
    private IslandPage displayedPage = IslandPage.Tasks;
    private int pageGeneration;
    public static readonly DependencyProperty IsCountdownRunningProperty = DependencyProperty.Register(nameof(IsCountdownRunning), typeof(bool), typeof(IslandWindow), new PropertyMetadata(false));
    public bool IsCountdownRunning { get => (bool)GetValue(IsCountdownRunningProperty); private set => SetValue(IsCountdownRunningProperty, value); }

    public IslandWindow(NotificationService notifications)
    {
        InitializeComponent();
        motion = new IslandMotion(this, IslandSurface, IslandClip, HeaderOffset, ContentPanel, ContentOffset, PositionWindow);
        this.notifications = notifications;
        DataContext = model;
        model.NavigationRequested += page => Navigate(page, true);
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(IslandViewModel.AnimationsEnabled) && new WindowInteropHelper(this).Handle != 0)
                RenderState();
        };
        if (notifications.Error is not null) model.Status = notifications.Error;
        enterTimer.Tick += (_, _) => { enterTimer.Stop(); if (IsMouseOver && state.Page == IslandPage.Collapsed) OpenTasks(false); };
        leaveTimer.Tick += (_, _) => { leaveTimer.Stop(); if (!IsMouseOver) { state.CollapseAfterPointerLeave(); RenderState(); } };
        SourceInitialized += (_, _) =>
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source.AddHook(WindowMessages);
            RefreshViewport();
        };
        SizeChanged += (_, _) => PositionWindow();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Closed += (_, _) =>
        {
            enterTimer.Stop(); leaveTimer.Stop(); source?.RemoveHook(WindowMessages);
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
            model.Dispose();
            System.Windows.Application.Current?.Shutdown();
        };
#if DEBUG
        var testButton = new Button { Content = "发送测试通知 · Debug" };
        testButton.Click += OnTestNotification;
        SettingsPanel.Children.Add(testButton);
#endif
    }

    public Task AttachTasksAsync(TaskService service) => model.AttachAsync(service);
    public void OpenTasks(bool focus) => Navigate(IslandPage.Tasks, focus);
    public void OpenSettings() => Navigate(IslandPage.Settings, true);
    public void OpenArchive() => Navigate(IslandPage.Archive, true);
    private void Navigate(IslandPage page, bool focus)
    {
        if (!model.TryLeaveEditor(page)) { if (focus) Activate(); return; }
        state.Navigate(page); RenderState();
        if (focus) Activate();
        if (page == IslandPage.Editor) Dispatcher.BeginInvoke(() => TitleInput.Focus(), DispatcherPriority.Input);
    }
    public void ShowStatus(string message) { model.Status = message; }
    public void ShowError(string message) { model.IsError = true; model.Status = message; }
    private void OnPointerEnter(object sender, MouseEventArgs e) { leaveTimer.Stop(); if (state.Page == IslandPage.Collapsed) enterTimer.Start(); }
    private void OnPointerLeave(object sender, MouseEventArgs e) { enterTimer.Stop(); leaveTimer.Start(); }
    private void OnEmptyClick(object sender, MouseButtonEventArgs e) { if (model.Tasks.Count == 0) model.AddCommand.Execute(null); else OpenTasks(true); }
    private void OnSettings(object sender, RoutedEventArgs e) => OpenSettings();
    private void OnTasks(object sender, RoutedEventArgs e) => OpenTasks(true);
    private void OnArchive(object sender, RoutedEventArgs e) => OpenArchive();
    private async void OnRenewClick(object sender, RoutedEventArgs e)
    {
        if (model.IsBusy) return;
        if (sender is not Button { DataContext: TaskRowViewModel row }) return;
        await model.RenewCommand.ExecuteAsync(row);
        if (model.IsError) return;
        ActiveList.UpdateLayout();
        var current = model.Tasks.FirstOrDefault(x => x.Item.Id == row.Item.Id);
        if (current is not null && ActiveList.ItemContainerGenerator.ContainerFromItem(current) is DependencyObject container)
            FindHourglass(container)?.PulseRenewal();
    }
    private static HourglassControl? FindHourglass(DependencyObject parent)
    {
        if (parent is HourglassControl control) return control;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindHourglass(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        return null;
    }
    private void OnBack(object sender, RoutedEventArgs e) { if (state.Page == IslandPage.Editor) model.CancelEditCommand.Execute(null); else OpenTasks(true); }
    private async void OnTaskRightClick(object sender, MouseButtonEventArgs e)
    { if (sender is FrameworkElement { DataContext: TaskRowViewModel row }) { e.Handled = true; await model.PinCommand.ExecuteAsync(row); } }
    private void OnAnchor(object sender, RoutedEventArgs e)
    {
        model.Anchor = Enum.Parse<IslandAnchor>((string)((Button)sender).Tag);
        RefreshViewport();
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (state.Page == IslandPage.Editor) { model.CancelEditCommand.Execute(null); e.Handled = true; return; }
        state.Navigate(state.Page is IslandPage.Settings or IslandPage.Archive ? IslandPage.Tasks : IslandPage.Collapsed);
        RenderState(); e.Handled = true;
    }
#if DEBUG
    private void OnTestNotification(object sender, RoutedEventArgs e)
    {
        notifications.SendTest();
        ShowStatus(notifications.Error ?? "测试通知已提交，请点击通知验证激活。");
    }
#endif

    private void RenderState()
    {
        var expanded = state.Page != IslandPage.Collapsed;
        IsCountdownRunning = expanded && state.Page == IslandPage.Tasks && model.AnimationsEnabled && SystemParameters.ClientAreaAnimation;
        BackButton.Visibility = state.Page == IslandPage.Editor ? Visibility.Visible : Visibility.Collapsed;
        // Keep the outgoing page arranged until its parent has faded and the shape has closed.
        if (expanded)
        {
            SwitchPage(state.Page);
        }
        var area = DesktopPlacement.PrimaryWorkAreaInDips(new WindowInteropHelper(this).Handle);
        motion.Transition(expanded, model.AnimationsEnabled && SystemParameters.ClientAreaAnimation, area, model.Anchor);
    }

    private void SwitchPage(IslandPage page)
    {
        FrameworkElement Panel(IslandPage p) => p switch { IslandPage.Settings => SettingsPanel, IslandPage.Archive => ArchivePanel, IslandPage.Editor => EditorViewport, _ => TasksPanel };
        int Order(IslandPage p) => p switch { IslandPage.Archive => 1, IslandPage.Settings => 2, IslandPage.Editor => 3, _ => 0 };
        var previous = displayedPage; displayedPage = page;
        var generation = ++pageGeneration;
        var incoming = Panel(page); var outgoing = Panel(previous);
        foreach (var panel in new FrameworkElement[] { TasksPanel, ArchivePanel, SettingsPanel, EditorViewport })
        {
            if (panel.RenderTransform is not TranslateTransform) panel.RenderTransform = new TranslateTransform();
            var transform = (TranslateTransform)panel.RenderTransform;
            transform.BeginAnimation(TranslateTransform.XProperty, null); transform.X = 0;
            panel.Visibility = panel == incoming ? Visibility.Visible : Visibility.Collapsed;
        }
        EditorPanel.Visibility = page == IslandPage.Editor ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (button, destination) in new[] { (TasksTab, IslandPage.Tasks), (ArchiveTab, IslandPage.Archive), (SettingsTab, IslandPage.Settings) })
            button.Foreground = (Brush)FindResource(destination == page ? "IslandAccent" : "IslandMuted");
        if (previous == page || !model.AnimationsEnabled || !SystemParameters.ClientAreaAnimation) return;
        outgoing.Visibility = Visibility.Visible;
        var direction = Order(page) > Order(previous) ? 1 : -1;
        var duration = TimeSpan.FromMilliseconds(220);
        DoubleAnimation Slide(double from, double to) => new(from, to, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var entry = Slide(direction * 334, 0);
        entry.Completed += (_, _) => { if (generation == pageGeneration) { outgoing.Visibility = Visibility.Collapsed; ((TranslateTransform)incoming.RenderTransform).BeginAnimation(TranslateTransform.XProperty, null); } };
        ((TranslateTransform)outgoing.RenderTransform).BeginAnimation(TranslateTransform.XProperty, Slide(0, -direction * 334));
        ((TranslateTransform)incoming.RenderTransform).BeginAnimation(TranslateTransform.XProperty, entry);
    }

    private void PositionWindow()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return;
        var area = DesktopPlacement.PrimaryWorkAreaInDips(handle);
        MaxWidth = Math.Max(1, area.Width); MaxHeight = Math.Max(1, area.Height - 8);
        var bounds = IslandPlacement.Calculate(area, Width, Height, model.Anchor);
        Left = bounds.X; Top = bounds.Y;
    }
    private nint WindowMessages(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message is 0x02E0 or 0x001A or 0x007E) Dispatcher.BeginInvoke(RefreshViewport);
        if (message == 0x0084) // Alpha-zero layered pixels handle cross-process margins; this assists same-thread hit testing.
        {
            var packed = lParam.ToInt64();
            var point = PointFromScreen(new Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF)));
            if (!motion.Contains(point)) { handled = true; return -1; } // HTTRANSPARENT
        }
        // Hover-expanded windows should not become active merely when Windows asks about mouse activation.
        if (message == 0x0021) { handled = true; return 3; } // MA_NOACTIVATE; buttons explicitly activate on click.
        return 0;
    }
    private void RefreshViewport()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != 0) motion.ConfigureViewport(DesktopPlacement.PrimaryWorkAreaInDips(handle), model.Anchor);
    }
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshViewport);
    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
        => Dispatcher.BeginInvoke(e.PropertyName == nameof(SystemParameters.ClientAreaAnimation) ? RenderState : RefreshViewport);
}
