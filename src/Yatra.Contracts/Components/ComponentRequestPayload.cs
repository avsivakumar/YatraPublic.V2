using System.Text.Json;

namespace Yatra.Contracts.Components;

public sealed record ComponentRequestPayload
{
    public required string ComponentId { get; init; }
    public required string ComponentVersion { get; init; }
    public required string InstanceId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required JsonElement Props { get; init; }
    public required IReadOnlyList<string> AllowedActions { get; init; }
}
