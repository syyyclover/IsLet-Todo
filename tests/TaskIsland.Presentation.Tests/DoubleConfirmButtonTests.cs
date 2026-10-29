using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using TaskIsland.Presentation;
using Xunit;
public class DoubleConfirmButtonTests
{
 private sealed class TestButton:DoubleConfirmButton
 {public long Time; protected override long CurrentTimestamp=>Time;}
 [Fact] public void OnlySecondActivationExecutesAndRecyclingClearsArm()=>IslandMotionTests.RunSta(window=>
 {
  var count=0;var button=new TestButton{Command=new RelayCommand(()=>count++),DataContext=new object()};
  button.Activate();Assert.True(button.IsArmed);Assert.Equal(0,count);
  button.Activate();Assert.Equal(1,count);Assert.False(button.IsArmed);
  button.Activate();button.DataContext=new object();button.Activate();Assert.Equal(1,count);
  button.Time+=10000;button.Activate();Assert.Equal(1,count);button.Activate();Assert.Equal(2,count);
 });
 [Fact] public void AutomationInvokeAlsoRequiresTwoActivations()=>IslandMotionTests.RunSta(window=>
 {
  var count=0;var button=new DoubleConfirmButton{Command=new RelayCommand(()=>count++),Content="删除"};
  var deck=(Panel)window.FindName("PageDeck");deck.Children.Add(button);Grid.SetRow(button,1);
  window.OpenTasks(false);IslandMotionTests.Pump(300);
  var peer=new System.Windows.Automation.Peers.ButtonAutomationPeer(button);
  var provider=(System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke);
  provider.Invoke();IslandMotionTests.Pump(20);Assert.Equal(0,count);Assert.True(button.IsArmed);
  provider.Invoke();IslandMotionTests.Pump(20);Assert.Equal(1,count);
 });
 [Fact] public void NoConfirmationRunsOnceAndUnloadingClearsArm()=>IslandMotionTests.RunSta(window=>
 {
  var count=0;var button=new TestButton{Command=new RelayCommand(()=>count++),ConfirmationRequired=false};
  button.Activate();Assert.Equal(1,count);
  button.ConfirmationRequired=true;button.Activate();button.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
  Assert.False(button.IsArmed);button.Activate();Assert.Equal(1,count);button.Visibility=Visibility.Collapsed;Assert.False(button.IsArmed);
 });
}
