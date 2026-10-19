using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Yatra.Contracts.Messages;
using Yatra.Orchestration.DependencyInjection;
namespace Yatra.Engine.Tests;

public sealed class SseReleaseReliabilityTests
{
    [Fact]
    public async Task IdleHeartbeatsDoNotPreventLaterMessagesOrCursorResume()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
            builder.UseSetting("Yatra:StorageFolder", Path.Combine(Path.GetTempPath(), "Yatra.Release.Sse", Guid.NewGuid().ToString("N")));
            builder.UseSetting("Yatra:SseHeartbeatSeconds", "1");
            builder.ConfigureServices(services => services.AddSampleYatraOrchestration());
        });
        using var client = factory.CreateClient(); var conversation = YatraUlid.NewUlid(); var id = YatraUlid.NewUlid();
        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{conversation}/events"), HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        var heartbeats = 0;
        while (heartbeats < 2) { var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(4)); Assert.NotNull(line); if (line.StartsWith(": heartbeat")) heartbeats++; }
        await client.PostAsJsonAsync($"/api/conversations/{conversation}/messages", new { messageId = id, type = "user.text", payload = new { text = "hello after idle" } });
        YatraMessage? outcome = null;
        while (outcome is null) {
            var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(4)); Assert.NotNull(line);
            if (line.StartsWith("data: ")) { var message = JsonSerializer.Deserialize<YatraMessage>(line[6..], YatraJson.SerializerOptions)!; if (message.Type == YatraMessageTypes.AgentText && message.InReplyTo == id) outcome = message; }
        }
        Assert.True(YatraUlid.IsValid(outcome.MessageId));
        using var resumed = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{conversation}/events?lastEventId={outcome.MessageId}"), HttpCompletionOption.ResponseHeadersRead);
        using var resumedReader = new StreamReader(await resumed.Content.ReadAsStreamAsync());
        while (true) { var line = await resumedReader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(4)); Assert.NotNull(line); Assert.False(line.StartsWith("data: ")); if (line.StartsWith(": heartbeat")) break; }
    }
}
