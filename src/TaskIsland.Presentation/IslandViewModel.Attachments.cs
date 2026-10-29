using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using TaskIsland.Application;
using TaskIsland.Domain;

namespace TaskIsland.Presentation;
public partial class IslandViewModel
{
 private AttachmentService? attachmentService;
 private CancellationTokenSource? attachmentCopyCancellation;
 private Guid? attachmentsTaskId;
 private AttachmentItem? removingAttachment;
 public Guid? AttachmentsTaskId=>attachmentsTaskId;
 public ObservableCollection<AttachmentItem> Attachments {get;}=[];
 [ObservableProperty] public partial bool IsAttachmentCopying {get;set;}
 [ObservableProperty] public partial double AttachmentProgressPercent {get;set;}
 [ObservableProperty] public partial bool ConfirmRemoveAttachment {get;set;}
 [ObservableProperty] public partial BitmapSource? AttachmentPreview {get;set;}
 [ObservableProperty] public partial AttachmentItem? AttachmentPreviewItem {get;set;}
 [ObservableProperty] public partial bool HasAttachmentPreview {get;set;}
 [ObservableProperty] public partial string AttachmentFailure {get;set;}="";
 public void AttachAttachmentService(AttachmentService value)
 {
  if(attachmentService is not null)attachmentService.ProgressChanged-=OnAttachmentProgress;
  attachmentService=value;value.ProgressChanged+=OnAttachmentProgress;
 }
 public void DisposeAttachments(){attachmentCopyCancellation?.Cancel();if(attachmentService is not null)attachmentService.ProgressChanged-=OnAttachmentProgress;}
 private void OnAttachmentProgress(AttachmentProgress progress)
 {
  void Update(){if(attachmentsTaskId==progress.TaskId)AttachmentProgressPercent=progress.TotalBytes<=0?0:100d*progress.BytesCopied/progress.TotalBytes;}
  if(context is null)Update();else context.Post(_=>Update(),null);
 }
 [RelayCommand] private Task OpenAttachmentsAsync(TaskRowViewModel row)=>RunAsync(async()=>
 {
  if(attachmentService is null)return;
  attachmentsTaskId=row.Item.Id; OnPropertyChanged(nameof(AttachmentsTaskId));
  AttachmentPreview=null;HasAttachmentPreview=false;AttachmentFailure="";ConfirmRemoveAttachment=false;
  await ReloadAttachmentsAsync();NavigationRequested?.Invoke(IslandPage.Attachments);
 });
 private async Task ReloadAttachmentsAsync()
 {
  if(attachmentService is null||attachmentsTaskId is not {} id)return;
  var items=await attachmentService.ListAsync(id);Attachments.Clear();foreach(var item in items)Attachments.Add(item);
 }
 public Task AddAttachmentsAsync(Guid taskId,IReadOnlyList<string> paths)=>RunAsync(async()=>
 {
  if(attachmentService is null)return;
  attachmentsTaskId=taskId;OnPropertyChanged(nameof(AttachmentsTaskId));
  AttachmentFailure="";AttachmentPreview=null;HasAttachmentPreview=false;IsAttachmentCopying=true;
  using var cancellation=new CancellationTokenSource();attachmentCopyCancellation=cancellation;
  NavigationRequested?.Invoke(IslandPage.Attachments);
  try
  {
   var results=await attachmentService.AddAsync(taskId,paths,cancellation.Token);
   var errors=results.Where(result=>!result.Success).Select(result=>result.Error??"附件添加失败").Distinct();
   AttachmentFailure=string.Join("；",errors);
   if(AttachmentFailure.Length>0){IsError=true;ErrorMessage=AttachmentFailure;}
   await ReloadAttachmentsAsync();await ReloadAsync();
  }
  finally{IsAttachmentCopying=false;attachmentCopyCancellation=null;}
 });
 [RelayCommand] private void CancelAttachmentCopy()=>attachmentCopyCancellation?.Cancel();
 [RelayCommand] private void RequestRemoveAttachment(AttachmentItem item)
 {if(IsBusy)return;removingAttachment=item;ConfirmRemoveAttachment=true;}
 [RelayCommand] private void CancelRemoveAttachment(){if(!IsBusy){ConfirmRemoveAttachment=false;removingAttachment=null;}}
 [RelayCommand] private Task ConfirmAttachmentRemovalAsync()=>RunAsync(async()=>
 {
  if(attachmentService is null||removingAttachment is not {} item)return;
  var complete=await attachmentService.RemoveAsync(item.TaskId,item.Id);
  ConfirmRemoveAttachment=false;removingAttachment=null;AttachmentPreview=null;HasAttachmentPreview=false;
  await ReloadAttachmentsAsync();await ReloadAsync();
  if(!complete){AttachmentFailure="附件记录已移除，文件正在等待清理。";IsError=true;ErrorMessage=AttachmentFailure;}
 });
 [RelayCommand] private Task PreviewAttachmentAsync(AttachmentItem item)=>RunAsync(async()=>
 {
  if(attachmentService is null)return;
  HasAttachmentPreview=false;AttachmentPreview=null;
  var extension=Path.GetExtension(item.OriginalName).ToLowerInvariant();
  if(extension is not (".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".webp"))return;
  AttachmentPreviewItem=item;
  var path=attachmentService.GetManagedPath(item);
  try
  {
   var bitmap=await Task.Run(()=>
   {
    using var stream=File.OpenRead(path);
    var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);var frame=decoder.Frames[0];
    var width=frame.PixelWidth;var height=frame.PixelHeight;stream.Position=0;
    var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;
    if(width/280d>=height/140d)image.DecodePixelWidth=Math.Min(width,280);else image.DecodePixelHeight=Math.Min(height,140);
    image.StreamSource=stream;image.EndInit();image.Freeze();return image;
   });
   AttachmentPreview=bitmap;HasAttachmentPreview=true;
  }
  catch(Exception ex) when(ex is IOException or NotSupportedException or FileFormatException or ArgumentException)
  {AttachmentFailure="图片无法预览，可尝试使用系统应用打开。";IsError=true;ErrorMessage=AttachmentFailure;}
 });
 [RelayCommand] private void CloseAttachmentPreview(){HasAttachmentPreview=false;AttachmentPreview=null;}
 [RelayCommand] private Task OpenAttachmentAsync(AttachmentItem item)=>RunAsync(async()=>
 {
  if(attachmentService is null)return;
  var path=attachmentService.GetManagedPath(item);
  if(!File.Exists(path))throw new InvalidOperationException("附件不存在，可移除此记录。");
  try{await Task.Run(()=>Process.Start(new ProcessStartInfo(path){UseShellExecute=true}));}
  catch(Exception){throw new InvalidOperationException("无法使用系统默认应用打开附件。");}
 });
}
