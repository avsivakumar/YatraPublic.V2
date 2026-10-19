using System.Text.Json;
using Yatra.AgentIntegration;
using Yatra.Contracts.Components;
using Yatra.Contracts.Messages;
using Yatra.Contracts.Routing;
namespace Yatra.Orchestration.Components;
public sealed class CustomerComparisonValidator : IComponentResponseValidator
{
    public string ValidatorKey => "customer-comparison/1.0";
    public ValueTask<bool> IsValidAsync(JsonElement response, PendingReply pendingReply, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(pendingReply.ReturnPath.QualifiedPath == "orchestrator/sample"
            && pendingReply.ComponentRequest is { } request && ValidateResponse(response, request));
    }
    public static bool ValidateProps(JsonElement props)
    {
        if (!ExactFields(props, ["customers"]) || !props.TryGetProperty("customers", out var customers)
            || customers.ValueKind != JsonValueKind.Array || customers.GetArrayLength() is < 2 or > 10) return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var customer in customers.EnumerateArray())
        {
            if (!ExactFields(customer, ["id", "name", "status", "location", "summary"])
                || !RequiredText(customer, "id", 100) || !RequiredText(customer, "name", 200)
                || !RequiredText(customer, "status", 100) || !OptionalText(customer, "location", 200)
                || !OptionalText(customer, "summary", 1000) || !ids.Add(customer.GetProperty("id").GetString()!)) return false;
        }
        return true;
    }
    public static bool ValidateResponse(JsonElement response, ComponentRequestPayload request)
    {
        if (request.ComponentId != "customer-comparison" || request.ComponentVersion != "1.0"
            || !ValidateProps(request.Props) || !ComponentPayloadValidator.Validate(response, true).IsValid
            || !ExactFields(response, ["componentId", "componentVersion", "instanceId", "action", "data"])) return false;
        var payload = response.Deserialize<ComponentResponsePayload>(YatraJson.SerializerOptions)!;
        if (payload.ComponentId != request.ComponentId || payload.ComponentVersion != request.ComponentVersion
            || payload.InstanceId != request.InstanceId || !request.AllowedActions.Contains(payload.Action)) return false;
        if (payload.Action == "cancel") return ExactFields(payload.Data, []) && !payload.Data.EnumerateObject().Any();
        return payload.Action == "select" && ExactFields(payload.Data, ["customerId"])
            && RequiredText(payload.Data, "customerId", 100)
            && request.Props.GetProperty("customers").EnumerateArray().Any(customer =>
                customer.GetProperty("id").GetString() == payload.Data.GetProperty("customerId").GetString());
    }
    private static bool ExactFields(JsonElement value, string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return value.EnumerateObject().All(property => allowed.Contains(property.Name) && seen.Add(property.Name));
    }
    private static bool RequiredText(JsonElement value, string key, int max) => value.TryGetProperty(key, out var text)
        && text.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(text.GetString()) && text.GetString()!.Length <= max;
    private static bool OptionalText(JsonElement value, string key, int max) => !value.TryGetProperty(key, out var text)
        || (text.ValueKind == JsonValueKind.String && text.GetString()!.Length <= max);
}
