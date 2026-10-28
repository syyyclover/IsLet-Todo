using System.Threading.Channels;

namespace TaskIsland.Application;

public sealed class TaskScheduler : IAsyncDisposable
{
    private readonly TaskService service;
    private readonly TimeProvider clock;
    private readonly CancellationTokenSource stop = new();
    private readonly Channel<bool> requests = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private ITimer? next;
    private ITimer? calibration;
    private Task? loop;
    public event Action? CycleCompleted;
    public event Action<Exception>? Failed;

    public TaskScheduler(TaskService service, TimeProvider clock) { this.service = service; this.clock = clock; }
    public void Start()
    {
        if (loop is not null) return;
        next = clock.CreateTimer(_ => RequestReconcile(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        calibration = clock.CreateTimer(_ => RequestReconcile(), null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
        service.Changed += RequestReconcile;
        loop = Task.Run(RunAsync);
        RequestReconcile();
    }
    public void RequestReconcile() => requests.Writer.TryWrite(true);
    private async Task RunAsync()
    {
        try
        {
            await foreach (var _ in requests.Reader.ReadAllAsync(stop.Token).ConfigureAwait(false))
            {
                try
                {
                    await service.ReconcileTimeAsync(stop.Token).ConfigureAwait(false);
                    var deadline = await service.GetNextEventAtAsync(stop.Token).ConfigureAwait(false);
                    var delay = deadline is { } instant ? instant - clock.GetUtcNow() : Timeout.InfiniteTimeSpan;
                    if (deadline is not null) delay = TimeSpan.FromMilliseconds(Math.Clamp(delay.TotalMilliseconds, 1, TimeSpan.FromDays(1).TotalMilliseconds));
                    next!.Change(delay, Timeout.InfiniteTimeSpan);
                    CycleCompleted?.Invoke();
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                catch (Exception ex) { Failed?.Invoke(ex); }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync()
    {
        service.Changed -= RequestReconcile;
        calibration?.Dispose(); next?.Dispose();
        stop.Cancel(); requests.Writer.TryComplete();
        if (loop is not null) await loop.ConfigureAwait(false);
        stop.Dispose();
    }
}
