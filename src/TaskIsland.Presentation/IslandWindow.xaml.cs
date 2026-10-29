using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Documents;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using TaskIsland.Application;
using TaskIsland.WindowsIntegration;

namespace TaskIsland.Presentation;

public partial class IslandWindow : Window
{
    private readonly IslandState state = new();
    private readonly IslandViewModel model = new();
    private readonly NotificationService notifications;
    private readonly Func<bool> systemAnimationsEnabled;
    private HwndSource? source;
    private readonly IslandMotion motion;
    private IslandPage displayedPage = IslandPage.Tasks;
    private int pageGeneration;
    private SettingsService? settingsService;
    private StartupRegistration? startupRegistration;
    private FullscreenMonitor? fullscreenMonitor;
    private bool hiddenForFullscreen;
    private AttachmentService? attachmentService;
    private bool exitApproved;
    private int exitRequestGeneration;
    private ListBoxItem? dragHighlight;
    private DragTargetAdorner? dragAdorner;
    private AdornerLayer? dragLayer;
    private readonly DispatcherTimer dragTracker = new() { Interval = TimeSpan.FromMilliseconds(16) };
    public static readonly DependencyProperty IsCountdownRunningProperty = DependencyProperty.Register(nameof(IsCountdownRunning), typeof(bool), typeof(IslandWindow), new PropertyMetadata(false));
    public bool IsCountdownRunning { get => (bool)GetValue(IsCountdownRunningProperty); private set => SetValue(IsCountdownRunningProperty, value); }

    public IslandWindow(NotificationService notifications) : this(notifications, static () => SystemParameters.ClientAreaAnimation) { }

    internal IslandWindow(NotificationService notifications, Func<bool> systemAnimationsEnabled)
    {
        this.systemAnimationsEnabled = systemAnimationsEnabled;
        InitializeComponent();
        motion = new IslandMotion(this, IslandSurface, IslandClip, HeaderOffset, ContentPanel, ContentOffset, PositionWindow);
        this.notifications = notifications;
        DataContext = model;
        model.NavigationRequested += page => Navigate(page, true);
        model.FeedbackRequested += PlayFeedbackAsync;
        dragTracker.Tick += (_, _) =>
        {
            if (CursorOutsideIsland()) EndExternalDrag();
            else if ((GetAsyncKeyState(1) & 0x8000) == 0) { dragTracker.Stop(); ClearDragHighlight(); }
        };
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(IslandViewModel.AnimationsEnabled) && new WindowInteropHelper(this).Handle != 0)
                RenderState();
            if (settingsService is not null && args.PropertyName is nameof(IslandViewModel.Anchor) or nameof(IslandViewModel.AnimationsEnabled)) SaveSettings();
        };
        if (notifications.Error is not null) model.Status = notifications.Error;
        SourceInitialized += (_, _) =>
        {
            TrayWindowStyle.Apply(new WindowInteropHelper(this).Handle);
            source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source.AddHook(WindowMessages);
            RefreshViewport();
        };
        SizeChanged += (_, _) => PositionWindow();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Closed += (_, _) =>
        {
            dragTracker.Stop(); ClearDragHighlight();
            fullscreenMonitor?.Dispose();
            source?.RemoveHook(WindowMessages);
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
            model.Dispose();
            System.Windows.Application.Current?.Shutdown();
        };
        Closing += (_, e) =>
        {
            if (!exitApproved && attachmentService?.IsCopying == true)
            {
                e.Cancel = true;
                exitRequestGeneration++;
                if (state.Page == IslandPage.Collapsed) state.Navigate(IslandPage.Tasks);
                Show(); RenderState(); CopyExitPrompt.Visibility = Visibility.Visible; Activate();
            }
        };
#if DEBUG
        var testButton = new Button { Content = "发送测试通知 · Debug" };
        testButton.Click += OnTestNotification;
        SettingsPanel.Children.Add(testButton);
