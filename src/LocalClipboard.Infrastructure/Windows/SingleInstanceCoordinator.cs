using System.IO.Pipes;
using System.Text;

namespace LocalClipboard.Infrastructure.Windows;

public sealed class SingleInstanceCoordinator : IAsyncDisposable
{
    private readonly string mutexName;
    private readonly string pipeName;
    private readonly object mutexSync = new();
    private readonly ManualResetEventSlim releaseMutex = new(false);
    private Thread? mutexThread;
    private bool mutexAcquired;
    private bool acquisitionAttempted;
    private NamedPipeServerStream? activePipe;
    private int disposed;

    public SingleInstanceCoordinator(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        mutexName = $"Local\\{name}";
        pipeName = $"{name}.pipe";
    }

    public bool TryAcquirePrimary()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        lock (mutexSync)
        {
            if (mutexAcquired) return true;
            if (acquisitionAttempted) return false;
            acquisitionAttempted = true;
        }

        using var ready = new ManualResetEventSlim(false);
        Exception? failure = null;
        bool acquired = false;
        var thread = new Thread(() =>
        {
            try
            {
                using var mutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);
                acquired = createdNew;
                ready.Set();
                if (!createdNew) return;

                releaseMutex.Wait();
                mutex.ReleaseMutex();
            }
            catch (Exception exception)
            {
                failure = exception;
                ready.Set();
            }
        })
        {
            IsBackground = true,
            Name = "LocalClipboard.SingleInstanceMutex"
        };

        lock (mutexSync) mutexThread = thread;
        thread.Start();
        ready.Wait();
        if (failure is not null)
        {
            thread.Join();
            throw new InvalidOperationException("Unable to acquire the single-instance mutex.", failure);
        }

        lock (mutexSync) mutexAcquired = acquired;
        if (!acquired) thread.Join();
        return acquired;
    }

    public async Task<string> WaitForMessageAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        if (Interlocked.CompareExchange(ref activePipe, server, null) is not null)
        {
            await server.DisposeAsync();
            throw new InvalidOperationException("A named-pipe wait is already active.");
        }

        try
        {
            await server.WaitForConnectionAsync(cancellationToken);
            using var reader = new StreamReader(server, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            return await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
        }
        finally
        {
            Interlocked.CompareExchange(ref activePipe, null, server);
            await server.DisposeAsync();
        }
    }

    public async Task SendShowMessageAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));

        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        await client.ConnectAsync(timeout.Token);
        await using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync("show".AsMemory(), timeout.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;

        NamedPipeServerStream? pipe = Interlocked.Exchange(ref activePipe, null);
        if (pipe is not null) await pipe.DisposeAsync();

        Thread? thread;
        lock (mutexSync)
        {
            thread = mutexThread;
            mutexThread = null;
        }

        releaseMutex.Set();
        thread?.Join();
        releaseMutex.Dispose();
    }
}
