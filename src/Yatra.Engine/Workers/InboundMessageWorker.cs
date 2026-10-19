using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Yatra.AgentIntegration;
using Yatra.Contracts.Errors;
using Yatra.Contracts.Forms;
using Yatra.Contracts.Messages;
using Yatra.Contracts.Routing;
using Yatra.Engine.Queues;
using Yatra.Engine.Routing;
using Yatra.Engine.Persistence;

namespace Yatra.Engine.Workers;

public sealed class InboundMessageWorker : BackgroundService
{
    private readonly IInboundMessageQueue _inboundQueue;
    private readonly IPendingReplyRegistry _pendingReplyRegistry;
    private readonly IOrchestrationAgentAdapter _agentAdapter;
    private readonly IEnumerable<IFormResponseValidator> _formResponseValidators;
    private readonly IEngineAgentOutputSink _outputSink;
    private readonly ILogger<InboundMessageWorker> _logger;
    private readonly FileEngineStateStore? _durableState;
    private readonly IConversationMessageStore? _messageStore;
    private readonly IEnumerable<IComponentResponseValidator> _componentResponseValidators;
    private BufferedOutputSink? _currentOutputs;
    private FormResponseAction? _completionAction;

    public InboundMessageWorker(
        IInboundMessageQueue inboundQueue,
        IPendingReplyRegistry pendingReplyRegistry,
        IOrchestrationAgentAdapter agentAdapter,
        IEnumerable<IFormResponseValidator> formResponseValidators,
        IEngineAgentOutputSink outputSink,
        ILogger<InboundMessageWorker> logger,
        FileEngineStateStore? durableState = null,
        IConversationMessageStore? messageStore = null,
        IEnumerable<IComponentResponseValidator>? componentResponseValidators = null)
    {
        _inboundQueue = inboundQueue;
        _pendingReplyRegistry = pendingReplyRegistry;
        _agentAdapter = agentAdapter;
        _formResponseValidators = formResponseValidators;
        _outputSink = outputSink;
        _logger = logger;
        _durableState = durableState;
        _messageStore = messageStore;
        _componentResponseValidators = componentResponseValidators ?? [];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in _inboundQueue.ReadAllAsync(stoppingToken))
        {
            try
            {
                _currentOutputs = _durableState is null ? null : new BufferedOutputSink();
                _completionAction = null;
                if (_messageStore is not null) await _messageStore.AppendAsync(message, stoppingToken);
                await HandleMessageAsync(message, stoppingToken);
                if (_durableState is not null)
                    await _durableState.CommitInboundAsync(message, _currentOutputs!.Outputs, _completionAction, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                if (_durableState is not null && message.InReplyTo is not null)
                    await _pendingReplyRegistry.ReleaseClaimAsync(message.InReplyTo, CancellationToken.None);
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to process inbound message {MessageId} in conversation {ConversationId}.",
                    message.MessageId,
                    message.ConversationId);
                if (_durableState is not null)
                {
                    if (message.InReplyTo is not null) await _pendingReplyRegistry.ReleaseClaimAsync(message.InReplyTo, stoppingToken);
                    await _durableState.RetryAsync(message.MessageId, inbound: true, stoppingToken);
                }
            }
        }
    }

    private async Task HandleMessageAsync(YatraMessage message, CancellationToken cancellationToken)
    {
        var validation = YatraMessageValidator.Validate(message);
        if (!validation.IsValid)
        {
            await PublishErrorAsync(message, "invalid_message", "The submitted message is invalid.", cancellationToken);
            return;
        }

        PendingReply? claimedPendingReply = null;
        ReturnPath? resolvedReturnPath = null;
        FormResponseAction? formResponseAction = null;
        FormResponsePayload? formResponse = null;
        var isComponentResponse = message.Type == YatraMessageTypes.ComponentResponse;

        if (message.Type == YatraMessageTypes.FormResponse)
        {
            if (!TryReadFormResponsePayload(message, out formResponse))
            {
                await PublishErrorAsync(
                    message,
                    "invalid_form_response",
                    "The submitted form response is invalid.",
                    cancellationToken);
                return;
            }

            formResponseAction = formResponse.Action;
            claimedPendingReply = await ClaimPendingReplyAsync(message, cancellationToken);
            if (claimedPendingReply is null)
            {
                return;
            }

            resolvedReturnPath = claimedPendingReply.ReturnPath;
        }

        if (isComponentResponse)
        {
            claimedPendingReply = await ClaimPendingReplyAsync(message, cancellationToken);
            if (claimedPendingReply is null) return;
            resolvedReturnPath = claimedPendingReply.ReturnPath;
            formResponseAction = message.Payload.GetProperty("action").GetString() == "cancel"
                ? FormResponseAction.Cancel : FormResponseAction.Submit;
        }

        try
        {
            if (isComponentResponse && !await IsValidComponentResponseAsync(message, claimedPendingReply!, cancellationToken))
            {
                await _pendingReplyRegistry.ReleaseClaimAsync(message.InReplyTo!, cancellationToken);
                await PublishErrorAsync(message, "invalid_component_response", "The submitted component response is invalid.", cancellationToken);
                return;
            }
            if (message.Type == YatraMessageTypes.FormResponse)
            {
                if (!await IsValidFormResponseAsync(formResponse!, claimedPendingReply!, cancellationToken))
                {
                    await _pendingReplyRegistry.ReleaseClaimAsync(message.InReplyTo!, cancellationToken);
                    await PublishErrorAsync(
                        message,
                        "invalid_form_response",
                        "The submitted form response is invalid.",
                        cancellationToken,
                        retryable: true);
                    return;
                }
            }

            var componentOutputs = isComponentResponse && _currentOutputs is null ? new BufferedOutputSink() : null;
            var staged = _currentOutputs ?? componentOutputs;
            await _agentAdapter.HandleAsync(
                new AgentInput(message, resolvedReturnPath, claimedPendingReply?.ComponentRequest),
                (IAgentOutputSink?)staged ?? _outputSink,
                cancellationToken);

            if (componentOutputs is not null)
                foreach (var output in componentOutputs.Outputs) await _outputSink.PublishAsync(output, cancellationToken);

            if (_durableState is not null)
            {
                _completionAction = formResponseAction;
            }
            else if (message.Type == YatraMessageTypes.FormResponse || isComponentResponse)
            {
                if (isComponentResponse && componentOutputs!.Outputs.Any(output => output.Message.Type == YatraMessageTypes.Error && output.Message.InReplyTo == message.MessageId))
                    await _pendingReplyRegistry.FailAsync(message.InReplyTo!, cancellationToken);
                else await MarkResponseAcceptedAsync(message.InReplyTo!, formResponseAction!.Value, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (message.Type == YatraMessageTypes.FormResponse || isComponentResponse)
            {
                await _pendingReplyRegistry.ReleaseClaimAsync(message.InReplyTo!, CancellationToken.None);
            }

            throw;
        }
        catch (Exception)
        {
            _currentOutputs?.Outputs.Clear();
            // A durable component response stays in the inbox until an atomic outcome commit.
            // Re-delivery uses the same identity; a transport retry must not create new work.
            if (isComponentResponse && _durableState is not null) throw;
            if (message.Type == YatraMessageTypes.FormResponse || isComponentResponse)
            {
                await _pendingReplyRegistry.ReleaseClaimAsync(message.InReplyTo!, cancellationToken);
                await PublishErrorAsync(
                    message,
                    "requester_unavailable",
                    "The response could not be delivered. Please try again.",
                    cancellationToken,
                    retryable: true);
                if (_durableState is not null) return;
            }

            throw;
        }
    }

    private sealed class BufferedOutputSink : IAgentOutputSink
    {
        public List<AgentOutput> Outputs { get; } = [];
        public ValueTask PublishAsync(AgentOutput output, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Outputs.Add(output);
            return ValueTask.CompletedTask;
        }
    }

    private async Task<PendingReply?> ClaimPendingReplyAsync(
        YatraMessage message,
        CancellationToken cancellationToken)
    {
        var resolution = await _pendingReplyRegistry.ClaimForResponseAsync(
            message.InReplyTo!,
            message.ConversationId,
            cancellationToken);

        if (resolution.Status != PendingReplyResolutionStatus.Resolved || resolution.PendingReply is null)
        {
            var (code, errorMessage) = GetPendingReplyError(resolution.Status);
            await PublishErrorAsync(message, code, errorMessage, cancellationToken);
            return null;
        }

        return resolution.PendingReply;
    }

    private async Task MarkResponseAcceptedAsync(
        string requestMessageId,
        FormResponseAction action,
        CancellationToken cancellationToken)
    {
        var marked = action == FormResponseAction.Cancel
            ? await _pendingReplyRegistry.CancelAsync(requestMessageId, cancellationToken)
            : await _pendingReplyRegistry.CompleteAsync(requestMessageId, cancellationToken);

        if (!marked)
        {
            throw new InvalidOperationException($"Claimed pending reply '{requestMessageId}' could not be finalized.");
        }
    }

    private static (string Code, string Message) GetPendingReplyError(PendingReplyResolutionStatus status) =>
        status switch
        {
            PendingReplyResolutionStatus.ConversationMismatch => (
                "conversation_mismatch",
                "The requested interaction belongs to a different conversation."),
            PendingReplyResolutionStatus.Expired => (
                "pending_reply_expired",
                "The requested interaction has expired."),
            PendingReplyResolutionStatus.AlreadyProcessing or PendingReplyResolutionStatus.AlreadyCompleted or PendingReplyResolutionStatus.Cancelled or PendingReplyResolutionStatus.Failed => (
                "duplicate_response",
                "This interaction has already been completed."),
            _ => (
                "pending_reply_not_found",
                "The requested interaction is no longer available.")
        };

    private async ValueTask<bool> IsValidFormResponseAsync(
        FormResponsePayload response,
        PendingReply pendingReply,
        CancellationToken cancellationToken)
    {
        if (!MatchesExpectedForm(response, pendingReply))
        {
            return false;
        }

        foreach (var validator in _formResponseValidators)
        {
            if (!await validator.IsValidAsync(response, pendingReply, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesExpectedForm(FormResponsePayload response, PendingReply pendingReply)
    {
        if (pendingReply.ComponentRequest is not null) return false;
        if (pendingReply.ExpectedFormId is not null
            && !string.Equals(response.FormId, pendingReply.ExpectedFormId, StringComparison.Ordinal))
        {
            return false;
        }

        if (pendingReply.ExpectedFormVersion is not null
            && !string.Equals(response.FormVersion, pendingReply.ExpectedFormVersion, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private async ValueTask<bool> IsValidComponentResponseAsync(YatraMessage message, PendingReply pending, CancellationToken token)
    {
        var request = pending.ComponentRequest;
        if (request is null || pending.ExpectedFormId is not null) return false;
        var response = message.Payload.Deserialize<Yatra.Contracts.Components.ComponentResponsePayload>(YatraJson.SerializerOptions)!;
        if (response.ComponentId != request.ComponentId || response.ComponentVersion != request.ComponentVersion
            || response.InstanceId != request.InstanceId || !request.AllowedActions.Contains(response.Action)) return false;
        var validators = _componentResponseValidators.Where(validator => validator.ValidatorKey == pending.ValidatorKey).ToArray();
        // No authoritative validator, or ambiguous registration, fails closed.
        return validators.Length == 1 && await validators[0].IsValidAsync(message.Payload, pending, token);
    }

    private static bool TryReadFormResponsePayload(
        YatraMessage message,
        out FormResponsePayload response)
    {
        try
        {
            var payload = message.Payload.Deserialize<FormResponsePayload>(YatraJson.SerializerOptions);
            if (payload is null)
            {
                response = null!;
                return false;
            }

            response = payload;
            return true;
        }
        catch (JsonException)
        {
            response = null!;
            return false;
        }
    }

    private async Task PublishErrorAsync(
        YatraMessage relatedMessage,
        string code,
        string message,
        CancellationToken cancellationToken,
        bool retryable = false)
    {
        var error = new YatraMessage
        {
            MessageId = YatraUlid.NewUlid(),
            ConversationId = relatedMessage.ConversationId,
            Type = YatraMessageTypes.Error,
            Source = "engine:routing",
            Destination = "ui:current-conversation",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            InReplyTo = relatedMessage.MessageId,
            ExpectsReply = false,
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                new YatraErrorPayload
                {
                    Code = code,
                    Message = message,
                    Retryable = retryable,
                    RelatedMessageId = relatedMessage.InReplyTo ?? relatedMessage.MessageId
                },
                YatraJson.SerializerOptions)
        };

        await ((IAgentOutputSink?)_currentOutputs ?? _outputSink).PublishAsync(new AgentOutput(error), cancellationToken);
    }
}
