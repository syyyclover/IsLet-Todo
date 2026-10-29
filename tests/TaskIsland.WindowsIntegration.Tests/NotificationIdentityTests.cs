using System.IO;
using TaskIsland.WindowsIntegration;
using Xunit;
public class NotificationIdentityTests
{
 [Fact] public void ShortcutHasStableIdentityAndUpdatesOwnedTarget()
 {
  var folder=Path.Combine(Path.GetTempPath(),"TaskIsland.Identity.Tests",Guid.NewGuid().ToString("N"));
  try {
   Directory.CreateDirectory(folder); var first=Path.Combine(folder,"中文 空格.exe"); File.WriteAllText(first,"");
   var shortcut=NotificationIdentity.EnsureRegistered(first,folder,false);
   var metadata=NotificationIdentity.ReadShortcut(shortcut);
   Assert.Equal(first,metadata.TargetPath); Assert.Equal("TaskIsland.Desktop",metadata.AppUserModelId);
   var second=Path.Combine(folder,"新版本.exe"); File.WriteAllText(second,"");
   Assert.Equal(shortcut,NotificationIdentity.EnsureRegistered(second,folder,false));
   Assert.Equal(second,NotificationIdentity.ReadShortcut(shortcut).TargetPath);
  } finally {if(Directory.Exists(folder)) Directory.Delete(folder,true);}
 }
 [Fact] public void ValidForeignShortcutWithoutOurAppIdIsNotOverwritten()
 {
  var folder=Path.Combine(Path.GetTempPath(),"TaskIsland.Identity.Tests",Guid.NewGuid().ToString("N"));
  try {
   Directory.CreateDirectory(folder); var exe=Path.Combine(folder,"app.exe"); File.WriteAllText(exe,"");
   var path=Path.Combine(folder,"IsLet-Todo.lnk");
   var shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell",true)!)!;
   object? shortcut=null;
   try {
    dynamic automation=shell; shortcut=automation.CreateShortcut(path);
    dynamic link=shortcut; link.TargetPath=exe; link.Save();
   } finally {
    if(shortcut is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
   }
   var before=File.ReadAllBytes(path);
   Assert.Null(NotificationIdentity.ReadShortcut(path).AppUserModelId);
   Assert.Throws<InvalidOperationException>(()=>NotificationIdentity.EnsureRegistered(exe,folder,false));
   Assert.Equal(before,File.ReadAllBytes(path));
  } finally {if(Directory.Exists(folder)) Directory.Delete(folder,true);}
 }
 [Fact] public void ForeignShortcutIsPreservedAndQuotedExecutableRejected()
 {
  var folder=Path.Combine(Path.GetTempPath(),"TaskIsland.Identity.Tests",Guid.NewGuid().ToString("N"));
  try {
   Directory.CreateDirectory(folder); var exe=Path.Combine(folder,"app.exe"); File.WriteAllText(exe,"");
   var path=Path.Combine(folder,"IsLet-Todo.lnk"); File.WriteAllText(path,"foreign content");
   Assert.ThrowsAny<Exception>(()=>NotificationIdentity.EnsureRegistered(exe,folder,false));
   Assert.Equal("foreign content",File.ReadAllText(path));
   Assert.Throws<ArgumentException>(()=>NotificationIdentity.EnsureRegistered("\""+exe+"\"",folder,false));
  } finally {if(Directory.Exists(folder)) Directory.Delete(folder,true);}
 }
}
