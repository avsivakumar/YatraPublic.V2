using System.Text.Json;
using System.Text.RegularExpressions;

namespace Yatra.Contracts.Components;

public sealed record ComponentCapability(string ComponentId, string ComponentVersion, IReadOnlyList<string> SupportedActions);

public static class ComponentCapabilities
{
    public const int MaxComponents = 32;

    public static bool TryRead(JsonElement value, out IReadOnlyList<ComponentCapability> capabilities)
    {
        capabilities = [];
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > MaxComponents || !ComponentPayloadLimits.IsWithinLimits(value)) return false;
        var result = new List<ComponentCapability>();
        var seen = new HashSet<(string, string)>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Count() != 3
                || !item.TryGetProperty("componentId", out var id) || id.ValueKind != JsonValueKind.String
                || !item.TryGetProperty("componentVersion", out var version) || version.ValueKind != JsonValueKind.String
                || !item.TryGetProperty("supportedActions", out var actions) || actions.ValueKind != JsonValueKind.Array
                || actions.GetArrayLength() is < 1 or > 16) return false;
            var actionNames = actions.EnumerateArray().Select(action => action.ValueKind == JsonValueKind.String ? action.GetString() : null).ToArray();
            if (id.GetString()!.Length > 100 || !Regex.IsMatch(id.GetString()!, @"\A[a-z][a-z0-9]*(?:-[a-z0-9]+)*\z")
                || version.GetString()!.Length > 32 || !Regex.IsMatch(version.GetString()!, @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z")
                || actionNames.Any(action => action is null || action.Length > 64 || !Regex.IsMatch(action, @"\A[a-z][a-z0-9]*(?:-[a-z0-9]+)*\z"))
                || actionNames.Distinct(StringComparer.Ordinal).Count() != actionNames.Length || !seen.Add((id.GetString()!, version.GetString()!))) return false;
            result.Add(new(id.GetString()!, version.GetString()!, actionNames.Select(action => action!).ToArray()));
        }
        capabilities = result;
        return true;
    }
}
