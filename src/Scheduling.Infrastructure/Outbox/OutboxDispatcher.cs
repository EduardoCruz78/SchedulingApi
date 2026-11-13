using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Scheduling.Infrastructure.Outbox;

/// <summary>
/// Polls the outbox and hands pending messages to WebhookRelay. If the relay is down, messages
/// simply wait in the table (that is the point of the outbox) and polling backs off exponentially.
/// </summary>
public sealed partial class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var interval = TimeSpan.FromSeconds(settings.PollIntervalSeconds);
        var maxBackoff = TimeSpan.FromSeconds(settings.MaxBackoffSeconds);
        var delay = interval;
        LogStarted(settings.RelayUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
                var result = await processor.ProcessBatchAsync(stoppingToken);

                if (result.RelayFailing)
                {
                    delay = Min(delay * 2, maxBackoff);
                }
                else
                {
                    delay = interval;
                    // A full batch means more may be waiting: go again without sleeping.
                    if (result.Published + result.Rejected >= settings.BatchSize)
                    {
                        continue;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // A background loop must survive any failure (e.g. database briefly down).
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogBatchFailed(ex);
                delay = Min(delay * 2, maxBackoff);
            }

            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox dispatcher publishing to {RelayUrl}")]
    private partial void LogStarted(Uri? relayUrl);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox batch failed; retrying with backoff")]
    private partial void LogBatchFailed(Exception exception);
}
