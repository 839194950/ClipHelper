using LocalClipboard.Infrastructure.Storage;

namespace LocalClipboard.Infrastructure.Tests.Storage;

public sealed class RetryableOneTimeInitializerTests
{
    [Fact]
    public async Task EnsureInitialized_ConcurrentCallersWaitForSingleInitializer()
    {
        RetryableOneTimeInitializer initializer = new();
        using CountdownEvent ready = new(8);
        using CountdownEvent calling = new(8);
        using ManualResetEventSlim start = new(false);
        using ManualResetEventSlim initializerEntered = new(false);
        using ManualResetEventSlim releaseInitializer = new(false);
        int initializerCount = 0;

        Task[] tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            ready.Signal();
            start.Wait();
            calling.Signal();
            initializer.EnsureInitialized(() =>
            {
                Interlocked.Increment(ref initializerCount);
                initializerEntered.Set();
                releaseInitializer.Wait();
            });
        })).ToArray();

        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        start.Set();
        Assert.True(calling.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(initializerEntered.Wait(TimeSpan.FromSeconds(5)));

        try
        {
            Assert.Equal(1, Volatile.Read(ref initializerCount));
            Assert.All(tasks, task => Assert.False(task.IsCompleted));
        }
        finally
        {
            releaseInitializer.Set();
        }

        await Task.WhenAll(tasks);
        Assert.Equal(1, Volatile.Read(ref initializerCount));
    }

    [Fact]
    public void EnsureInitialized_FailedAttemptCanBeRetried()
    {
        RetryableOneTimeInitializer initializer = new();
        InvalidOperationException expected = new("initialization failed");
        int initializerCount = 0;

        InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
            initializer.EnsureInitialized(() =>
            {
                Interlocked.Increment(ref initializerCount);
                throw expected;
            }));

        initializer.EnsureInitialized(() => Interlocked.Increment(ref initializerCount));
        initializer.EnsureInitialized(() => Interlocked.Increment(ref initializerCount));

        Assert.Same(expected, actual);
        Assert.Equal(2, initializerCount);
    }
}
