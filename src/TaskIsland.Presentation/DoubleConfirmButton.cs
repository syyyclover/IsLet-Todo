using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TaskIsland.Presentation;
/// <summary>Two ordinary button activations confirm the same item within the Windows double-click interval.</summary>
public class DoubleConfirmButton : Button
{
 public static readonly DependencyProperty ConfirmationRequiredProperty=DependencyProperty.Register(nameof(ConfirmationRequired),typeof(bool),typeof(DoubleConfirmButton),new PropertyMetadata(true,(d,e)=>((DoubleConfirmButton)d).ResetArm()));
 private static readonly DependencyPropertyKey IsArmedKey=DependencyProperty.RegisterReadOnly(nameof(IsArmed),typeof(bool),typeof(DoubleConfirmButton),new PropertyMetadata(false));
 public static readonly DependencyProperty IsArmedProperty=IsArmedKey.DependencyProperty;
 public bool ConfirmationRequired {get=>(bool)GetValue(ConfirmationRequiredProperty);set=>SetValue(ConfirmationRequiredProperty,value);}
 public bool IsArmed=>(bool)GetValue(IsArmedProperty);
 private readonly DispatcherTimer timer;
 private long armedAt;
 private object? armedContext,armedParameter;
 private Brush? previousBorder;
 private double previousOpacity;
 protected virtual long CurrentTimestamp=>Environment.TickCount64;
 public DoubleConfirmButton()
 {
  SetResourceReference(StyleProperty,typeof(Button));
  timer=new DispatcherTimer(DispatcherPriority.Input,Dispatcher){Interval=TimeSpan.FromMilliseconds(GetDoubleClickTime())};
  timer.Tick+=(_,_)=>ResetArm();
  DataContextChanged+=(_,_)=>ResetArm();Unloaded+=(_,_)=>ResetArm();
  IsVisibleChanged+=(_,_)=>{if(!IsVisible)ResetArm();};
 }
 public void Activate()=>OnClick();
 protected override void OnClick()
 {
  if(!IsEnabled){ResetArm();return;}
  if(!ConfirmationRequired){ResetArm();base.OnClick();return;}
  if(IsArmed&&ReferenceEquals(DataContext,armedContext)&&Equals(CommandParameter,armedParameter)&&CurrentTimestamp-armedAt<=GetDoubleClickTime())
  {ResetArm();base.OnClick();return;}
  ResetArm();armedAt=CurrentTimestamp;armedContext=DataContext;armedParameter=CommandParameter;
  previousBorder=BorderBrush;previousOpacity=Opacity;
  SetValue(IsArmedKey,true);SetCurrentValue(BorderBrushProperty,new SolidColorBrush(Color.FromRgb(166,217,181)));SetCurrentValue(OpacityProperty,0.82);
  timer.Start();
 }
 protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e){ResetArm();base.OnLostKeyboardFocus(e);}
 protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
 {
  base.OnPropertyChanged(e);
  if(e.Property==CommandParameterProperty||e.Property==CommandProperty||(e.Property==VisibilityProperty&&Visibility!=Visibility.Visible))ResetArm();
 }
 private void ResetArm()
 {
  timer?.Stop();if(!IsArmed)return;
  SetValue(IsArmedKey,false);SetCurrentValue(BorderBrushProperty,previousBorder);SetCurrentValue(OpacityProperty,previousOpacity);
  armedContext=null;armedParameter=null;
 }
 [DllImport("user32.dll")]private static extern uint GetDoubleClickTime();
}
