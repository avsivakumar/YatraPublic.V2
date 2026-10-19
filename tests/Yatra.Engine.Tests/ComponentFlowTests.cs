using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Yatra.AgentIntegration;
using Yatra.Contracts.Components;
using Yatra.Contracts.Messages;
using Yatra.Contracts.Routing;
using Yatra.Engine.Persistence;
using Yatra.Engine.Routing;
using Yatra.Orchestration.Components;
using Yatra.Orchestration.DependencyInjection;
namespace Yatra.Engine.Tests;
public sealed class ComponentFlowTests
{
    [Theory] [InlineData("select")] [InlineData("cancel")]
    public async Task InMemoryFlowRegistersBeforeDeliveryAndRoutesToOriginalRequester(string action)
    {
        var inbound = new Yatra.Engine.Queues.ChannelInboundMessageQueue();
        var outbound = new Yatra.Engine.Queues.ChannelOutboundMessageQueue();
        var registry = new InMemoryPendingReplyRegistry();
        var sink = new EngineAgentOutputSink(outbound, registry, Microsoft.Extensions.Options.Options.Create(new YatraEngineOptions()));
        var request = new YatraMessage
        {
            MessageId = YatraUlid.NewUlid(), ConversationId = YatraUlid.NewUlid(), Type = YatraMessageTypes.ComponentRequest,
            Source = "sample", Destination = "ui", CreatedAtUtc = DateTimeOffset.UtcNow, ExpectsReply = true,
            Payload = JsonSerializer.SerializeToElement(CustomerComparisonFactory.Create(), YatraJson.SerializerOptions)
        };
        await sink.PublishAsync(new AgentOutput(request, new ReturnPath("orchestrator/sample")), default);
        var pending = await registry.ResolveAsync(request.MessageId, request.ConversationId);
        Assert.NotNull(pending?.ComponentRequest);
        Assert.Equal("customer-comparison/1.0", pending.ValidatorKey);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reader = outbound.ReadAllAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync()); Assert.Equal(request.MessageId, reader.Current.Message.MessageId);
        using var worker = new Yatra.Engine.Workers.InboundMessageWorker(inbound, registry,
            new Yatra.Orchestration.SampleOrchestrationAgentAdapter(new Yatra.Orchestration.Llm.FakeLlmClient()), [], sink,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Yatra.Engine.Workers.InboundMessageWorker>.Instance,
            componentResponseValidators: [new CustomerComparisonValidator()]);
        await worker.StartAsync(default);
        try
        {
            var response = Response(request, action);
            await inbound.WriteAsync(request with { MessageId = response.messageId, Type = response.type, InReplyTo = request.MessageId, ExpectsReply = false, Payload = response.payload }, timeout.Token);
            Assert.True(await reader.MoveNextAsync()); Assert.Equal(response.messageId, reader.Current.Message.InReplyTo);
            Assert.Equal(YatraMessageTypes.AgentText, reader.Current.Message.Type);
        }
        finally { await worker.StopAsync(default); }
        var result = await registry.ClaimForResponseAsync(request.MessageId, request.ConversationId);
        Assert.Equal(action == "cancel" ? PendingReplyResolutionStatus.Cancelled : PendingReplyResolutionStatus.AlreadyCompleted, result.Status);
    }

    [Theory] [InlineData("select")] [InlineData("cancel")]
    public async Task HttpCommandToComponentResponseToCorrelatedSseOutcome(string action)
    {
        await using var factory = Factory(); using var client = factory.CreateClient(); var conversation = YatraUlid.NewUlid();
        var request = await Command(factory, client, conversation, "compare customers");
        Assert.True(request.ExpectsReply); Assert.True(YatraMessageValidator.Validate(request).IsValid);
        var pending = await factory.Services.GetRequiredService<IPendingReplyRegistry>().ResolveAsync(request.MessageId, conversation);
        Assert.Equal("customer-comparison/1.0", pending!.ValidatorKey);
        Assert.Equal(request.Payload.GetProperty("instanceId").GetString(), pending.ComponentRequest!.InstanceId);
        var response = Response(request, action); Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(Url(conversation), response)).StatusCode);
        var outcome = await WaitFor(factory, conversation, m => m.InReplyTo == response.messageId && m.Type == YatraMessageTypes.AgentText);
        Assert.Contains(action == "select" ? "C101" : "cancelled", outcome.Payload.GetProperty("text").GetString());
        var resolution = await factory.Services.GetRequiredService<IPendingReplyRegistry>().ClaimForResponseAsync(request.MessageId, conversation);
        Assert.Equal(action == "cancel" ? PendingReplyResolutionStatus.Cancelled : PendingReplyResolutionStatus.AlreadyCompleted, resolution.Status);
        using var events = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{conversation}/events"), HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await events.Content.ReadAsStreamAsync()); var received = new List<YatraMessage>();
        while (!received.Any(m => m.MessageId == outcome.MessageId)) {
            var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)); Assert.NotNull(line);
            if (line.StartsWith("data: ")) { Assert.DoesNotContain("returnPath", line, StringComparison.OrdinalIgnoreCase); received.Add(JsonSerializer.Deserialize<YatraMessage>(line[6..], YatraJson.SerializerOptions)!); }
        }
        Assert.Contains(received, m => m.Type == YatraMessageTypes.ComponentRequest); Assert.Contains(received, m => m.Type == YatraMessageTypes.ComponentResponse);
    }
    [Theory]
    [InlineData("componentId", "wrong")][InlineData("componentVersion", "2.0")][InlineData("instanceId", "01K00000000000000000000000")][InlineData("action", "delete")][InlineData("customerId", "C999")][InlineData("extra", "injected")]
    public async Task ForgedResponsesAreRejectedWithoutConsumingRequest(string field, string value)
    {
        await using var factory = Factory(); using var client = factory.CreateClient(); var conversation = YatraUlid.NewUlid();
        var request = await Command(factory, client, conversation, "compare customers"); var response = Response(request, "select");
        var payload = JsonNode.Parse(response.payload.GetRawText())!;
        if (field == "customerId") payload["data"]![field] = value; else payload[field] = value;
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(Url(conversation), new { response.messageId, response.type, response.inReplyTo, payload })).StatusCode);
        var error = await WaitFor(factory, conversation, m => m.InReplyTo == response.messageId && m.Type == YatraMessageTypes.Error);
        Assert.Equal("invalid_component_response", error.Payload.GetProperty("code").GetString());
        Assert.NotNull(await factory.Services.GetRequiredService<IPendingReplyRegistry>().ResolveAsync(request.MessageId, conversation));
    }
    [Fact] public async Task TwoComponentsAnsweredOutOfOrderWhileFormRemainsIndependent()
    {
        await using var factory = Factory(); using var client = factory.CreateClient(); var conversation = YatraUlid.NewUlid();
        var first = await Command(factory, client, conversation, "compare customers"); var second = await Command(factory, client, conversation, "compare customers");
        var form = await Command(factory, client, conversation, "show contact form");
        var registry = factory.Services.GetRequiredService<IPendingReplyRegistry>();
        foreach (var request in new[] { second, first }) {
            var response = Response(request, request == second ? "cancel" : "select");
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(Url(conversation), response)).StatusCode);
            await WaitFor(factory, conversation, m => m.InReplyTo == response.messageId && m.Type == YatraMessageTypes.AgentText);
        }
        Assert.NotNull(await registry.ResolveAsync(form.MessageId, conversation));
        var formResponse = new { messageId = YatraUlid.NewUlid(), type = "form.response", inReplyTo = form.MessageId, payload = new { formId = "contact", formVersion = "1.0", action = "cancel", values = new {} } };
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(Url(conversation), formResponse)).StatusCode);
        await WaitFor(factory, conversation, m => m.InReplyTo == formResponse.messageId && m.Type == YatraMessageTypes.AgentText);
    }
    [Fact] public async Task FormAndComponentCannotAnswerEachOthersPendingRequest()
    {
        await using var factory = Factory(); using var client = factory.CreateClient(); var conversation = YatraUlid.NewUlid();
        var component = await Command(factory, client, conversation, "compare customers"); var form = await Command(factory, client, conversation, "show contact form");
        var response = Response(component, "select") with { inReplyTo = form.MessageId };
        await client.PostAsJsonAsync(Url(conversation), response);
        await WaitFor(factory, conversation, m => m.InReplyTo == response.messageId && m.Type == YatraMessageTypes.Error);
        var formResponse = new { messageId = YatraUlid.NewUlid(), type = "form.response", inReplyTo = component.MessageId, payload = new { formId = "contact", formVersion = "1.0", action = "cancel", values = new {} } };
        await client.PostAsJsonAsync(Url(conversation), formResponse);
        await WaitFor(factory, conversation, m => m.InReplyTo == formResponse.messageId && m.Type == YatraMessageTypes.Error);
        var registry = factory.Services.GetRequiredService<IPendingReplyRegistry>();
        Assert.NotNull(await registry.ResolveAsync(form.MessageId, conversation)); Assert.NotNull(await registry.ResolveAsync(component.MessageId, conversation));
    }
    [Theory] [InlineData("wrong/path", "customer-comparison")] [InlineData("orchestrator/sample", "unregistered")]
    public async Task WrongPrivatePathOrMissingValidatorFailsClosed(string path, string componentId)
    {
        await using var factory = Factory(); using var client = factory.CreateClient(); var conversation = YatraUlid.NewUlid();
        var request = new YatraMessage { MessageId = YatraUlid.NewUlid(), ConversationId = conversation, Type = YatraMessageTypes.ComponentRequest, Source = "test", Destination = "ui", ExpectsReply = true, CreatedAtUtc = DateTimeOffset.UtcNow, Payload = JsonSerializer.SerializeToElement(CustomerComparisonFactory.Create() with { ComponentId = componentId }, YatraJson.SerializerOptions) };
        await factory.Services.GetRequiredService<IEngineAgentOutputSink>().PublishAsync(new AgentOutput(request, new ReturnPath(path)), default);
        var response = Response(request, "select"); await client.PostAsJsonAsync(Url(conversation), response);
        var error = await WaitFor(factory, conversation, m => m.InReplyTo == response.messageId && m.Type == YatraMessageTypes.Error);
        Assert.Equal("invalid_component_response", error.Payload.GetProperty("code").GetString());
    }
    private static WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
        builder.UseSetting("Yatra:StorageFolder", Path.Combine(Path.GetTempPath(), "Yatra.Component.Tests", Guid.NewGuid().ToString("N")));
        builder.ConfigureServices(services => services.AddSampleYatraOrchestration());
    });
    private static string Url(string conversation) => $"/api/conversations/{conversation}/messages";
    private static async Task<YatraMessage> Command(WebApplicationFactory<Program> factory, HttpClient client, string conversation, string text) {
        var id = YatraUlid.NewUlid(); Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(Url(conversation), new { messageId = id, type = "user.text", payload = new { text } })).StatusCode);
        return await WaitFor(factory, conversation, m => m.InReplyTo == id && m.ExpectsReply);
    }
    private static async Task<YatraMessage> WaitFor(WebApplicationFactory<Program> factory, string conversation, Func<YatraMessage, bool> predicate) {
        var store = factory.Services.GetRequiredService<IConversationMessageStore>(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (true) { var match = (await store.ReadConversationAsync(conversation, timeout.Token)).FirstOrDefault(predicate); if (match is not null) return match; await Task.Delay(20, timeout.Token); }
    }
    private sealed record Submission(string messageId, string type, string inReplyTo, JsonElement payload);
    private static Submission Response(YatraMessage request, string action) {
        var component = request.Payload.Deserialize<ComponentRequestPayload>(YatraJson.SerializerOptions)!;
        return new(YatraUlid.NewUlid(), "component.response", request.MessageId, JsonSerializer.SerializeToElement(new { componentId = component.ComponentId, componentVersion = component.ComponentVersion, instanceId = component.InstanceId, action, data = action == "select" ? JsonSerializer.SerializeToElement(new { customerId = "C101" }) : JsonSerializer.SerializeToElement(new {}) }));
    }
}
