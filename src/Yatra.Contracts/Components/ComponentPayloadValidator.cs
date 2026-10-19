using System.Text.Json;
using System.Text.RegularExpressions;
using Yatra.Contracts.Messages;

namespace Yatra.Contracts.Components;

public static class ComponentPayloadValidator
{
    public static YatraMessageValidationResult Validate(JsonElement payload, bool response = false)
    {
        var errors = new List<YatraMessageValidationError>();
        void Invalid(string field) => errors.Add(new(field, "invalid_component_payload", $"Invalid component field: {field}."));
        if (!ComponentPayloadLimits.IsWithinLimits(payload))
        {
            Invalid("payloadLimits");
            return new(errors);
        }
        if (payload.ValueKind != JsonValueKind.Object)
        {
            Invalid("payload");
            return new(errors);
        }
        string? Text(string field) => payload.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        bool Matches(string? value, string pattern, int max) => value is not null && value.Length <= max && Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant);
        if (!Matches(Text("componentId"), @"\A[a-z][a-z0-9]*(?:-[a-z0-9]+)*\z", 100)) Invalid("componentId");
        if (!Matches(Text("componentVersion"), @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z", 32)) Invalid("componentVersion");
        if (!YatraUlid.IsValid(Text("instanceId"))) Invalid("instanceId");
        var objectField = response ? "data" : "props";
        if (!payload.TryGetProperty(objectField, out var obj) || obj.ValueKind != JsonValueKind.Object) Invalid(objectField);
        if (response)
        {
            if (!Matches(Text("action"), @"\A[a-z][a-z0-9]*(?:-[a-z0-9]+)*\z", 64)) Invalid("action");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Text("title")) || Text("title")!.Length > 200) Invalid("title");
            if (payload.TryGetProperty("description", out var description) && description.ValueKind != JsonValueKind.Null &&
                (description.ValueKind != JsonValueKind.String || description.GetString()!.Length > 2000)) Invalid("description");
            if (!payload.TryGetProperty("allowedActions", out var actions) || actions.ValueKind != JsonValueKind.Array || actions.GetArrayLength() is < 1 or > 16)
                Invalid("allowedActions");
            else
            {
                var seen = new HashSet<string>();
                foreach (var action in actions.EnumerateArray())
                    if (action.ValueKind != JsonValueKind.String || !Matches(action.GetString(), @"\A[a-z][a-z0-9]*(?:-[a-z0-9]+)*\z", 64) || !seen.Add(action.GetString()!)) Invalid("allowedActions");
            }
        }
        return new(errors);
    }
}
