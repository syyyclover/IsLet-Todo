using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using TaskIsland.Presentation;
using Xunit;

public class IslandFeedbackVisualTests
{
 [Fact] public void NormalStatusIsAbsentAndErrorsStayInsidePageDeck() => IslandMotionTests.RunSta(window =>
 {
  var model=(IslandViewModel)window.DataContext; model.Status="普通状态不得显示"; model.IsError=false;
  window.OpenTasks(false); IslandMotionTests.Pump(300);
  Assert.DoesNotContain(Descendants(window).OfType<TextBlock>(),text=>text.IsVisible && text.Text==model.Status);
  var banner=(FrameworkElement)window.FindName("ErrorBanner"); Assert.NotNull(banner);
  Assert.Equal(Visibility.Collapsed,banner.Visibility);
  window.ShowError("写入失败"); IslandMotionTests.Pump(40);
  Assert.Equal(Visibility.Visible,banner.Visibility);
  Assert.Same(window.FindName("PageDeck"),VisualTreeHelper.GetParent(banner));
  Assert.Equal(0,Grid.GetRow(banner)); Assert.Equal(1,Grid.GetRow((FrameworkElement)window.FindName("EditorViewport")));
  Assert.Equal("日程内容",AutomationProperties.GetName((FrameworkElement)window.FindName("TitleInput")));
  model.Status="已完成并归档"; IslandMotionTests.Pump(20);
  Assert.DoesNotContain(Descendants(window).OfType<TextBlock>(),text=>text.IsVisible && text.Text==model.Status);
  Assert.Contains(Descendants(banner).OfType<TextBlock>(),text=>text.Text=="写入失败");
 });
 [Fact] public void HoverTextIsDisabledAndAccessibleNamesRemain() => IslandMotionTests.RunSta(window =>
 {
  window.OpenTasks(false); IslandMotionTests.Pump(300);
  foreach(var element in Descendants(window).OfType<FrameworkElement>()) Assert.Null(element.ToolTip);
  var add=(Button)window.FindName("AddTaskButton"); Assert.False(ToolTipService.GetIsEnabled(add));
  Assert.Equal("添加日程",AutomationProperties.GetName(add));
 });
 private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
 {
  for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
  {var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var node in Descendants(child))yield return node;}
 }
}
