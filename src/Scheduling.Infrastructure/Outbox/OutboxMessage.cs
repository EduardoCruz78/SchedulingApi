namespace Scheduling.Infrastructure.Outbox;

/// <summary>
/// An event waiting to be published (Transactional Outbox, ADR 0005). Written in the same
/// transaction as the appointment change that produced it, read later by <see cref="OutboxProcessor"/>.
/// </summary>
public sealed class OutboxMessage
{
    public OutboxMessage(Guid id, string type, string payload, DateTimeOffset occurredAt)
    {
        Id = id;
        Type = type;
        Payload = payload;
        OccurredAt = occurredAt;
    }

    /// <summary>Also the idempotency key WebhookRelay deduplicates on, so a re-send is harmless.</summary>
    public Guid Id { get; private set; }

    /// <summary>Event type as WebhookRelay knows it, e.g. <c>appointment.created</c>.</summary>
    public string Type { get; private set; }

    /// <summary>JSON object stored as jsonb.</summary>
    public string Payload { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Set once WebhookRelay accepted the message (or refused it for good).</summary>
    public DateTimeOffset? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public bool IsPending => ProcessedAt is null;

    public void MarkPublished(DateTimeOffset now)
    {
        Attempts++;
        ProcessedAt = now;
        LastError = null;
    }

    /// <summary>
    /// The relay refused the message permanently (e.g. 400). Retrying would fail forever and block
    /// the messages behind it, so it is closed with the reason kept for investigation.
    /// </summary>
    public void MarkRejected(DateTimeOffset now, string error)
    {
        Attempts++;
        ProcessedAt = now;
        LastError = Truncate(error);
    }

    /// <summary>Temporary failure (relay down, timeout, 5xx): stays pending and is retried.</summary>
    public void RecordFailure(string error)
    {
        Attempts++;
        LastError = Truncate(error);
    }

    private static string Truncate(string error) => error.Length <= 1000 ? error : error[..1000];
}
