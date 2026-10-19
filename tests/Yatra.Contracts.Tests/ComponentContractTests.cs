using System.Text.Json;
using System.Text.Json.Nodes;
using Yatra.Contracts.Components;
using Yatra.Contracts.Messages;
namespace Yatra.Contracts.Tests;
public class ComponentContractTests
{
    private static JsonElement Request() => JsonSerializer.SerializeToElement(new ComponentRequestPayload {
        ComponentId = "test-card", ComponentVersion = "1.0", InstanceId = YatraUlid.NewUlid(), Title = "Test", Props = JsonSerializer.SerializeToElement(new {}), AllowedActions = ["select", "cancel"]
    }, YatraJson.SerializerOptions);
    private static JsonElement Response() => JsonSerializer.SerializeToElement(new ComponentResponsePayload {
        ComponentId = "test-card", ComponentVersion = "1.0", InstanceId = YatraUlid.NewUlid(), Action = "select", Data = JsonSerializer.SerializeToElement(new { customerId = "C101" })
    }, YatraJson.SerializerOptions);
    [Fact] public void PayloadsRoundTrip()
    {
        var request = Request(); var response = Response();
        Assert.Equal(request.GetRawText(), JsonSerializer.SerializeToElement(request.Deserialize<ComponentRequestPayload>(YatraJson.SerializerOptions), YatraJson.SerializerOptions).GetRawText());
        Assert.Equal(response.GetRawText(), JsonSerializer.SerializeToElement(response.Deserialize<ComponentResponsePayload>(YatraJson.SerializerOptions), YatraJson.SerializerOptions).GetRawText());
        Assert.True(ComponentPayloadValidator.Validate(request).IsValid);
        Assert.True(ComponentPayloadValidator.Validate(response, true).IsValid);
    }
    [Theory]
    [InlineData("componentId")][InlineData("componentVersion")][InlineData("instanceId")][InlineData("title")][InlineData("props")][InlineData("allowedActions")]
    public void MissingRequestFieldsRejected(string field)
    {
        var node = JsonNode.Parse(Request().GetRawText())!.AsObject(); node.Remove(field);
        Assert.False(ComponentPayloadValidator.Validate(JsonSerializer.SerializeToElement(node)).IsValid);
    }
    [Theory]
    [InlineData("componentId")][InlineData("componentVersion")][InlineData("instanceId")][InlineData("action")][InlineData("data")]
    public void MissingResponseFieldsRejected(string field)
    {
        var node = JsonNode.Parse(Response().GetRawText())!.AsObject(); node.Remove(field);
        Assert.False(ComponentPayloadValidator.Validate(JsonSerializer.SerializeToElement(node), true).IsValid);
    }
    [Theory]
    [InlineData("componentId", "Bad ID")][InlineData("componentVersion", "latest")][InlineData("instanceId", "invalid")][InlineData("title", " ")][InlineData("allowedActions", "select")][InlineData("props", "invalid")]
    public void MalformedRequestFieldsRejected(string field, string value)
    {
        var node = JsonNode.Parse(Request().GetRawText())!; node[field] = value;
        Assert.False(ComponentPayloadValidator.Validate(JsonSerializer.SerializeToElement(node)).IsValid);
    }
    [Theory]
    [InlineData("[]")][InlineData("[\"Select\"]")][InlineData("[\"select\",\"select\"]")][InlineData("[null]")]
    public void InvalidActionsRejected(string json)
    {
        var node = JsonNode.Parse(Request().GetRawText())!; node["allowedActions"] = JsonNode.Parse(json);
        Assert.False(ComponentPayloadValidator.Validate(JsonSerializer.SerializeToElement(node)).IsValid);
    }
    [Fact] public void InvalidResponseActionRejected()
    {
        var node = JsonNode.Parse(Response().GetRawText())!; node["action"] = "Select";
        Assert.False(ComponentPayloadValidator.Validate(JsonSerializer.SerializeToElement(node), true).IsValid);
    }
    [Fact] public void GenericEnvelopeRequiresCorrelationAndReplyExpectation()
    {
        var message = new YatraMessage { MessageId = YatraUlid.NewUlid(), ConversationId = YatraUlid.NewUlid(), Type = YatraMessageTypes.ComponentRequest, Source = "sample", Destination = "ui", CreatedAtUtc = DateTimeOffset.UtcNow, ExpectsReply = true, Payload = Request() };
        Assert.True(YatraMessageValidator.Validate(message).IsValid);
        Assert.False(YatraMessageValidator.Validate(message with { ExpectsReply = false }).IsValid);
        var response = message with { Type = YatraMessageTypes.ComponentResponse, Payload = Response(), ExpectsReply = false };
        Assert.False(YatraMessageValidator.Validate(response).IsValid);
        Assert.True(YatraMessageValidator.Validate(response with { InReplyTo = message.MessageId }).IsValid);
    }
}