#endif
    }

    public Task AttachTasksAsync(TaskService service) => model.AttachAsync(service);
    public void AttachAttachments(AttachmentService service) { attachmentService = service; model.AttachAttachmentService(service); }
    public void RequestExit() => Close();
    private async void OnWaitCopyExit(object sender, RoutedEventArgs e)
    { var request = exitRequestGeneration; if (attachmentService is not null) await attachmentService.WaitForIdleAsync(); FinishCopyExit(request); }
    private async void OnCancelCopyExit(object sender, RoutedEventArgs e)
    { var request = exitRequestGeneration; attachmentService?.CancelAll(); if (attachmentService is not null) await attachmentService.WaitForIdleAsync(); FinishCopyExit(request); }
    private void FinishCopyExit(int request) { if (request != exitRequestGeneration || exitApproved) return; exitApproved = true; Close(); }
    private void OnKeepCopying(object sender, RoutedEventArgs e) { exitRequestGeneration++; CopyExitPrompt.Visibility = Visibility.Collapsed; }
    private static ListBoxItem? RowAt(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ListBoxItem item) return item;
            source = source is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }
    private void ClearDragHighlight() { if (dragAdorner is not null) dragLayer?.Remove(dragAdorner); dragAdorner = null; dragLayer = null; dragHighlight = null; }
    private void SetDragHighlight(ListBoxItem? row)
    {
        if (ReferenceEquals(row, dragHighlight)) return;
        ClearDragHighlight(); if (row is null) return;
        dragHighlight = row; dragLayer = AdornerLayer.GetAdornerLayer(row);
        if (dragLayer is not null) { dragAdorner = new DragTargetAdorner(row); dragLayer.Add(dragAdorner); }
    }
    private bool CursorOutsideIsland() => GetCursorPos(out var position) && !motion.Contains(PointFromScreen(new Point(position.X, position.Y)));
    private void CollapseToTasks()
    {
        if (state.Page == IslandPage.Collapsed) return;
        state.Navigate(IslandPage.Tasks); SwitchPage(IslandPage.Tasks);
        state.CollapseAfterPointerLeave(); RenderState();
    }
    private void EndExternalDrag() { dragTracker.Stop(); ClearDragHighlight(); CollapseToTasks(); }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    private void OnAttachmentDragEnter(object sender, DragEventArgs e)
    { if (e.Data.GetDataPresent(DataFormats.FileDrop, false)) { dragTracker.Start(); if (state.Page != IslandPage.Tasks) OpenTasks(false); } }
    private void OnAttachmentDragOver(object sender, DragEventArgs e)
    {
        var row = RowAt(e.OriginalSource as DependencyObject);
        var accepted = state.Page == IslandPage.Tasks && row?.DataContext is TaskRowViewModel && e.Data.GetDataPresent(DataFormats.FileDrop, false) && !model.IsBusy;
        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        SetDragHighlight(accepted ? row : null);
        e.Handled = true;
    }
    private void OnAttachmentDragLeave(object sender, DragEventArgs e)
    {
        if (CursorOutsideIsland()) EndExternalDrag();
    }
    private async void OnAttachmentDrop(object sender, DragEventArgs e)
    {
        dragTracker.Stop(); ClearDragHighlight(); e.Handled = true;
        var row = RowAt(e.OriginalSource as DependencyObject);
        if (state.Page != IslandPage.Tasks || row?.DataContext is not TaskRowViewModel item || e.Data.GetData(DataFormats.FileDrop, false) is not string[] paths)
        { ShowError("请拖到具体日程上"); return; }
        await model.AddAttachmentsAsync(item.Item.Id, paths);
    }
    public async Task AttachSettingsAsync(SettingsService service)
    {
        var saved = await service.LoadAsync();
        model.Anchor = saved.Anchor; model.AnimationsEnabled = saved.AnimationsEnabled;
        settingsService = service; RefreshViewport();
        try { startupRegistration = new StartupRegistration(Environment.ProcessPath!); AutoStartCheck.IsChecked = startupRegistration.IsEnabled; }
        catch (Exception ex) { AutoStartCheck.IsEnabled = false; App.WriteDiagnostic("startup-setting", ex); }
    }
    private async void SaveSettings()
    {
        try { await settingsService!.SaveAsync(new(model.Anchor, model.AnimationsEnabled)); }
        catch (Exception ex) { App.WriteDiagnostic("settings", ex); ShowError("设置保存失败，请重试"); }
    }
    private void OnAutoStart(object sender, RoutedEventArgs e)
    {
        if (startupRegistration is null) return;
        try { startupRegistration.SetEnabled(AutoStartCheck.IsChecked == true); ShowStatus("已更新登录启动设置"); }
        catch (Exception ex) { AutoStartCheck.IsChecked = AutoStartCheck.IsChecked != true; App.WriteDiagnostic("startup-setting", ex); ShowError("登录启动设置失败"); }
    }
    public void StartFullscreenMonitoring()
    {
        fullscreenMonitor = new FullscreenMonitor(new WindowInteropHelper(this).Handle);
        fullscreenMonitor.FullscreenChanged += (_, _) => ApplyFullscreen(); ApplyFullscreen();
    }
    private void ApplyFullscreen()
    {
        if (fullscreenMonitor!.IsFullscreen)
        {
            hiddenForFullscreen = true; IsCountdownRunning = false; Hide();
        }
        else if (hiddenForFullscreen) { hiddenForFullscreen = false; Show(); RenderState(); }
    }
    public async Task FocusNotificationAsync(Guid? taskId)
    {
        await model.ReloadAsync();
        if (!model.TryLeaveEditor(IslandPage.Tasks)) { Activate(); return; }
        OpenTasks(true);
        if (taskId is not { } id) return;
        var row = model.Tasks.FirstOrDefault(x => x.Item.Id == id);
        if (row is null) { ShowError("这条日程已归档或不再存在"); return; }
        ActiveList.SelectedItem = row; ActiveList.ScrollIntoView(row);
        ActiveList.Focus(); ShowStatus("已定位到期日程");
    }
    public void OpenTasks(bool focus) => Navigate(IslandPage.Tasks, focus);
    public void OpenSettings() => Navigate(IslandPage.Settings, true);
    public void OpenArchive() => Navigate(IslandPage.Archive, true);
    private void Navigate(IslandPage page, bool focus)
    {
        if (model.ConfirmDelete && model.IsBusy) return;
        if (focus && !IsVisible) { hiddenForFullscreen = false; Show(); }
        if (!model.TryLeaveEditor(page)) { if (focus) Activate(); return; }
        if (page != IslandPage.Archive) model.CancelDeleteCommand.Execute(null);
        state.Navigate(page); RenderState();
        if (focus) Activate();
        if (page == IslandPage.Editor) Dispatcher.BeginInvoke(() => TitleInput.Focus(), DispatcherPriority.Input);
    }
    public void ShowStatus(string message) { model.IsError = false; model.Status = message; }
    public void ShowError(string message) { model.ErrorMessage = message; model.IsError = true; model.Status = message; }
    private void OnDismissError(object sender, RoutedEventArgs e) { model.IsError = false; model.Status = ""; }
    private Task PlayFeedbackAsync(Guid? id, OperationCue cue)
    {
        var enabled = model.AnimationsEnabled && systemAnimationsEnabled() && IsVisible;
        FrameworkElement? target = null;
        if (cue == OperationCue.Delete && id is null) target = ArchiveList;
        else if (id is { } taskId)
        {
            var archived = cue is OperationCue.Restore or OperationCue.Delete;
            var list = archived ? ArchiveList : ActiveList;
            var items = archived ? model.ArchivedTasks : model.Tasks;
            var row = items.FirstOrDefault(x => x.Item.Id == taskId);
            if (row is not null)
            {
                list.UpdateLayout();
                target = list.ItemContainerGenerator.ContainerFromItem(row) as FrameworkElement;
            }
        }
        if (cue == OperationCue.Save && target is null) target = AddTaskButton;
        if (target is null) return Task.CompletedTask;
        if (cue == OperationCue.Renew && enabled) FindHourglass(target)?.PulseRenewal();
        return OperationMotion.PlayAsync(target, cue, enabled);
    }
    private void OnPointerEnter(object sender, MouseEventArgs e) { if (state.Page == IslandPage.Collapsed) { state.ExpandAfterPointerEnter(); RenderState(); } }
    private void OnPointerLeave(object sender, MouseEventArgs e)
    {
        if (dragTracker.IsEnabled) { if (CursorOutsideIsland()) EndExternalDrag(); return; }
        CollapseToTasks();
    }
    private void OnEmptyClick(object sender, MouseButtonEventArgs e) { if (model.Tasks.Count == 0) model.AddCommand.Execute(null); else OpenTasks(true); }
    private void OnPageTabEnter(object sender, MouseEventArgs e) => SelectPeerPage(sender);
    private void OnPageTabClick(object sender, RoutedEventArgs e) => SelectPeerPage(sender);
    private void SelectPeerPage(object sender)
    {
        if (state.Page == IslandPage.Collapsed || dragTracker.IsEnabled) return;
        var page = ReferenceEquals(sender, ArchiveTab) ? IslandPage.Archive
            : ReferenceEquals(sender, SettingsTab) ? IslandPage.Settings : IslandPage.Tasks;
        if (state.Page == page) return;
        // Page browsing preserves the editor draft and never activates the window.
        state.Navigate(page); RenderState();
    }
    private async void OnRenewClick(object sender, RoutedEventArgs e)
    {
        if (model.IsBusy) return;
        if (sender is not Button { DataContext: TaskRowViewModel row }) return;
        await model.RenewCommand.ExecuteAsync(row);
    }
    private static HourglassControl? FindHourglass(DependencyObject parent)
    {
        if (parent is HourglassControl control) return control;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindHourglass(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        return null;
    }
    private void OnBack(object sender, RoutedEventArgs e) { model.LeaveEditorCommand.Execute(null); }
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
        if (model.ConfirmDelete) { model.CancelDeleteCommand.Execute(null); e.Handled = true; return; }
        if (state.Page == IslandPage.Editor) { if (model.HasUnsavedChanges) BackButton.Activate(); else model.LeaveEditorCommand.Execute(null); e.Handled = true; return; }
        state.Navigate(state.Page is IslandPage.Settings or IslandPage.Archive ? IslandPage.Tasks : IslandPage.Collapsed);
        RenderState(); e.Handled = true;
    }
