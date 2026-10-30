using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Xunit;

public class TooltipSuppressionTests
{
    [Fact] public void WindowBlocksTooltipsEvenWhenAChildTemplateEnablesThem() => IslandMotionTests.RunSta(window =>
    {
        var panel = (Panel)window.FindName("TasksPanel");
        var child = new Button { ToolTip = "Click to go forward, hold to see history" };
        ToolTipService.SetIsEnabled(child, true);
        panel.Children.Add(child);
        var opening = (ToolTipEventArgs)Activator.CreateInstance(typeof(ToolTipEventArgs), BindingFlags.Instance | BindingFlags.NonPublic, null, [true], null)!;
        child.RaiseEvent(opening);
        Assert.True(opening.Handled);
    });
}
