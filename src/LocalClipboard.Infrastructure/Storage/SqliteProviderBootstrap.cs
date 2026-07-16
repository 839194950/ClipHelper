using System.Runtime.ExceptionServices;

namespace LocalClipboard.Infrastructure.Storage;

internal sealed class RetryableOneTimeInitializer
{
    private sealed class InitializationAttempt
    {
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ExceptionDispatchInfo? Failure { get; set; }
    }

    private readonly object syncRoot = new();
    private bool initialized;
    private InitializationAttempt? currentAttempt;

    public void EnsureInitialized(Action initializer)
    {
        ArgumentNullException.ThrowIfNull(initializer);
        if (Volatile.Read(ref initialized)) return;

        InitializationAttempt attempt;
        bool runsInitializer = false;
        lock (syncRoot)
        {
            if (initialized) return;

            attempt = currentAttempt ?? new InitializationAttempt();
            if (currentAttempt is null)
            {
                currentAttempt = attempt;
                runsInitializer = true;
            }
        }

        if (!runsInitializer)
        {
            attempt.Completion.Task.GetAwaiter().GetResult();
            attempt.Failure?.Throw();
            return;
        }

        try
        {
            initializer();
        }
        catch (Exception exception)
        {
            attempt.Failure = ExceptionDispatchInfo.Capture(exception);
            lock (syncRoot)
            {
                currentAttempt = null;
            }

            attempt.Completion.SetResult(false);
            throw;
        }

        lock (syncRoot)
        {
            Volatile.Write(ref initialized, true);
            currentAttempt = null;
        }

        attempt.Completion.SetResult(true);
    }
}

internal static class SqliteProviderBootstrap
{
    private static readonly RetryableOneTimeInitializer Initializer = new();

    public static void EnsureInitialized() => Initializer.EnsureInitialized(static () =>
    {
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_winsqlite3());
        SQLitePCL.raw.FreezeProvider(true);
    });
}
