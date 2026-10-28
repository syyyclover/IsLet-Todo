using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
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

    public IslandWindow(NotificationService notifications)
    {
        InitializeComponent();
        this.notifications = notifications;
        DataContext = model;
        if (notifications.Error is not null) model.Status = notifications.Error;
        enterTimer.Tick += (_, _) => { enterTimer.Stop(); if (IsMouseOver && state.Page == IslandPage.Collapsed) OpenTasks(false); };
        leaveTimer.Tick += (_, _) => { leaveTimer.Stop(); if (!IsMouseOver) { state.CollapseAfterPointerLeave(); RenderState(); } };
        SourceInitialized += (_, _) =>
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source.AddHook(WindowMessages);
            PositionWindow();
        };
        SizeChanged += (_, _) => PositionWindow();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Closed += (_, _) =>
        {
            enterTimer.Stop(); leaveTimer.Stop(); source?.RemoveHook(WindowMessages);
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
            System.Windows.Application.Current.Shutdown();
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
        PositionWindow();
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
        ContentPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = state.Page is IslandPage.Settings or IslandPage.Archive ? Visibility.Visible : Visibility.Collapsed;
        TasksPanel.Visibility = state.Page == IslandPage.Tasks ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = state.Page == IslandPage.Settings ? Visibility.Visible : Visibility.Collapsed;
        ArchivePanel.Visibility = state.Page == IslandPage.Archive ? Visibility.Visible : Visibility.Collapsed;
        var area = DesktopPlacement.PrimaryWorkAreaInDips(new WindowInteropHelper(this).Handle);
        var bounds = IslandPlacement.Calculate(area, expanded ? 360 : 240, expanded ? 295 : 40, model.Anchor);
        Animate(WidthProperty, bounds.Width);
        Animate(HeightProperty, bounds.Height);
    }

    private void Animate(DependencyProperty property, double target)
    {
        var current = (double)GetValue(property);
        BeginAnimation(property, null);
        SetValue(property, target);
        if (model.AnimationsEnabled && SystemParameters.ClientAreaAnimation && Math.Abs(current - target) > 0.1)
            BeginAnimation(property, new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(220))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }, HandoffBehavior.SnapshotAndReplace);
    }

    private void PositionWindow()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return;
        var area = DesktopPlacement.PrimaryWorkAreaInDips(handle);
        MaxWidth = Math.Max(1, area.Width); MaxHeight = Math.Max(1, area.Height - 8);
        var bounds = IslandPlacement.Calculate(area, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height, model.Anchor);
        Left = bounds.X; Top = bounds.Y;
    }
    private nint WindowMessages(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message is 0x02E0 or 0x001A or 0x007E) Dispatcher.BeginInvoke(PositionWindow);
        // Hover-expanded windows should not become active merely when Windows asks about mouse activation.
        if (message == 0x0021) { handled = true; return 3; } // MA_NOACTIVATE; buttons explicitly activate on click.
        return 0;
    }
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(PositionWindow);
    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(PositionWindow);
}