#if DEBUG
    private void OnTestNotification(object sender, RoutedEventArgs e)
    {
        notifications.SendTest();
        if (notifications.Error is { } error) ShowError(error);
        else ShowStatus("测试通知已提交，请点击通知验证激活。");
    }
#endif

    private void RenderState()
    {
        var expanded = state.Page != IslandPage.Collapsed;
        IsCountdownRunning = expanded && state.Page == IslandPage.Tasks && model.AnimationsEnabled && systemAnimationsEnabled();
        BackButton.Visibility = state.Page is IslandPage.Editor or IslandPage.Attachments ? Visibility.Visible : Visibility.Collapsed;
        // Keep the outgoing page arranged until its parent has faded and the shape has closed.
        if (expanded)
        {
            SwitchPage(state.Page);
        }
        var area = DesktopPlacement.PrimaryWorkAreaInDips(new WindowInteropHelper(this).Handle);
        motion.Transition(expanded, model.AnimationsEnabled && systemAnimationsEnabled(), area, model.Anchor);
    }

    private void SwitchPage(IslandPage page)
    {
        FrameworkElement Panel(IslandPage p) => p switch { IslandPage.Settings => SettingsPanel, IslandPage.Archive => ArchivePanel, IslandPage.Editor => EditorViewport, IslandPage.Attachments => AttachmentsPanel, _ => TasksPanel };
        int Order(IslandPage p) => p switch { IslandPage.Archive => 1, IslandPage.Settings => 2, IslandPage.Editor => 3, _ => 0 };
        var previous = displayedPage; displayedPage = page;
        var generation = ++pageGeneration;
        var incoming = Panel(page); var outgoing = Panel(previous);
        foreach (var panel in new FrameworkElement[] { TasksPanel, ArchivePanel, SettingsPanel, EditorViewport, AttachmentsPanel })
        {
            if (panel.RenderTransform is not TranslateTransform) panel.RenderTransform = new TranslateTransform();
            var transform = (TranslateTransform)panel.RenderTransform;
            transform.BeginAnimation(TranslateTransform.XProperty, null); transform.X = 0;
            panel.Visibility = panel == incoming ? Visibility.Visible : Visibility.Collapsed;
        }
        EditorPanel.Visibility = page == IslandPage.Editor ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (button, destination) in new[] { (TasksTab, IslandPage.Tasks), (ArchiveTab, IslandPage.Archive), (SettingsTab, IslandPage.Settings) })
            button.Foreground = (Brush)FindResource(destination == page ? "IslandAccent" : "IslandMuted");
        if (previous == page || !model.AnimationsEnabled || !systemAnimationsEnabled()) return;
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
        if (message == 0x0010) { handled = true; CollapseToTasks(); return 0; } // Shell/Alt+F4 close keeps the tray resident. Tray exit calls RequestExit.
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
