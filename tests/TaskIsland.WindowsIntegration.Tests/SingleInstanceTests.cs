using TaskIsland.WindowsIntegration;
using Xunit;

public class SingleInstanceTests
{
    [Fact]
    public async Task DisposalDoesNotWaitForUiSynchronizationContext()
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new QueuedUiContext());
            using (var primary = new SingleInstance("TaskIsland.Test." + Guid.NewGuid().ToString("N"))) primary.Listen();
            finished.SetResult();
        }) { IsBackground = true };
        thread.Start();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    private sealed class QueuedUiContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) { /* UI is exiting, no more pumping. */ }
    }

    [Fact]
    public async Task SecondaryForwardsToPrimaryAndCannotBecomePrimary()
    {
        var name = "TaskIsland.Test." + Guid.NewGuid().ToString("N");
        using var first = new SingleInstance(name);
        using var second = new SingleInstance(name);
        Assert.True(first.IsPrimary);
        Assert.False(second.IsPrimary);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Activated += value => received.TrySetResult(value);
        first.Listen();
        await second.ForwardAsync("show", CancellationToken.None);
        Assert.Equal("show", await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void PrimaryCanRestartAfterAllHandlesAreClosed()
    {
        var name = "TaskIsland.Test." + Guid.NewGuid().ToString("N");
        using (var first = new SingleInstance(name)) Assert.True(first.IsPrimary);
        using var restart = new SingleInstance(name);
        Assert.True(restart.IsPrimary);
    }

    [Fact]
    public async Task UnresponsiveClientDoesNotBlockSubsequentActivation()
    {
        var name = "TaskIsland.Test." + Guid.NewGuid().ToString("N");
        using var first = new SingleInstance(name);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Activated += value => received.TrySetResult(value);
        first.Listen();
        var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        using (var stalled = new System.IO.Pipes.NamedPipeClientStream(".", $"{name}.{sid}", System.IO.Pipes.PipeDirection.Out))
        {
            await stalled.ConnectAsync(3000);
            using var second = new SingleInstance(name);
            await second.ForwardAsync("show", CancellationToken.None);
            Assert.Equal("show", await received.Task.WaitAsync(TimeSpan.FromSeconds(6)));
        }
    }
}
