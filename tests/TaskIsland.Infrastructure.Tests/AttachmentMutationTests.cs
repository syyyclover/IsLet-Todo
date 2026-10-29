using TaskIsland.Application;
using TaskIsland.Infrastructure;
using TaskIsland.Domain;
using Xunit;

public class AttachmentMutationTests
{
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = new(2026,10,29,0,0,0,TimeSpan.Zero); public override DateTimeOffset GetUtcNow()=>Now; }
    [Fact] public async Task CopyLeaseProtectsMutationsAndAvoidsPastDeadlineSpin()
    {
        var path=Path.Combine(Path.GetTempPath(),"TaskIsland.CopyGate.Tests",Guid.NewGuid().ToString("N"));
        try
        {
            await using var store=new SqliteTaskStore(path); await store.InitializeAsync();
            var clock=new Clock();var gate=new TaskMutationGate();var service=new TaskService(store,clock,gate);
            var item=await service.CreateTaskAsync("复制保护",null);
            using(var lease=gate.Acquire(item.Id))
            {
                await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ArchiveTaskAsync(item.Id,ArchiveReason.Manual));
                await Assert.ThrowsAsync<InvalidOperationException>(()=>service.UpdateTaskAsync(item.Id,"标题",clock.Now.AddHours(1)));
                await service.UpdateTaskAsync(item.Id,"允许只修改文字",null);
                clock.Now=clock.Now.AddHours(25); await service.ReconcileTimeAsync();
                Assert.Single((await service.ListActiveAsync()).Tasks); Assert.Null(await service.GetNextEventAtAsync());
            }
            await service.ReconcileTimeAsync(); Assert.Empty((await service.ListActiveAsync()).Tasks);
            Assert.Single(await service.ListArchivedAsync());
        }
        finally {if(Directory.Exists(path))Directory.Delete(path,true);}
    }
}
