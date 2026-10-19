using System.Text.Json;
using Yatra.Contracts.Components;
using Yatra.Contracts.Messages;
namespace Yatra.Orchestration.Components;
public static class CustomerComparisonFactory
{
    public static ComponentRequestPayload Create() => new()
    {
        ComponentId = "customer-comparison", ComponentVersion = "1.0", InstanceId = YatraUlid.NewUlid(),
        Title = "Select the customer to continue", Description = "Review the matching customers and choose one.",
        AllowedActions = ["select", "cancel"],
        Props = JsonSerializer.SerializeToElement(new { customers = new[] {
            new { id = "C101", name = "ABC Industries", status = "Active", location = "Dallas, TX", summary = "Industrial customer with an active account." },
            new { id = "C205", name = "XYZ Systems", status = "Pending", location = "Irving, TX", summary = "Technology customer awaiting account activation." }
        } }, YatraJson.SerializerOptions)
    };
}
