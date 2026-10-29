namespace TaskIsland.Presentation;
public static class UserFacingErrors
{
 public static string Message(Exception error)
 {
  if(error is not (ArgumentException or InvalidOperationException)) return "操作失败，请重试或检查本地存储。";
  var text=error.Message.Split('\r','\n')[0];
  if(error is ArgumentException)
  {
   foreach(var marker in new[]{" (Parameter "," (参数","（参数"})
   {var index=text.IndexOf(marker,StringComparison.Ordinal);if(index>=0)text=text[..index];}
  }
  return text;
 }
}
