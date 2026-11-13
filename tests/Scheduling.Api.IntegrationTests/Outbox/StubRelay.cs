using System.Collections.Concurrent;
using System.Net;

namespace Scheduling.Api.IntegrationTests.Outbox;

/// <summary>Plays WebhookRelay's POST /events: answers with scripted responses and records every call.</summary>
public sealed class StubRelay : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<HttpResponseMessage>> _script = new();

    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    /// <summary>Delay before answering, to let concurrent dispatchers overlap.</summary>
    public TimeSpan Latency { get; set; } = TimeSpan.Zero;

    public void RespondWith(params HttpStatusCode[] statuses)
    {
        foreach (var status in statuses)
        {
            _script.Enqueue(() => new HttpResponseMessage(status));
        }
    }

    public void FailWith(Exception exception) => _script.Enqueue(() => throw exception);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));

        if (Latency > TimeSpan.Zero)
        {
            await Task.Delay(Latency, cancellationToken);
        }

        return _script.TryDequeue(out var next) ? next() : new HttpResponseMessage(HttpStatusCode.Accepted);
    }
}

public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string Body);
