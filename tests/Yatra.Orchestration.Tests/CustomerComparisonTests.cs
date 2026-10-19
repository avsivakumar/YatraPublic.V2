using System.Text.Json;
using System.Text.Json.Nodes;
using Yatra.AgentIntegration;
using Yatra.Contracts.Components;
using Yatra.Contracts.Messages;
using Yatra.Contracts.Routing;
using Yatra.Orchestration.Components;
using Yatra.Orchestration.Llm;
namespace Yatra.Orchestration.Tests;
public sealed class CustomerComparisonTests
{
    public static IEnumerable<object[]> PropsCases()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Yatra.slnx"))) root = root.Parent;
        var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(root!.FullName, "tests/fixtures/customer-comparison-props.json")));
        return cases.RootElement.EnumerateArray().Select(test => new object[] { test.GetProperty("name").GetString()!, test.GetProperty("props").Clone(), test.GetProperty("valid").GetBoolean() }).ToArray();
    }
    [Theory, MemberData(nameof(PropsCases))]
    public void PropsMatchSharedBoundaries(string name, JsonElement props, bool valid) => Assert.True(valid == CustomerComparisonValidator.ValidateProps(props), name);
    [Fact] public void FactoryCreatesFreshValidIdentityAndExactContract()
    {
        var request = CustomerComparisonFactory.Create();
        Assert.Equal("customer-comparison", request.ComponentId); Assert.Equal("1.0", request.ComponentVersion);
        Assert.True(YatraUlid.IsValid(request.InstanceId)); Assert.NotEqual(request.InstanceId, CustomerComparisonFactory.Create().InstanceId);
        Assert.Equal(new[] { "select", "cancel" }, request.AllowedActions);
        Assert.True(CustomerComparisonValidator.ValidateProps(request.Props));
    }
    [Theory]
    [InlineData("customerId", "other")][InlineData("componentId", "other")][InlineData("componentVersion", "2.0")][InlineData("instanceId", "01K00000000000000000000000")][InlineData("action", "delete")][InlineData("extra", "x")]
    public void ResponseRejectsForgedFields(string field, string value)
    {
        var request = CustomerComparisonFactory.Create(); var response = JsonNode.Parse(Response(request, "select").GetRawText())!;
        if (field == "customerId") response["data"]![field] = value; else response[field] = value;
        Assert.False(CustomerComparisonValidator.ValidateResponse(JsonSerializer.SerializeToElement(response), request));
    }
    [Theory] [InlineData("select")] [InlineData("cancel")]
    public async Task AdapterConfirmsValidatedActionWithResponseCorrelation(string action)
    {
        var request = CustomerComparisonFactory.Create(); var sink = new Sink();
        var message = Message(YatraMessageTypes.ComponentResponse, Response(request, action)) with { InReplyTo = YatraUlid.NewUlid() };
        await new SampleOrchestrationAgentAdapter(new FakeLlmClient()).HandleAsync(new AgentInput(message, new ReturnPath("orchestrator/sample"), request), sink, default);
        var output = Assert.Single(sink.Outputs); Assert.Equal(message.MessageId, output.Message.InReplyTo);
        Assert.Equal(YatraMessageTypes.AgentText, output.Message.Type);
        Assert.Contains(action == "select" ? "C101" : "cancelled", output.Message.Payload.GetRawText());
    }
    [Theory] [InlineData(null)] [InlineData("wrong/path")]
    public async Task WrongReturnPathFailsClosed(string? path)
    {
        var request = CustomerComparisonFactory.Create(); var sink = new Sink();
        await new SampleOrchestrationAgentAdapter(new FakeLlmClient()).HandleAsync(new AgentInput(Message(YatraMessageTypes.ComponentResponse, Response(request, "select")), path is null ? null : new ReturnPath(path), request), sink, default);
        Assert.Equal(YatraMessageTypes.Error, Assert.Single(sink.Outputs).Message.Type);
        var pending = new PendingReply(YatraUlid.NewUlid(), YatraUlid.NewUlid(), new ReturnPath(path ?? "wrong/path"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), PendingReplyStatus.Pending, ComponentRequest: request);
        Assert.False(await new CustomerComparisonValidator().IsValidAsync(Response(request, "select"), pending, default));
    }
    [Fact] public async Task CompareCommandPublishesPrivateCorrelatedRequest()
    {
        var input = Message(YatraMessageTypes.UserText, JsonSerializer.SerializeToElement(new { text = "  COMPARE CUSTOMERS  " })); var sink = new Sink();
        await new SampleOrchestrationAgentAdapter(new FakeLlmClient()).HandleAsync(new AgentInput(input), sink, default);
        var output = Assert.Single(sink.Outputs); Assert.True(output.Message.ExpectsReply); Assert.Equal(input.MessageId, output.Message.InReplyTo);
        Assert.Equal("orchestrator/sample", output.ReturnPath!.QualifiedPath); Assert.Equal(YatraMessageTypes.ComponentRequest, output.Message.Type);
        Assert.DoesNotContain("returnPath", output.Message.Payload.GetRawText());
        Assert.NotEqual(output.Message.MessageId, output.Message.Payload.GetProperty("instanceId").GetString());
    }
    [Fact] public void RejectsAdditionalDataAndDisallowedActions()
    {
        var request = CustomerComparisonFactory.Create();
        foreach (var action in new[] { "select", "cancel" }) {
            var node = JsonNode.Parse(Response(request, action).GetRawText())!; node["data"]!["extra"] = "x";
            Assert.False(CustomerComparisonValidator.ValidateResponse(JsonSerializer.SerializeToElement(node), request));
        }
        Assert.False(CustomerComparisonValidator.ValidateResponse(Response(request, "select"), request with { AllowedActions = ["cancel"] }));
    }
    private static JsonElement Response(ComponentRequestPayload request, string action) => JsonSerializer.SerializeToElement(new { componentId = request.ComponentId, componentVersion = request.ComponentVersion, instanceId = request.InstanceId, action, data = action == "select" ? JsonSerializer.SerializeToElement(new { customerId = "C101" }) : JsonSerializer.SerializeToElement(new {}) });
    private static YatraMessage Message(string type, JsonElement payload) => new() { MessageId = YatraUlid.NewUlid(), ConversationId = YatraUlid.NewUlid(), Type = type, Source = "test", Destination = "test", CreatedAtUtc = DateTimeOffset.UtcNow, ExpectsReply = false, Payload = payload };
    private sealed class Sink : IAgentOutputSink { public List<AgentOutput> Outputs { get; } = []; public ValueTask PublishAsync(AgentOutput output, CancellationToken token) { Outputs.Add(output); return ValueTask.CompletedTask; } }
}

