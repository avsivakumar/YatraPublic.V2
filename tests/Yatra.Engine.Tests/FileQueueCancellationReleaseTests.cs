using Microsoft.Extensions.Options;
using Yatra.Engine.Persistence;
namespace Yatra.Engine.Tests;

public sealed class FileQueueCancellationReleaseTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AlreadyCancelledQueueReadThrowsInsteadOfSilentlyEnding(bool inbound)
    {
        using var store = new FileEngineStateStore(Options.Create(new YatraEngineOptions {
            StorageFolder = Path.Combine(Path.GetTempPath(), "Yatra.Release.Cancel", Guid.NewGuid().ToString("N"))
        }));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        if (inbound) {
            await using var reader = store.ReadInboundAsync(cancellation.Token).GetAsyncEnumerator();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.MoveNextAsync());
        } else {
            await using var reader = store.ReadOutboundAsync(cancellation.Token).GetAsyncEnumerator();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.MoveNextAsync());
        }
    }
}

