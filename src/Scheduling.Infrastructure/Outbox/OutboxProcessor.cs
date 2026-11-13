using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scheduling.Infrastructure.Persistence;

namespace Scheduling.Infrastructure.Outbox;

public sealed record OutboxBatchResult(int Published, int Rejected, int Failed)
{
    public bool RelayFailing => Failed > 0;
}

/// <summary>
/// Publishes one batch of pending outbox messages. Rows are claimed with
/// <c>FOR UPDATE SKIP LOCKED</c>, so several API instances can run the dispatcher at the same time:
/// each one locks a different set of rows and none waits for another.
/// </summary>
public sealed partial class OutboxProcessor(
    SchedulingDbContext dbContext,
    WebhookRelayClient relay,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor> logger)
{
    public async Task<OutboxBatchResult> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var batch = await dbContext.OutboxMessages
            .FromSql($"""
                SELECT * FROM outbox_messages
                WHERE processed_at IS NULL
                ORDER BY occurred_at, id
                LIMIT {options.Value.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        int published = 0, rejected = 0, failed = 0;
        foreach (var message in batch)
        {
            var result = await relay.PublishAsync(message, cancellationToken);
            var now = timeProvider.GetUtcNow();
            switch (result.Status)
            {
                case PublishStatus.Published:
                    message.MarkPublished(now);
                    published++;
                    break;
                case PublishStatus.Rejected:
                    message.MarkRejected(now, result.Error ?? "Rejected");
                    rejected++;
                    LogRejected(message.Id, message.Type, result.Error);
                    break;
                case PublishStatus.Transient:
                    message.RecordFailure(result.Error ?? "Failed");
                    failed++;
                    break;
            }

            // The relay is probably down: stop here and keep the remaining messages in order.
            if (result.Status == PublishStatus.Transient)
            {
                LogRelayFailing(result.Error);
                break;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new OutboxBatchResult(published, rejected, failed);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "WebhookRelay rejected outbox message {MessageId} ({Type}): {Error}")]
    private partial void LogRejected(Guid messageId, string type, string? error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "WebhookRelay unavailable, will retry: {Error}")]
    private partial void LogRelayFailing(string? error);
}
