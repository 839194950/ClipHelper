using LocalClipboard.Infrastructure.Windows;

namespace LocalClipboard.App.IntegrationTests.Windows;

public sealed class SingleInstanceIntegrationTests
{
    [Fact]
    public async Task SecondCoordinatorSignalsPrimaryToShowWindow()
    {
        string name = "LocalClipboard.Tests." + Guid.NewGuid().ToString("N");
        await using var primary = new SingleInstanceCoordinator(name);
        await using var secondary = new SingleInstanceCoordinator(name);
        Assert.True(primary.TryAcquirePrimary());
        Assert.False(secondary.TryAcquirePrimary());

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task<string> message = primary.WaitForMessageAsync(timeout.Token);
        await secondary.SendShowMessageAsync(timeout.Token);

        Assert.Equal("show", await message);
    }
}
