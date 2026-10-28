using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace TaskIsland.Presentation;
public partial class TimePickerControl : UserControl
{
    private double dragY;
    public TimePickerControl() => InitializeComponent();
    private void Step(object sender, int delta) { if (DataContext is EditorTimeModel model && sender is FrameworkElement { Tag: string column }) model.Step(column, delta); }
    private void Up(object sender, RoutedEventArgs e) => Step(sender, 1);
    private void Down(object sender, RoutedEventArgs e) => Step(sender, -1);
    private void Wheel(object sender, MouseWheelEventArgs e) { Step(sender, e.Delta > 0 ? 1 : -1); e.Handled = true; }
    private void Key(object sender, KeyEventArgs e) { if (e.Key is System.Windows.Input.Key.Up or System.Windows.Input.Key.Down) { Step(sender, e.Key == System.Windows.Input.Key.Up ? 1 : -1); e.Handled = true; } }
    private void DragStart(object sender, MouseButtonEventArgs e) { var element = (FrameworkElement)sender; element.Focus(); dragY = e.GetPosition(element).Y; element.CaptureMouse(); e.Handled = true; }
    private void DragEnd(object sender, MouseButtonEventArgs e) => ((FrameworkElement)sender).ReleaseMouseCapture();
    private void DragMove(object sender, MouseEventArgs e)
    {
        var element = (FrameworkElement)sender;
        if (!element.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
        var y = e.GetPosition(element).Y; var steps = (int)((dragY - y) / 18);
        if (steps == 0) return; Step(sender, steps); dragY = y;
    }
}
