using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Yatra.AgentIntegration;
using Yatra.Contracts.Components;
using Yatra.Contracts.Messages;
using Yatra.Contracts.Routing;
using Yatra.Engine.Persistence;
using Yatra.Engine.Queues;
using Yatra.Engine.Routing;
using Yatra.Orchestration.Components;
using Yatra.Orchestration.DependencyInjection;
namespace Yatra.Engine.Tests;

public sealed class ComponentExtensibilityTests
{
    [Fact] public async Task SecondComponentUsesUnchangedGenericHttpRoutingAndCorrelatedCompletion()
    {
        await using var factory = Factory(); using var client = factory.CreateClient(); var conversation = YatraUlid.NewUlid();
        var id = YatraUlid.NewUlid(); var capabilities = new[] { new ComponentCapability("status-summary", "1.0", ["acknowledge"]) };
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(Url(conversation), new { messageId = id, type = "user.text", payload = new { text = "show status summary", componentCapabilities = capabilities } }, YatraJson.SerializerOptions)).StatusCode);
        var request = await WaitFor(factory, conversation, m => m.InReplyTo == id && m.Type == YatraMessageTypes.ComponentRequest);
        var pending = await factory.Services.GetRequiredService<IPendingReplyRegistry>().ResolveAsync(request.MessageId, conversation);
        Assert.Equal("status-summary/1.0", pending!.ValidatorKey); Assert.Equal("orchestrator/sample", pending.ReturnPath.QualifiedPath);
        var response = Respond(request); Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(Url(conversation), response, YatraJson.SerializerOptions)).StatusCode);
        var outcome = await WaitFor(factory, conversation, m => m.InReplyTo == response.MessageId && m.Type == YatraMessageTypes.AgentText);
        Assert.Contains("acknowledged", outcome.Payload.GetProperty("text").GetString()); Assert.DoesNotContain("returnPath", JsonSerializer.Serialize(outcome, YatraJson.SerializerOptions));
        Assert.Equal(PendingReplyResolutionStatus.AlreadyCompleted, (await factory.Services.GetRequiredService<IPendingReplyRegistry>().ClaimForResponseAsync(request.MessageId, conversation)).Status);
    }
    [Theory] [InlineData("compare customers")] [InlineData("show status summary")]
    public async Task ExplicitMissingCapabilityProducesPlainCorrelatedFallback(string command)
    {
        await using var factory = Factory(); using var client = factory.CreateClient(); var conversation = YatraUlid.NewUlid(); var id = YatraUlid.NewUlid();
        await client.PostAsJsonAsync(Url(conversation), new { messageId = id, type = "user.text", payload = new { text = command, componentCapabilities = Array.Empty<ComponentCapability>() } });
        var fallback = await WaitFor(factory, conversation, m => m.InReplyTo == id);
        Assert.Equal(YatraMessageTypes.AgentText, fallback.Type); Assert.False(fallback.ExpectsReply); Assert.Contains("does not advertise", fallback.Payload.GetProperty("text").GetString());
        Assert.Null(await factory.Services.GetRequiredService<IPendingReplyRegistry>().ResolveAsync(fallback.MessageId, conversation));
    }
    [Fact] public async Task AdvertisedCapabilityCannotAuthorizeInvalidResponseData()
    {
        await using var factory = Factory(); using var client = factory.CreateClient(); var request = await PublishStatus(factory);
        var response = Respond(request) with { Payload = JsonSerializer.SerializeToElement(new { componentId = "status-summary", componentVersion = "1.0", instanceId = request.Payload.GetProperty("instanceId").GetString(), action = "acknowledge", data = new { extra = "not allowed" } }) };
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(Url(request.ConversationId), response, YatraJson.SerializerOptions)).StatusCode);
        var error = await WaitFor(factory, request.ConversationId, m => m.InReplyTo == response.MessageId && m.Type == YatraMessageTypes.Error);
        Assert.Equal("invalid_component_response", error.Payload.GetProperty("code").GetString());
        Assert.NotNull(await factory.Services.GetRequiredService<IPendingReplyRegistry>().ResolveAsync(request.MessageId, request.ConversationId));
    }
    [Theory] [InlineData("string")] [InlineData("array")] [InlineData("depth")] [InlineData("nodes")] [InlineData("bytes")]
    public async Task ExcessiveBrowserResponsesAreRejectedBeforeAcceptance(string kind)
    {
        await using var factory = Factory(); using var client = factory.CreateClient(); var request = await PublishStatus(factory);
        object value = kind switch {
            "string" => new string('x', 4097), "array" => new int[101],
            "nodes" => Enumerable.Range(0, 21).Select(_ => new int[100]).ToArray(),
            "bytes" => Enumerable.Range(0, 17).Select(_ => new string('x', 4096)).ToArray(), _ => "leaf"
        };
        if (kind == "depth") for (var index = 0; index < 9; index++) value = new Dictionary<string, object> { ["child"] = value };
        var response = Respond(request) with { Payload = JsonSerializer.SerializeToElement(new { componentId = "status-summary", componentVersion = "1.0", instanceId = request.Payload.GetProperty("instanceId").GetString(), action = "acknowledge", data = new { value } }) };
        var rejected = await client.PostAsJsonAsync(Url(request.ConversationId), response, YatraJson.SerializerOptions); Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode); Assert.Contains("payloadLimits", await rejected.Content.ReadAsStringAsync());
        Assert.DoesNotContain(await factory.Services.GetRequiredService<IConversationMessageStore>().ReadConversationAsync(request.ConversationId, default), m => m.MessageId == response.MessageId);
        Assert.NotNull(await factory.Services.GetRequiredService<IPendingReplyRegistry>().ResolveAsync(request.MessageId, request.ConversationId));
    }
    [Fact] public async Task ExcessiveAdapterRequestFailsBeforePendingRegistrationOrPersistence()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Yatra.Limits.Tests", Guid.NewGuid().ToString("N")); var options = Options.Create(new YatraEngineOptions { StorageFolder = folder });
        var request = Request(StatusSummaryFactory.Create() with { Props = JsonSerializer.SerializeToElement(new { status = "Ready", summary = new string('x', 4097) }) });
        var registry = new InMemoryPendingReplyRegistry(); var sink = new EngineAgentOutputSink(new ChannelOutboundMessageQueue(), registry, options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sink.PublishAsync(new AgentOutput(request, new ReturnPath("orchestrator/sample")), default).AsTask());
        Assert.Null(await registry.ResolveAsync(request.MessageId, request.ConversationId));
        using var state = new FileEngineStateStore(options); var before = File.ReadAllText(Path.Combine(folder, "engine-state.json"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => state.PublishAsync(new AgentOutput(request, new ReturnPath("orchestrator/sample")), default)); Assert.Equal(before, File.ReadAllText(Path.Combine(folder, "engine-state.json")));
    }
    [Fact] public async Task CompactBudgetIsStableAcrossIndentedSnapshotPersistence()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Yatra.Limits.Restore.Tests", Guid.NewGuid().ToString("N")); var options = Options.Create(new YatraEngineOptions { StorageFolder = folder });
        var props = JsonSerializer.SerializeToElement(new { rows = Enumerable.Range(0, 10).Select(_ => Enumerable.Repeat(new string('x', 50), 100).ToArray()).ToArray() });
        var request = Request(StatusSummaryFactory.Create() with { ComponentId = "test-limit", Props = props }); Assert.True(ComponentPayloadLimits.IsWithinLimits(request.Payload));
        using (var state = new FileEngineStateStore(options)) await state.PublishAsync(new AgentOutput(request, new ReturnPath("orchestrator/sample")), default);
        using var restored = new FileEngineStateStore(options); Assert.NotNull(await restored.ResolveAsync(request.MessageId, request.ConversationId));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await using var outbox = restored.ReadOutboundAsync(timeout.Token).GetAsyncEnumerator(); Assert.True(await outbox.MoveNextAsync()); Assert.True(ComponentPayloadLimits.IsWithinLimits(outbox.Current.Message.Payload));
    }
    [Fact] public void EngineAndTimelineContainNoConcreteComponentBranch()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory); while (root is not null && !File.Exists(Path.Combine(root.FullName, "Yatra.slnx"))) root = root.Parent;
        var engine = Path.Combine(root!.FullName, "src/Yatra.Engine");
        var sources = Directory.EnumerateFiles(engine, "*.cs", SearchOption.AllDirectories).Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));
        foreach (var source in sources.Append(Path.Combine(root.FullName, "yatra-ui/src/components/MessageList.tsx"))) {
            var text = File.ReadAllText(source); Assert.DoesNotContain("customer-comparison", text); Assert.DoesNotContain("status-summary", text);
        }
    }
    private static YatraMessage Request(ComponentRequestPayload payload) => new() { MessageId = YatraUlid.NewUlid(), ConversationId = YatraUlid.NewUlid(), Type = YatraMessageTypes.ComponentRequest, Source = "orchestrator:sample", Destination = "ui", CreatedAtUtc = DateTimeOffset.UtcNow, ExpectsReply = true, Payload = JsonSerializer.SerializeToElement(payload, YatraJson.SerializerOptions) };
    private static YatraMessage Respond(YatraMessage request) => request with { MessageId = YatraUlid.NewUlid(), Type = YatraMessageTypes.ComponentResponse, ExpectsReply = false, InReplyTo = request.MessageId, Payload = JsonSerializer.SerializeToElement(new { componentId = "status-summary", componentVersion = "1.0", instanceId = request.Payload.GetProperty("instanceId").GetString(), action = "acknowledge", data = new {} }) };
    private static async Task<YatraMessage> PublishStatus(WebApplicationFactory<Program> factory) { var request = Request(StatusSummaryFactory.Create()); await factory.Services.GetRequiredService<IEngineAgentOutputSink>().PublishAsync(new AgentOutput(request, new ReturnPath("orchestrator/sample")), default); return request; }
    private static WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder => { builder.UseSetting("Yatra:StorageFolder", Path.Combine(Path.GetTempPath(), "Yatra.Extension.Tests", Guid.NewGuid().ToString("N"))); builder.ConfigureServices(services => services.AddSampleYatraOrchestration()); });
    private static string Url(string conversation) => $"/api/conversations/{conversation}/messages";
    private static async Task<YatraMessage> WaitFor(WebApplicationFactory<Program> factory, string conversation, Func<YatraMessage, bool> predicate) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)); var store = factory.Services.GetRequiredService<IConversationMessageStore>(); while (true) { var result = (await store.ReadConversationAsync(conversation, timeout.Token)).FirstOrDefault(predicate); if (result is not null) return result; await Task.Delay(20, timeout.Token); } }
}
