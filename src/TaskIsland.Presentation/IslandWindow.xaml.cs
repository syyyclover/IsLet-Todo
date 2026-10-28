using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
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

    public IslandWindow(NotificationService notifications)
    {
        InitializeComponent();
        motion = new IslandMotion(this, IslandSurface, IslandClip, HeaderOffset, ContentPanel, ContentOffset, PositionWindow);
        this.notifications = notifications;
        DataContext = model;
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
            System.Windows.Application.Current?.Shutdown();
        };
#if DEBUG
        var testButton = new Button { Content = "发送测试通知 · Debug" };
        testButton.Click += OnTestNotification;
        SettingsPanel.Children.Add(testButton);
#endif
    }

    public void OpenTasks(bool focus) { state.Navigate(IslandPage.Tasks); RenderState(); if (focus) Activate(); }
    public void OpenSettings() { state.Navigate(IslandPage.Settings); RenderState(); Activate(); }
    public void OpenArchive() { state.Navigate(IslandPage.Archive); RenderState(); Activate(); }
    public void ShowStatus(string message) { model.Status = message; }
    private void OnPointerEnter(object sender, MouseEventArgs e) { leaveTimer.Stop(); if (state.Page == IslandPage.Collapsed) enterTimer.Start(); }
    private void OnPointerLeave(object sender, MouseEventArgs e) { enterTimer.Stop(); leaveTimer.Start(); }
    private void OnEmptyClick(object sender, MouseButtonEventArgs e) { OpenTasks(true); ShowStatus("添加日程将在任务阶段接入。"); }
    private void OnSettings(object sender, RoutedEventArgs e) => OpenSettings();
    private void OnArchive(object sender, RoutedEventArgs e) => OpenArchive();
    private void OnBack(object sender, RoutedEventArgs e) => OpenTasks(true);
    private void OnAnchor(object sender, RoutedEventArgs e)
    {
        model.Anchor = Enum.Parse<IslandAnchor>((string)((Button)sender).Tag);
        RefreshViewport();
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
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
        BackButton.Visibility = state.Page is IslandPage.Settings or IslandPage.Archive ? Visibility.Visible : Visibility.Collapsed;
        // Keep the outgoing page arranged until its parent has faded and the shape has closed.
        if (expanded)
        {
            TasksPanel.Visibility = state.Page == IslandPage.Tasks ? Visibility.Visible : Visibility.Collapsed;
            SettingsPanel.Visibility = state.Page == IslandPage.Settings ? Visibility.Visible : Visibility.Collapsed;
            ArchivePanel.Visibility = state.Page == IslandPage.Archive ? Visibility.Visible : Visibility.Collapsed;
        }
        var area = DesktopPlacement.PrimaryWorkAreaInDips(new WindowInteropHelper(this).Handle);
        motion.Transition(expanded, model.AnimationsEnabled && SystemParameters.ClientAreaAnimation, area, model.Anchor);
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
