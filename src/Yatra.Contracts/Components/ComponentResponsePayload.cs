using System.Text.Json;

namespace Yatra.Contracts.Components;

public sealed record ComponentResponsePayload
{
    public required string ComponentId { get; init; }
    public required string ComponentVersion { get; init; }
    public required string InstanceId { get; init; }
    public required string Action { get; init; }
    public required JsonElement Data { get; init; }
}
