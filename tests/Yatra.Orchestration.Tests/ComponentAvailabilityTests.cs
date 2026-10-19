using System.Text.Json;
using Yatra.AgentIntegration;
using Yatra.Contracts.Components;
using Yatra.Contracts.Messages;
using Yatra.Contracts.Routing;
using Yatra.Orchestration.Components;
using Yatra.Orchestration.Llm;
namespace Yatra.Orchestration.Tests;

public sealed class ComponentAvailabilityTests
{
    [Theory]
    [InlineData("[]", false)]
    [InlineData("null", false)]
    [InlineData("[{\"componentId\":\"customer-comparison\",\"componentVersion\":\"1.1\",\"supportedActions\":[\"select\",\"cancel\"]}]", false)]
    [InlineData("[{\"componentId\":\"Customer-comparison\",\"componentVersion\":\"1.0\",\"supportedActions\":[\"select\",\"cancel\"]}]", false)]
    [InlineData("[{\"componentId\":\"customer-comparison\",\"componentVersion\":\"1.0\",\"supportedActions\":[\"select\"]}]", false)]
    [InlineData("[{\"componentId\":\"customer-comparison\",\"componentVersion\":\"1.0\",\"supportedActions\":[\"select\",\"cancel\"]}]", true)]
    public async Task CustomerCommandUsesExactAdvertisedAvailability(string capabilities, bool available)
    {
        var sink = new Sink(); var input = Message("compare customers", capabilities);
        await new SampleOrchestrationAgentAdapter(new FakeLlmClient()).HandleAsync(new AgentInput(input), sink, default);
        var result = Assert.Single(sink.Outputs);
        Assert.Equal(available ? YatraMessageTypes.ComponentRequest : YatraMessageTypes.AgentText, result.Message.Type);
        Assert.Equal(input.MessageId, result.Message.InReplyTo);
        if (!available) { Assert.Null(result.ReturnPath); Assert.False(result.Message.ExpectsReply); Assert.Contains("does not advertise", result.Message.Payload.GetRawText()); }
    }
    [Fact] public async Task LegacyCustomerBehaviorIsPreservedButNewStatusRequiresDiscovery()
    {
        foreach (var command in new[] { "compare customers", "show status summary" }) {
            var sink = new Sink(); await new SampleOrchestrationAgentAdapter(new FakeLlmClient()).HandleAsync(new AgentInput(Message(command)), sink, default);
            Assert.Equal(command == "compare customers" ? YatraMessageTypes.ComponentRequest : YatraMessageTypes.AgentText, Assert.Single(sink.Outputs).Message.Type);
        }
    }
    [Fact] public async Task StatusFactoryAndRegisteredOutcomeUseSamePrivateReplyFlow()
    {
        var capability = "[{\"componentId\":\"status-summary\",\"componentVersion\":\"1.0\",\"supportedActions\":[\"acknowledge\"]}]";
        var adapter = new SampleOrchestrationAgentAdapter(new FakeLlmClient()); var sink = new Sink();
        await adapter.HandleAsync(new AgentInput(Message("  SHOW STATUS SUMMARY  ", capability)), sink, default);
        var output = Assert.Single(sink.Outputs); var request = output.Message.Payload.Deserialize<ComponentRequestPayload>(YatraJson.SerializerOptions)!;
        Assert.Equal("status-summary", request.ComponentId); Assert.Equal("1.0", request.ComponentVersion); Assert.True(StatusSummaryValidator.ValidateProps(request.Props)); Assert.True(output.Message.ExpectsReply);
        Assert.Equal("orchestrator/sample", output.ReturnPath!.QualifiedPath); Assert.True(YatraUlid.IsValid(request.InstanceId)); Assert.NotEqual(request.InstanceId, StatusSummaryFactory.Create().InstanceId);
        var response = output.Message with { MessageId = YatraUlid.NewUlid(), InReplyTo = output.Message.MessageId, Type = YatraMessageTypes.ComponentResponse, ExpectsReply = false, Payload = Response(request) };
        var pending = new PendingReply(output.Message.MessageId, response.ConversationId, output.ReturnPath, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), PendingReplyStatus.Pending, ValidatorKey: "status-summary/1.0", ComponentRequest: request);
        Assert.True(await new StatusSummaryValidator().IsValidAsync(response.Payload, pending, default));
        Assert.False(await new StatusSummaryValidator().IsValidAsync(response.Payload, pending with { ReturnPath = new ReturnPath("wrong/path") }, default));
        sink.Outputs.Clear(); await adapter.HandleAsync(new AgentInput(response, output.ReturnPath, request), sink, default);
        Assert.Equal(response.MessageId, Assert.Single(sink.Outputs).Message.InReplyTo); Assert.Contains("acknowledged", sink.Outputs[0].Message.Payload.GetRawText());
    }
    [Theory]
    [InlineData("{\"status\":\"\",\"summary\":\"ok\"}")][InlineData("{\"status\":\"ok\"}")][InlineData("{\"status\":\"ok\",\"summary\":null}")][InlineData("{\"status\":\"ok\",\"summary\":\"ok\",\"extra\":true}")]
    public void StatusPropertiesRejectMalformedOrUnknownFields(string json) => Assert.False(StatusSummaryValidator.ValidateProps(JsonDocument.Parse(json).RootElement));
    [Theory] [InlineData(100, 1000, true)] [InlineData(101, 1000, false)] [InlineData(100, 1001, false)]
    public void StatusTextBoundaries(int statusLength, int summaryLength, bool valid) => Assert.Equal(valid, StatusSummaryValidator.ValidateProps(JsonSerializer.SerializeToElement(new { status = new string('x', statusLength), summary = new string('x', summaryLength) })));
    [Theory] [InlineData("componentId", "customer-comparison")] [InlineData("componentVersion", "1.1")] [InlineData("instanceId", "01K00000000000000000000000")] [InlineData("action", "cancel")] [InlineData("extra", "x")]
    public void StatusResponseRejectsMismatchesAndExtraProperties(string field, string value)
    {
        var request = StatusSummaryFactory.Create(); var response = System.Text.Json.Nodes.JsonNode.Parse(Response(request).GetRawText())!; response[field] = value;
        Assert.False(StatusSummaryValidator.ValidateResponse(JsonSerializer.SerializeToElement(response), request));
    }
    [Fact] public void StatusAcknowledgeRequiresEmptyDataAndCannotBypassDomainValidationThroughCapabilities()
    {
        var request = StatusSummaryFactory.Create(); var response = Response(request);
        Assert.True(StatusSummaryValidator.ValidateResponse(response, request));
        var changed = System.Text.Json.Nodes.JsonNode.Parse(response.GetRawText())!; changed["data"]!["extra"] = "x";
        Assert.False(StatusSummaryValidator.ValidateResponse(JsonSerializer.SerializeToElement(changed), request));
        Assert.False(SampleComponentCatalog.TryOutcome(response, request with { ComponentVersion = "1.1" }, out _));
        Assert.False(StatusSummaryValidator.ValidateResponse(response, request with { Props = JsonSerializer.SerializeToElement(new { status = "bad" }) }));
    }
    private static JsonElement Response(ComponentRequestPayload request) => JsonSerializer.SerializeToElement(new { componentId = request.ComponentId, componentVersion = request.ComponentVersion, instanceId = request.InstanceId, action = "acknowledge", data = new {} });
    private static YatraMessage Message(string command, string? capabilities = null) => new() { MessageId = YatraUlid.NewUlid(), ConversationId = YatraUlid.NewUlid(), Type = YatraMessageTypes.UserText, Source = "ui", Destination = "engine", CreatedAtUtc = DateTimeOffset.UtcNow, ExpectsReply = false,
        Payload = JsonDocument.Parse(capabilities is null ? JsonSerializer.Serialize(new { text = command }) : $"{{\"text\":{JsonSerializer.Serialize(command)},\"componentCapabilities\":{capabilities}}}").RootElement.Clone() };
    private sealed class Sink : IAgentOutputSink { public List<AgentOutput> Outputs { get; } = []; public ValueTask PublishAsync(AgentOutput output, CancellationToken token) { Outputs.Add(output); return ValueTask.CompletedTask; } }
}
