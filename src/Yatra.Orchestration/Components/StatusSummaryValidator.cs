using System.Text.Json;
using Yatra.AgentIntegration;
using Yatra.Contracts.Components;
using Yatra.Contracts.Messages;
using Yatra.Contracts.Routing;

namespace Yatra.Orchestration.Components;

public sealed class StatusSummaryValidator : IComponentResponseValidator
{
    public string ValidatorKey => "status-summary/1.0";
    public ValueTask<bool> IsValidAsync(JsonElement response, PendingReply pendingReply, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(pendingReply.ReturnPath.QualifiedPath == "orchestrator/sample"
            && pendingReply.ComponentRequest is { } request && ValidateResponse(response, request));
    }
    public static bool ValidateProps(JsonElement props) => ExactFields(props, ["status", "summary"])
        && Text(props, "status", 100) && Text(props, "summary", 1000);
    public static bool ValidateResponse(JsonElement response, ComponentRequestPayload request)
    {
        if (request.ComponentId != "status-summary" || request.ComponentVersion != "1.0" || !ValidateProps(request.Props)
            || !ComponentPayloadValidator.Validate(response, true).IsValid || !ExactFields(response, ["componentId", "componentVersion", "instanceId", "action", "data"])) return false;
        var value = response.Deserialize<ComponentResponsePayload>(YatraJson.SerializerOptions)!;
        return value.ComponentId == request.ComponentId && value.ComponentVersion == request.ComponentVersion && value.InstanceId == request.InstanceId
            && value.Action == "acknowledge" && request.AllowedActions.Contains(value.Action) && ExactFields(value.Data, []);
    }
    private static bool Text(JsonElement value, string key, int max) => value.TryGetProperty(key, out var text) && text.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(text.GetString()) && text.GetString()!.Length <= max;
    private static bool ExactFields(JsonElement value, string[] fields) => value.ValueKind == JsonValueKind.Object
        && value.EnumerateObject().Select(property => property.Name).Order().SequenceEqual(fields.Order());
}
