using System.Text.Json;
using Yatra.Contracts.Components;
using Yatra.Contracts.Messages;

namespace Yatra.Orchestration.Components;

// Domain registrations belong to the requester, never the engine or timeline.
public static class SampleComponentCatalog
{
    private sealed record Registration(Func<JsonElement, ComponentRequestPayload, bool> Validate, Func<ComponentResponsePayload, string> Outcome);
    private static readonly IReadOnlyDictionary<(string, string), Registration> Entries = new Dictionary<(string, string), Registration>
    {
        [("customer-comparison", "1.0")] = new(CustomerComparisonValidator.ValidateResponse,
            response => response.Action == "cancel" ? "Customer comparison was cancelled."
                : $"Selected customer {response.Data.GetProperty("customerId").GetString()}. Your selection was routed back to the original requester."),
        [("status-summary", "1.0")] = new(StatusSummaryValidator.ValidateResponse, _ => "Status summary was acknowledged.")
    };
    public static bool TryOutcome(JsonElement response, ComponentRequestPayload request, out string text)
    {
        text = string.Empty;
        if (!Entries.TryGetValue((request.ComponentId, request.ComponentVersion), out var registration) || !registration.Validate(response, request)) return false;
        text = registration.Outcome(response.Deserialize<ComponentResponsePayload>(YatraJson.SerializerOptions)!);
        return true;
    }
    public static bool IsAvailable(JsonElement userPayload, ComponentRequestPayload request)
    {
        if (!Entries.ContainsKey((request.ComponentId, request.ComponentVersion))) return false;
        if (!userPayload.TryGetProperty("componentCapabilities", out var supplied))
            // Older clients retain only the original Phase 2/3 interaction contract.
            return request.ComponentId == "customer-comparison" && request.ComponentVersion == "1.0";
        return ComponentCapabilities.TryRead(supplied, out var capabilities) && capabilities.Any(capability =>
            capability.ComponentId == request.ComponentId && capability.ComponentVersion == request.ComponentVersion
            && request.AllowedActions.All(action => capability.SupportedActions.Contains(action)));
    }
}
