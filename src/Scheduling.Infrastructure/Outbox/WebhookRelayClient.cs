using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Scheduling.Infrastructure.Outbox;

public enum PublishStatus
{
    /// <summary>202 (new) or 200 (already received: the relay deduplicated it).</summary>
    Published,

    /// <summary>Relay down, timeout, 408, 429 or 5xx: try again later.</summary>
    Transient,

    /// <summary>Any other 4xx: the message itself is wrong; retrying will not help.</summary>
    Rejected,
}

public sealed record PublishResult(PublishStatus Status, string? Error = null);

/// <summary>Publishes one outbox message as <c>POST /events</c> on WebhookRelay.</summary>
public sealed class WebhookRelayClient(HttpClient http, IOptions<OutboxOptions> options)
{
    public async Task<PublishResult> PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        // "https://relay/base" + "events" would drop "base"; a trailing slash keeps the base path.
        var baseUrl = settings.RelayUrl!.AbsoluteUri.EndsWith('/') ? settings.RelayUrl : new Uri(settings.RelayUrl.AbsoluteUri + "/");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUrl, "events"))
        {
            Content = JsonContent.Create(new JsonObject
            {
                // The outbox id is the idempotency key: a re-send after a lost response is answered 200.
                ["id"] = message.Id.ToString(),
                ["type"] = message.Type,
                ["occurredAt"] = message.OccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                ["data"] = JsonNode.Parse(message.Payload),
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new PublishResult(PublishStatus.Published);
            }

            var error = $"HTTP {(int)response.StatusCode}";
            var transient = (int)response.StatusCode >= 500
                || response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;
            return new PublishResult(transient ? PublishStatus.Transient : PublishStatus.Rejected, error);
        }
        catch (HttpRequestException ex)
        {
            return new PublishResult(PublishStatus.Transient, ex.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PublishResult(PublishStatus.Transient, "Timed out");
        }
    }
}
