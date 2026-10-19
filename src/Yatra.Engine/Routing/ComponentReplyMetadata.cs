using System.Text.Json;
using Yatra.AgentIntegration;
using Yatra.Contracts.Components;
using Yatra.Contracts.Messages;

namespace Yatra.Engine.Routing;

internal static class ComponentReplyMetadata
{
    public static void ValidateMessage(YatraMessage message)
    {
        if (message.Type is YatraMessageTypes.ComponentRequest or YatraMessageTypes.ComponentResponse
            && !YatraMessageValidator.Validate(message).IsValid)
            throw new InvalidOperationException("Invalid or excessive component message.");
    }

    public static ComponentRequestPayload? Read(AgentOutput output)
    {
        if (output.Message.Type != YatraMessageTypes.ComponentRequest) return null;
        if (!YatraMessageValidator.Validate(output.Message).IsValid)
            throw new InvalidOperationException("Invalid component request.");
        return output.Message.Payload.Deserialize<ComponentRequestPayload>(YatraJson.SerializerOptions)!;
    }

    public static string Key(ComponentRequestPayload request) => $"{request.ComponentId}/{request.ComponentVersion}";
}
