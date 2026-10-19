using System.Text.Json;
using Yatra.Contracts.Routing;

namespace Yatra.AgentIntegration;

public interface IComponentResponseValidator
{
    string ValidatorKey { get; }
    ValueTask<bool> IsValidAsync(JsonElement response, PendingReply pendingReply, CancellationToken cancellationToken);
}
