using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Yatra.AgentIntegration;
using Yatra.Contracts.Components;
using Yatra.Contracts.Forms;
using Yatra.Contracts.Messages;
using Yatra.Contracts.Routing;
using Yatra.Engine.Persistence;
using Yatra.Engine.Routing;
using Yatra.Orchestration;
using Yatra.Orchestration.Components;
using Yatra.Orchestration.DependencyInjection;
using Yatra.Orchestration.Llm;
namespace Yatra.Engine.Tests;
public sealed class ComponentRecoveryTests
{
    private static string Folder() => Path.Combine(Path.GetTempPath(), "Yatra.Component.Recovery.Tests", Guid.NewGuid().ToString("N"));
    private static IOptions<YatraEngineOptions> OptionsFor(string folder) => Options.Create(new YatraEngineOptions { StorageFolder = folder });
    private static FileEngineStateStore Open(string folder) => new(OptionsFor(folder));
    private static YatraMessage Request() => new() {
        MessageId = YatraUlid.NewUlid(), ConversationId = YatraUlid.NewUlid(), Type = YatraMessageTypes.ComponentRequest,
        Source = "orchestrator:sample", Destination = "ui:current-conversation", CreatedAtUtc = DateTimeOffset.UtcNow, ExpectsReply = true,
        Payload = JsonSerializer.SerializeToElement(CustomerComparisonFactory.Create(), YatraJson.SerializerOptions)
    };
    private static YatraMessage Response(YatraMessage request, string action = "select") => request with {
        MessageId = YatraUlid.NewUlid(), Type = YatraMessageTypes.ComponentResponse, Source = "ui:current-conversation", Destination = "engine:inbound",
        ExpectsReply = false, InReplyTo = request.MessageId,
        Payload = JsonSerializer.SerializeToElement(new { componentId = "customer-comparison", componentVersion = "1.0", instanceId = request.Payload.GetProperty("instanceId").GetString(), action,
            data = action == "cancel" ? JsonSerializer.SerializeToElement(new {}) : JsonSerializer.SerializeToElement(new { customerId = "C101" }) })
    };
    private static AgentOutput Output(YatraMessage request) => new(request, new ReturnPath("orchestrator/sample"));
    private static YatraMessage Outcome(YatraMessage response, bool failed = false) => response with {
        MessageId = YatraUlid.NewUlid(), Type = failed ? YatraMessageTypes.Error : YatraMessageTypes.AgentText, InReplyTo = response.MessageId,
        Source = "orchestrator:sample", Destination = "ui:current-conversation", Payload = failed ? JsonSerializer.SerializeToElement(new { code = "business_failure", message = "Failed", retryable = false }) : JsonSerializer.SerializeToElement(new { text = "Done" })
    };
    [Fact] public async Task PendingComponentAndClaimRestoreWithExactMetadataAndPrivatePath()
    {
        var folder = Folder(); var request = Request();
        using (var state = Open(folder)) {
            await state.PublishAsync(Output(request), default); await state.ClaimForResponseAsync(request.MessageId, request.ConversationId);
        }
        using var restored = Open(folder); var pending = await restored.ResolveAsync(request.MessageId, request.ConversationId);
        Assert.NotNull(pending); Assert.Equal("orchestrator/sample", pending.ReturnPath.QualifiedPath); Assert.Equal("customer-comparison/1.0", pending.ValidatorKey);
        Assert.Equal(request.Payload.GetProperty("instanceId").GetString(), pending.ComponentRequest!.InstanceId);
        Assert.Equal("C101", pending.ComponentRequest.Props.GetProperty("customers")[0].GetProperty("id").GetString());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await using var outbox = restored.ReadOutboundAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await outbox.MoveNextAsync()); Assert.True(JsonNode.DeepEquals(JsonNode.Parse(request.Payload.GetRawText()), JsonNode.Parse(outbox.Current.Message.Payload.GetRawText())));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task AcceptedResponseRecoversBeforeOutcomeAndRetryDoesNotDuplicateAdapterWork(bool claimed)
    {
        var folder = Folder(); var request = Request(); var response = Response(request);
        using (var state = Open(folder)) {
            await state.PublishAsync(Output(request), default); Assert.False(await state.AcceptAsync(response, default));
            if (claimed) await state.ClaimForResponseAsync(request.MessageId, request.ConversationId);
        }
        var adapter = new CountingAdapter();
        await using (var factory = Factory(folder, adapter)) {
            using var client = factory.CreateClient();
            var acknowledgement = await client.PostAsJsonAsync(Url(request), response, YatraJson.SerializerOptions);
            Assert.Equal(HttpStatusCode.Accepted, acknowledgement.StatusCode);
            Assert.True((await acknowledgement.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());
            await WaitFor(factory, request, m => m.InReplyTo == response.MessageId && m.Type == YatraMessageTypes.AgentText);
            Assert.Equal(1, adapter.ResponseCalls);
        }
        var afterRestart = new CountingAdapter();
        await using (var factory = Factory(folder, afterRestart)) {
            using var client = factory.CreateClient();
            var repeat = await client.PostAsJsonAsync(Url(request), response, YatraJson.SerializerOptions); Assert.Equal(HttpStatusCode.Accepted, repeat.StatusCode);
            Assert.True((await repeat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());
            var changed = response with { Payload = Response(request, "cancel").Payload };
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Url(request), changed, YatraJson.SerializerOptions)).StatusCode);
            var history = await factory.Services.GetRequiredService<IConversationMessageStore>().ReadConversationAsync(request.ConversationId, default);
            Assert.Single(history, m => m.Type == YatraMessageTypes.AgentText && m.InReplyTo == response.MessageId); Assert.Equal(0, afterRestart.ResponseCalls);
        }
    }
    [Fact] public async Task ConcurrentSameIdentityHttpRetriesProduceOneOutcomeAndChangedContentConflicts()
    {
        var folder = Folder(); var request = Request(); var response = Response(request);
        using (var state = Open(folder)) await state.PublishAsync(Output(request), default);
        var adapter = new CountingAdapter(); await using var factory = Factory(folder, adapter); using var client = factory.CreateClient();
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.PostAsJsonAsync(Url(request), response, YatraJson.SerializerOptions)));
        Assert.All(attempts, attempt => Assert.Equal(HttpStatusCode.Accepted, attempt.StatusCode));
        await WaitFor(factory, request, m => m.InReplyTo == response.MessageId && m.Type == YatraMessageTypes.AgentText); Assert.Equal(1, adapter.ResponseCalls);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Url(request), response with { Payload = Response(request, "cancel").Payload }, YatraJson.SerializerOptions)).StatusCode);
    }
    [Fact] public async Task CommitBeforeOutcomeDeliveryRestoresOutboxWithoutRepeatingAdapter()
    {
        var folder = Folder(); var request = Request(); var response = Response(request); var outcome = Outcome(response);
        using (var state = Open(folder)) {
            await state.PublishAsync(Output(request), default); await state.AcceptAsync(response, default); await state.ClaimForResponseAsync(request.MessageId, request.ConversationId);
            var history = new FileConversationMessageStore(OptionsFor(folder), NullLogger<FileConversationMessageStore>.Instance);
            await history.AppendAsync(request, default); await history.AppendAsync(response, default);
            await state.CommitInboundAsync(response, [new AgentOutput(outcome)], FormResponseAction.Submit, default);
        }
        var adapter = new CountingAdapter(); await using var factory = Factory(folder, adapter); using var client = factory.CreateClient();
        var restored = await WaitFor(factory, request, m => m.MessageId == outcome.MessageId); Assert.Equal(outcome.MessageId, restored.MessageId); Assert.Equal(0, adapter.ResponseCalls);
        var pending = await factory.Services.GetRequiredService<IPendingReplyRegistry>().ClaimForResponseAsync(request.MessageId, request.ConversationId);
        Assert.Equal(PendingReplyResolutionStatus.AlreadyCompleted, pending.Status);
    }
    [Theory] [InlineData("completed")] [InlineData("cancelled")] [InlineData("expired")] [InlineData("failed")]
    public async Task TerminalComponentsRemainTerminalAfterRestartAndRejectNewResponse(string terminal)
    {
        var folder = Folder(); var request = Request(); var response = Response(request, terminal == "cancelled" ? "cancel" : "select");
        using (var state = Open(folder)) {
            await state.PublishAsync(Output(request), default);
            if (terminal == "expired") await state.ExpireAsync(DateTimeOffset.UtcNow.AddHours(1));
            else {
                await state.AcceptAsync(response, default); await state.ClaimForResponseAsync(request.MessageId, request.ConversationId);
                await state.CommitInboundAsync(response, [new AgentOutput(Outcome(response, terminal == "failed"))], terminal == "cancelled" ? FormResponseAction.Cancel : FormResponseAction.Submit, default);
            }
        }
        var adapter = new CountingAdapter(); await using var factory = Factory(folder, adapter); using var client = factory.CreateClient();
        var next = Response(request); await client.PostAsJsonAsync(Url(request), next, YatraJson.SerializerOptions);
        var error = await WaitFor(factory, request, m => m.Type == YatraMessageTypes.Error && m.InReplyTo == next.MessageId);
        Assert.Equal(terminal == "expired" ? "pending_reply_expired" : "duplicate_response", error.Payload.GetProperty("code").GetString()); Assert.Equal(0, adapter.ResponseCalls);
    }
    [Fact] public async Task ExpiryAndPublicOutcomeAreAtomicAndNotDuplicatedAcrossRestarts()
    {
        var folder = Folder(); var request = Request();
        using (var state = Open(folder)) {
            await state.PublishAsync(Output(request), default); await state.ExpireAsync(DateTimeOffset.UtcNow.AddHours(1));
            await state.ExpireAsync(DateTimeOffset.UtcNow.AddHours(2));
        }
        using (var state = Open(folder)) await state.ExpireAsync(DateTimeOffset.UtcNow.AddHours(3));
        using (var state = Open(folder)) {
            var snapshot = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "engine-state.json")))!;
            var notifications = snapshot["outbound"]!.AsObject().Select(pair => pair.Value!["message"]!["message"]!).Where(m => m["type"]!.GetValue<string>() == "status").ToArray();
            Assert.Single(notifications); Assert.Equal(request.MessageId, notifications[0]["inReplyTo"]!.GetValue<string>());
            Assert.DoesNotContain("returnPath", notifications[0].ToJsonString(), StringComparison.OrdinalIgnoreCase);
        }
    }
    [Fact] public async Task FailedAtomicCommitLeavesInboxPendingAndOriginalMetadataIntact()
    {
        var folder = Folder(); var request = Request(); var response = Response(request);
        using (var state = Open(folder)) {
            await state.PublishAsync(Output(request), default); await state.AcceptAsync(response, default); await state.ClaimForResponseAsync(request.MessageId, request.ConversationId);
            var before = File.ReadAllText(Path.Combine(folder, "engine-state.json"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => state.CommitInboundAsync(response, [new AgentOutput(Request())], FormResponseAction.Submit, default));
            Assert.Equal(before, File.ReadAllText(Path.Combine(folder, "engine-state.json")));
        }
        using var restored = Open(folder); Assert.NotNull(await restored.ResolveAsync(request.MessageId, request.ConversationId));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)); await using var inbox = restored.ReadInboundAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await inbox.MoveNextAsync()); Assert.Equal(response.MessageId, inbox.Current.MessageId);
    }
    [Fact] public async Task OverduePendingComponentExpiresDuringStartupBeforeReplay()
    {
        var folder = Folder(); var request = Request();
        using (var state = Open(folder)) await state.PublishAsync(Output(request), default);
        var path = Path.Combine(folder, "engine-state.json");
        var snapshot = JsonNode.Parse(File.ReadAllText(path))!;
        var reply = snapshot["replies"]![request.MessageId]!;
        reply["createdAtUtc"] = DateTimeOffset.UtcNow.AddHours(-2);
        reply["expiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(-1);
        File.WriteAllText(path, snapshot.ToJsonString());
        using var restored = Open(folder);
        Assert.Equal(PendingReplyResolutionStatus.Expired, (await restored.ClaimForResponseAsync(request.MessageId, request.ConversationId)).Status);
        var durable = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Single(durable["outbound"]!.AsObject(), pair => pair.Value!["message"]!["message"]!["type"]!.GetValue<string>() == "status");
    }
    [Theory] [InlineData("missing")] [InlineData("version")] [InlineData("instance")] [InlineData("validator")] [InlineData("properties")] [InlineData("status")]
    public async Task CorruptComponentMetadataFailsClosedWithoutRewritingSnapshot(string corruption)
    {
        var folder = Folder(); var request = Request(); using (var state = Open(folder)) await state.PublishAsync(Output(request), default);
        var path = Path.Combine(folder, "engine-state.json"); var snapshot = JsonNode.Parse(File.ReadAllText(path))!; var reply = snapshot["replies"]![request.MessageId]!;
        switch (corruption) {
            case "missing": reply["componentRequest"] = null; break;
            case "version": reply["componentRequest"]!["componentVersion"] = "latest"; break;
            case "instance": reply["componentRequest"]!["instanceId"] = "invalid"; break;
            case "validator": reply["validatorKey"] = "wrong"; break;
            case "properties": reply["componentRequest"]!["props"]!["customers"]![0]!["id"] = "changed"; break;
            case "status": reply["status"] = "unsupported"; break;
        }
        var corrupt = snapshot.ToJsonString(); File.WriteAllText(path, corrupt);
        var error = Record.Exception(() => Open(folder));
        Assert.True(error is JsonException or InvalidDataException, error?.ToString());
        Assert.Equal(corrupt, File.ReadAllText(path));
    }
    [Fact] public async Task OptionalDescriptionFromPhase2SnapshotRestoresWithoutFalseCorruption()
    {
        var folder = Folder(); var request = Request(); var payload = JsonNode.Parse(request.Payload.GetRawText())!.AsObject(); payload.Remove("description");
        request = request with { Payload = JsonSerializer.SerializeToElement(payload) };
        using (var state = Open(folder)) await state.PublishAsync(Output(request), default);
        using var restored = Open(folder); Assert.NotNull(await restored.ResolveAsync(request.MessageId, request.ConversationId));
    }
    [Fact] public async Task TransientRequesterFailureRetriesDurableInboxWithoutPublishingFalseTerminalFailure()
    {
        var folder = Folder(); var request = Request(); var response = Response(request);
        using (var state = Open(folder)) { await state.PublishAsync(Output(request), default); await state.AcceptAsync(response, default); }
        var adapter = new CountingAdapter(throwOnce: true); await using var factory = Factory(folder, adapter); using var client = factory.CreateClient();
        await WaitFor(factory, request, m => m.InReplyTo == response.MessageId && m.Type == YatraMessageTypes.AgentText);
        var history = await factory.Services.GetRequiredService<IConversationMessageStore>().ReadConversationAsync(request.ConversationId, default);
        Assert.Equal(2, adapter.ResponseCalls); Assert.Single(history, m => m.InReplyTo == response.MessageId && m.Type == YatraMessageTypes.AgentText);
        Assert.DoesNotContain(history, m => m.Type == YatraMessageTypes.Error && m.InReplyTo == response.MessageId);
    }
    private static string Url(YatraMessage request) => $"/api/conversations/{request.ConversationId}/messages";
    private static WebApplicationFactory<Program> Factory(string folder, CountingAdapter adapter) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
        builder.UseSetting("Yatra:StorageFolder", folder); builder.ConfigureServices(services => { services.AddSampleYatraOrchestration(); services.AddSingleton<IOrchestrationAgentAdapter>(adapter); });
    });
    private static async Task<YatraMessage> WaitFor(WebApplicationFactory<Program> factory, YatraMessage request, Func<YatraMessage, bool> predicate) {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)); var store = factory.Services.GetRequiredService<IConversationMessageStore>();
        while (true) { var match = (await store.ReadConversationAsync(request.ConversationId, timeout.Token)).FirstOrDefault(predicate); if (match is not null) return match; await Task.Delay(20, timeout.Token); }
    }
    private sealed class CountingAdapter(bool throwOnce = false) : IOrchestrationAgentAdapter {
        public int ResponseCalls; public string AgentType => "orchestrator:sample"; private readonly SampleOrchestrationAgentAdapter _inner = new(new FakeLlmClient());
        public Task HandleAsync(AgentInput input, IAgentOutputSink output, CancellationToken token) {
            if (input.Message.Type == YatraMessageTypes.ComponentResponse && Interlocked.Increment(ref ResponseCalls) == 1 && throwOnce) throw new IOException("Simulated interrupted requester.");
            return _inner.HandleAsync(input, output, token);
        }
    }
}


