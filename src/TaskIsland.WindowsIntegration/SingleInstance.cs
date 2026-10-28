using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace TaskIsland.WindowsIntegration;

/// <summary>The named object remains alive while the primary holds its handle; no thread-affine ownership.</summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex marker;
    private readonly CancellationTokenSource shutdown = new();
    private readonly string pipeName;
    private Task? listener;
    public bool IsPrimary { get; }
    public event Action<string>? Activated;
    public event Action<Exception>? Failed;

    public SingleInstance(string applicationId = "TaskIsland")
    {
        var user = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("无法识别当前用户");
        pipeName = $"{applicationId}.{user}";
        marker = new Mutex(false, $"Local\\{pipeName}", out var created);
        IsPrimary = created;
    }

    public void Listen()
    {
        if (!IsPrimary || listener is not null) return;
        listener = ListenAsync(shutdown.Token);
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromSeconds(2));
                // Fixed-size buffer bounds memory even if a local client sends an unending message.
                var bytes = new byte[256];
                var count = 0;
                while (count < bytes.Length)
                {
                    var read = await pipe.ReadAsync(bytes.AsMemory(count), deadline.Token).ConfigureAwait(false);
                    if (read == 0) break;
                    count += read;
                    if (Array.IndexOf(bytes, (byte)'\n', 0, count) >= 0) break;
                }
                var newline = Array.IndexOf(bytes, (byte)'\n', 0, count);
                if (newline >= 0) Activated?.Invoke(Encoding.UTF8.GetString(bytes, 0, newline));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { }
            catch (IOException ex) { Failed?.Invoke(ex); }
            catch (UnauthorizedAccessException ex) { Failed?.Invoke(ex); break; }
        }
    }

    public async Task ForwardAsync(string argument, CancellationToken ct)
    {
        if (Encoding.UTF8.GetByteCount(argument) > 200) throw new ArgumentException("激活参数过长", nameof(argument));
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(3000, ct);
        await pipe.WriteAsync(Encoding.UTF8.GetBytes(argument + "\n"), ct);
        await pipe.FlushAsync(ct);
    }

    public void Dispose()
    {
        shutdown.Cancel();
        // No UI work is awaited by the listener; cancellation unblocks pending pipe I/O.
        listener?.GetAwaiter().GetResult();
        marker.Dispose();
        shutdown.Dispose();
    }
}
