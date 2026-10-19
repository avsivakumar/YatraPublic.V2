using System.Text.Json;
using System.Text.RegularExpressions;
using Yatra.Contracts.Components;
using Yatra.Contracts.Messages;
namespace Yatra.Contracts.Tests;

public sealed class V2DocumentationContractTests
{
    [Fact]
    public void PublishedComponentExamplesMatchCanonicalContractsAndCorrelation()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory); while (root is not null && !File.Exists(Path.Combine(root.FullName, "Yatra.slnx"))) root = root.Parent;
        var protocol = File.ReadAllText(Path.Combine(root!.FullName, "docs/Message-Protocol.md"));
        var examples = Regex.Matches(protocol, @"```json\s*([\s\S]*?)```").Select(match => JsonDocument.Parse(match.Groups[1].Value).RootElement.Clone()).ToArray();
        var request = Assert.Single(examples, item => item.TryGetProperty("type", out var type) && type.GetString() == YatraMessageTypes.ComponentRequest);
        var message = request.Deserialize<YatraMessage>(YatraJson.SerializerOptions)!; Assert.True(YatraMessageValidator.Validate(message).IsValid);
        var payload = message.Payload.Deserialize<ComponentRequestPayload>(YatraJson.SerializerOptions)!; Assert.NotEqual(message.MessageId, payload.InstanceId);
        var responses = examples.Where(item => item.TryGetProperty("type", out var type) && type.GetString() == YatraMessageTypes.ComponentResponse).ToArray(); Assert.Equal(3, responses.Length);
        foreach (var response in responses) {
            Assert.True(YatraUlid.IsValid(response.GetProperty("messageId").GetString())); Assert.True(YatraUlid.IsValid(response.GetProperty("inReplyTo").GetString()));
            Assert.True(ComponentPayloadValidator.Validate(response.GetProperty("payload"), true).IsValid);
            if (response.GetProperty("payload").GetProperty("componentId").GetString() == payload.ComponentId) {
                Assert.Equal(message.MessageId, response.GetProperty("inReplyTo").GetString()); Assert.Equal(payload.InstanceId, response.GetProperty("payload").GetProperty("instanceId").GetString());
            }
        }
        var capabilityExample = Assert.Single(examples, item => item.TryGetProperty("componentCapabilities", out _));
        Assert.True(ComponentCapabilities.TryRead(capabilityExample.GetProperty("componentCapabilities"), out var capabilities)); Assert.Equal(2, capabilities.Count);
    }
}
