using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Yatra.Contracts.Messages;
using Yatra.Engine.Persistence;
using Yatra.Engine.Queues;
using Yatra.Engine.Streaming;

namespace Yatra.Engine.Api;

public static class YatraEngineEndpointExtensions
{
    private static readonly JsonSerializerOptions SseJsonOptions = new(YatraJson.SerializerOptions)
    {
        WriteIndented = false
    };

    public static IEndpointRouteBuilder MapYatraEngineApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
            "/api/conversations/{conversationId}/messages",
            async Task<IResult> (
                string conversationId,
                BrowserMessageSubmission submission,
                IInboundMessageQueue inboundQueue,
                IConversationMessageStore messageStore,
                BrowserSubmissionGate submissionGate,
                CancellationToken cancellationToken) =>
            {
                var message = BuildTrustedBrowserMessage(conversationId, submission);
                var validation = ValidateBrowserSubmission(conversationId, message);
                if (!validation.IsValid)
                {
                    return Results.BadRequest(validation);
                }

                return await submissionGate.AcceptAsync(message, messageStore, inboundQueue, cancellationToken);
            });

        endpoints.MapGet(
            "/api/conversations/{conversationId}/events",
            async Task<IResult> (
                string conversationId,
                HttpContext httpContext,
                IConversationStreamHub streamHub,
                IConversationMessageStore messageStore,
                IOptions<YatraEngineOptions> options) =>
            {
                if (!YatraUlid.IsValid(conversationId))
                {
                    return Results.BadRequest(new YatraMessageValidationResult(
                    [
                        new YatraMessageValidationError(
                            "conversationId",
                            "invalid_ulid",
                            "Conversation route value must be a valid ULID.")
                    ]));
                }

                await StreamConversationEventsAsync(
                    conversationId,
                    httpContext,
                    streamHub,
                    messageStore,
                    options.Value,
                    httpContext.RequestAborted);

                return Results.Empty;
            });

        return endpoints;
    }

    private static YatraMessage BuildTrustedBrowserMessage(
        string conversationId,
        BrowserMessageSubmission submission)
    {
        return new YatraMessage
        {
            MessageId = submission.MessageId,
            ConversationId = conversationId,
            Type = submission.Type,
            Source = "ui:current-conversation",
            Destination = "engine:inbound",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            InReplyTo = submission.InReplyTo,
            ExpectsReply = false,
            Payload = submission.Payload
        };
    }

    private static YatraMessageValidationResult ValidateBrowserSubmission(
        string conversationId,
        YatraMessage message)
    {
        var errors = YatraMessageValidator.Validate(message).Errors.ToList();

        if (!YatraUlid.IsValid(conversationId))
        {
            errors.Add(new(
                "conversationId",
                "invalid_ulid",
                "Conversation route value must be a valid ULID."));
        }

        if (message.ConversationId != conversationId)
        {
            errors.Add(new(
                nameof(message.ConversationId),
                "conversation_mismatch",
                "Message conversation ID must match the route conversation ID."));
        }

        if (message.Type is not (YatraMessageTypes.UserText or YatraMessageTypes.FormResponse or YatraMessageTypes.ComponentResponse))
        {
            errors.Add(new(
                nameof(message.Type),
                "unsupported_message_type",
                "Only user.text and form.response, or component.response, messages can be submitted by the browser."));
        }

        if (message.Type == YatraMessageTypes.FormResponse && message.Payload.ValueKind == JsonValueKind.Object)
        {
            var payload = message.Payload;
            var valid = payload.TryGetProperty("formId", out var formId) && formId.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(formId.GetString())
                && payload.TryGetProperty("formVersion", out var version) && version.ValueKind == JsonValueKind.String
                && System.Text.RegularExpressions.Regex.IsMatch(version.GetString()!, @"^1(?:\.\d+)*$")
                && payload.TryGetProperty("action", out var action) && action.ValueKind == JsonValueKind.String && action.GetString() is "submit" or "cancel"
                && payload.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Object;
            if (payload.TryGetProperty("replacesResponseId", out var replaces) && replaces.ValueKind != JsonValueKind.Null)
                valid &= replaces.ValueKind == JsonValueKind.String && YatraUlid.IsValid(replaces.GetString());
            if (!valid) errors.Add(new("payload", "invalid_form_response", "The form response contract is invalid or unsupported."));
        }

        return errors.Count == 0
            ? YatraMessageValidationResult.Success
            : new YatraMessageValidationResult(errors);
    }

    private static async Task StreamConversationEventsAsync(
        string conversationId,
        HttpContext httpContext,
        IConversationStreamHub streamHub,
        IConversationMessageStore messageStore,
        YatraEngineOptions options,
        CancellationToken cancellationToken)
    {
        await using var subscription = streamHub.Subscribe(conversationId);
        httpContext.Response.Headers.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";
        httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        await httpContext.Response.StartAsync(cancellationToken);
        await httpContext.Response.WriteAsync(": connected\n\n", cancellationToken);
        await httpContext.Response.Body.FlushAsync(cancellationToken);

        var sentMessageIds = new HashSet<string>(StringComparer.Ordinal);
        var history = await messageStore.ReadConversationAsync(conversationId, cancellationToken);
        // Components stay locked while history is reconstructed; these transport markers
        // are not conversation messages and do not advance the replay cursor.
        var componentReplay = history.Any(message => message.Type == YatraMessageTypes.ComponentRequest);
        if (componentReplay) await WriteReplayMarkerAsync(httpContext.Response, conversationId, true, cancellationToken);
        var cursor = httpContext.Request.Headers["Last-Event-ID"].FirstOrDefault()
            ?? httpContext.Request.Query["lastEventId"].FirstOrDefault();
        var cursorIndex = history.ToList().FindIndex(message => message.MessageId == cursor);
        foreach (var message in history.Take(cursorIndex + 1)) sentMessageIds.Add(message.MessageId);
        foreach (var message in history.Skip(cursorIndex + 1))
        {
            await WriteSseMessageAsync(httpContext.Response, message, cancellationToken);
            sentMessageIds.Add(message.MessageId);
        }
        if (componentReplay) await WriteReplayMarkerAsync(httpContext.Response, conversationId, false, cancellationToken);
        await httpContext.Response.Body.FlushAsync(cancellationToken);

        var heartbeatInterval = TimeSpan.FromSeconds(Math.Max(1, options.SseHeartbeatSeconds));
        using var streamCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var waitForMessage = subscription.Reader.WaitToReadAsync(streamCancellation.Token).AsTask();
        var waitForHeartbeat = Task.Delay(heartbeatInterval, streamCancellation.Token);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var completed = await Task.WhenAny(waitForMessage, waitForHeartbeat);

                if (completed == waitForHeartbeat)
                {
                    await httpContext.Response.WriteAsync($": heartbeat {DateTimeOffset.UtcNow:O}\n\n", cancellationToken);
                    await httpContext.Response.Body.FlushAsync(cancellationToken);
                    waitForHeartbeat = Task.Delay(heartbeatInterval, streamCancellation.Token);
                    continue;
                }

                if (!await waitForMessage)
                {
                    break;
                }

                while (subscription.Reader.TryRead(out var message))
                {
                    if (!sentMessageIds.Add(message.MessageId))
                    {
                        continue;
                    }

                    await WriteSseMessageAsync(httpContext.Response, message, cancellationToken);
                }
                // Keep one pending channel read through idle heartbeats, rather than
                // accumulating an orphaned read on each heartbeat interval.
                waitForMessage = subscription.Reader.WaitToReadAsync(streamCancellation.Token).AsTask();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally { await streamCancellation.CancelAsync(); }
    }

    private static Task WriteReplayMarkerAsync(HttpResponse response, string conversationId, bool active, CancellationToken token)
    {
        var marker = new YatraMessage
        {
            MessageId = YatraUlid.NewUlid(), ConversationId = conversationId, Type = YatraMessageTypes.Status,
            Source = "engine:replay", Destination = "ui:current-conversation", CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpectsReply = false, Payload = JsonSerializer.SerializeToElement(new { active })
        };
        return response.WriteAsync($"event: yatra.replay\ndata: {JsonSerializer.Serialize(marker, SseJsonOptions)}\n\n", token);
    }

    private static async Task WriteSseMessageAsync(
        HttpResponse response,
        YatraMessage message,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, SseJsonOptions);

        await response.WriteAsync($"id: {message.MessageId}\n", cancellationToken);
        await response.WriteAsync($"event: {message.Type}\n", cancellationToken);
        await response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}

