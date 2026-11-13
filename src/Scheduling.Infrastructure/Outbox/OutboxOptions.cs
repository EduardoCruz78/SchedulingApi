using System.ComponentModel.DataAnnotations;

namespace Scheduling.Infrastructure.Outbox;

/// <summary>Bound from the "Outbox" section. The dispatcher runs only when both RelayUrl and ApiKey are set.</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>WebhookRelay base URL, e.g. https://webhook-relay.onrender.com.</summary>
    public Uri? RelayUrl { get; init; }

    /// <summary>A WebhookRelay "source" API key. Secret: environment variable Outbox__ApiKey.</summary>
    public string? ApiKey { get; init; }

    [Range(1, 3600)]
    public int PollIntervalSeconds { get; init; } = 5;

    /// <summary>Longest wait between polls while the relay keeps failing (exponential backoff).</summary>
    [Range(1, 3600)]
    public int MaxBackoffSeconds { get; init; } = 300;

    [Range(1, 500)]
    public int BatchSize { get; init; } = 20;

    [Range(1, 120)]
    public int RequestTimeoutSeconds { get; init; } = 10;

    public bool Enabled => RelayUrl is not null && !string.IsNullOrWhiteSpace(ApiKey);
}
