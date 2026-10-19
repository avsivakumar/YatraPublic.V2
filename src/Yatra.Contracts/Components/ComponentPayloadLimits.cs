using System.Text.Encodings.Web;
using System.Text.Json;

namespace Yatra.Contracts.Components;

public static class ComponentPayloadLimits
{
    private static readonly JsonSerializerOptions CompactJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public const int MaxUtf8Bytes = 65_536;
    public const int MaxDepth = 8;
    public const int MaxNodes = 2_048;
    public const int MaxArrayItems = 100;
    public const int MaxObjectProperties = 100;
    public const int MaxPropertyNameLength = 100;
    public const int MaxStringLength = 4_096;

    public static bool IsWithinLimits(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Undefined) return false;
        var nodes = 0;
        bool Visit(JsonElement value, int depth)
        {
            if (depth > MaxDepth || ++nodes > MaxNodes) return false;
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                    if (property.Name.Length > MaxPropertyNameLength || !names.Add(property.Name) || names.Count > MaxObjectProperties || !Visit(property.Value, depth + 1)) return false;
                return true;
            }
            if (value.ValueKind == JsonValueKind.Array)
                return value.GetArrayLength() <= MaxArrayItems && value.EnumerateArray().All(item => Visit(item, depth + 1));
            return value.ValueKind != JsonValueKind.String || value.GetString()!.Length <= MaxStringLength;
        }
        return Visit(payload, 0) && JsonSerializer.SerializeToUtf8Bytes(payload, CompactJson).Length <= MaxUtf8Bytes;
    }
}
