using TaskIsland.Application;
using TaskIsland.Infrastructure;
using System.IO;
using TaskIsland.Domain;
using TaskIsland.Presentation;
using Xunit;
public class AttachmentInteractionTests
{
 [Fact] public async Task AttachmentPageRendersAndTaskBadgeUpdates()
 {
  var root=Path.Combine(Path.GetTempPath(),"TaskIsland.Attachment.Render.Tests",Guid.NewGuid().ToString("N"));
  try
  {
   Directory.CreateDirectory(root);var source=Path.Combine(root,"测试文档.txt");await File.WriteAllTextAsync(source,"source retained");
   var data=Path.Combine(root,"data");await using var store=new SqliteTaskStore(data);await store.InitializeAsync();
   var gate=new TaskMutationGate();var tasks=new TaskService(store,TimeProvider.System,gate);var item=await tasks.CreateTaskAsync("含附件任务",null);
   var files=new AttachmentService(store,new ManagedAttachmentFiles(data),TimeProvider.System,gate);
   IslandMotionTests.RunSta(window=>
   {
    void Finish(Task operation){var watch=System.Diagnostics.Stopwatch.StartNew();while(!operation.IsCompleted&&watch.Elapsed<TimeSpan.FromSeconds(5))IslandMotionTests.Pump(10);Assert.True(operation.IsCompletedSuccessfully,operation.Exception?.ToString());}
    Finish(window.AttachTasksAsync(tasks));window.AttachAttachments(files);
    var vm=(IslandViewModel)window.DataContext;Finish(vm.AddAttachmentsAsync(item.Id,[source]));IslandMotionTests.Pump(300);
    Assert.Equal(1,Assert.Single(vm.Tasks).AttachmentCount);
    Assert.True(((System.Windows.FrameworkElement)window.FindName("AttachmentsPanel")).IsVisible);
    Assert.Equal("测试文档.txt",Assert.Single(vm.Attachments).OriginalName);
    Assert.False(vm.IsError);Assert.True(File.Exists(source));
   });
  }
  finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
 private static AttachmentItem Item()=>new(Guid.NewGuid(),Guid.NewGuid(),"来源 图片.png","attachments/owned.png",12,"image",DateTimeOffset.UtcNow);
 [Fact] public void RemovalRequiresConfirmationAndCancelKeepsVisibleRecord()
 {
  using var model=new IslandViewModel();var item=Item();model.Attachments.Add(item);
  model.RequestRemoveAttachmentCommand.Execute(item);Assert.True(model.ConfirmRemoveAttachment);
  model.CancelRemoveAttachmentCommand.Execute(null);Assert.False(model.ConfirmRemoveAttachment);Assert.Equal(item,Assert.Single(model.Attachments));
 }
 [Fact] public async Task CopyThenConfirmRemovalUpdatesUiAndPreservesSource()
 {
  var root=Path.Combine(Path.GetTempPath(),"TaskIsland.Attachment.UI.Tests",Guid.NewGuid().ToString("N"));
  try
  {
   Directory.CreateDirectory(root);var source=Path.Combine(root,"原始文件.txt");await File.WriteAllTextAsync(source,"原始内容");
   var data=Path.Combine(root,"data");await using var store=new SqliteTaskStore(data);await store.InitializeAsync();
   var gate=new TaskMutationGate();var tasks=new TaskService(store,TimeProvider.System,gate);
   var task=await tasks.CreateTaskAsync("含附件日程",null);
   var attachments=new AttachmentService(store,new ManagedAttachmentFiles(data),TimeProvider.System,gate);
   using var model=new IslandViewModel();await model.AttachAsync(tasks);model.AttachAttachmentService(attachments);
   await model.AddAttachmentsAsync(task.Id,[source]);
   Assert.False(model.IsAttachmentCopying);Assert.False(model.IsError);var item=Assert.Single(model.Attachments);
   model.RequestRemoveAttachmentCommand.Execute(item);Assert.Single(model.Attachments);
   await model.ConfirmAttachmentRemovalCommand.ExecuteAsync(null);
   Assert.Empty(model.Attachments);Assert.False(model.ConfirmRemoveAttachment);Assert.Equal("原始内容",await File.ReadAllTextAsync(source));
   model.DisposeAttachments();
  }
  finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
 [Fact] public void BusyCopyCannotStartOrDismissRemoval()
 {
  using var model=new IslandViewModel();model.IsBusy=true;
  model.RequestRemoveAttachmentCommand.Execute(Item());Assert.False(model.ConfirmRemoveAttachment);
  model.IsBusy=false;model.RequestRemoveAttachmentCommand.Execute(Item());model.IsBusy=true;
  model.CancelRemoveAttachmentCommand.Execute(null);Assert.True(model.ConfirmRemoveAttachment);
 }
}
