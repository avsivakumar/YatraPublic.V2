using System.Text.Json;
using Yatra.Contracts.Components;
using Yatra.Contracts.Messages;

namespace Yatra.Orchestration.Components;

public static class StatusSummaryFactory
{
    public static ComponentRequestPayload Create() => new()
    {
        ComponentId = "status-summary", ComponentVersion = "1.0", InstanceId = YatraUlid.NewUlid(),
        Title = "Sample status summary", Description = "Review this status and acknowledge it to continue.",
        AllowedActions = ["acknowledge"],
        Props = JsonSerializer.SerializeToElement(new { status = "Ready", summary = "The sample is ready to continue. This status is illustrative and performs no external action." })
    };
}
