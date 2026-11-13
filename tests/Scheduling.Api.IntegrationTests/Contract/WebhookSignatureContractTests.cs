using System.Security.Cryptography;
using System.Text;

namespace Scheduling.Api.IntegrationTests.Contract;

/// <summary>
/// How a .NET service verifies a webhook from WebhookRelay (Standard Webhooks scheme). This is the
/// snippet published in WebhookRelay's docs/webhook-signature.md, kept here so it is compiled and tested.
/// </summary>
public static class WebhookSignature
{
    public static bool Verify(
        string secret,
        string webhookId,
        string webhookTimestamp,
        string webhookSignature,
        string body,
        DateTimeOffset now,
        TimeSpan? tolerance = null)
    {
        // 1. Reject stale or future timestamps: a captured request cannot be replayed later.
        if (!long.TryParse(webhookTimestamp, out var seconds)
            || (now - DateTimeOffset.FromUnixTimeSeconds(seconds)).Duration() > (tolerance ?? TimeSpan.FromMinutes(5)))
        {
            return false;
        }

        // 2. HMAC-SHA256 over "id.timestamp.body" with the base64-decoded part of "whsec_...".
        var key = Convert.FromBase64String(secret["whsec_".Length..]);
        var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{webhookId}.{webhookTimestamp}.{body}"));

        // 3. The header may carry several signatures (secret rotation): accept if any v1 matches,
        //    comparing in constant time.
        Span<byte> received = stackalloc byte[64];
        foreach (var part in webhookSignature.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!part.StartsWith("v1,", StringComparison.Ordinal))
            {
                continue;
            }

            if (Convert.TryFromBase64String(part[3..], received, out var length)
                && CryptographicOperations.FixedTimeEquals(received[..length], expected))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Contract between WebhookRelay (Node) and its receivers (here, .NET). The vectors below were
/// produced by WebhookRelay's signature code; the Node test suite pins the same values, so a change
/// on either side breaks a build.
/// </summary>
public class WebhookSignatureContractTests
{
    // Produced by WebhookRelay's signatureHeader() for a rotation in progress: new and old secret.
    private const string NewSecret = "whsec_c2NoZWR1bGluZy1hcGktY29udHJhY3QtdGVzdC1rZXkx";
    private const string OldSecret = "whsec_b2xkLXNlY3JldC1zdGlsbC12YWxpZC1mb3ItMjRo";
    private const string Id = "01938a2b-0000-7000-8000-000000000001";
    private const string Timestamp = "1796731200";
    private const string Body = """{"id":"01938a2b-0000-7000-8000-000000000001","type":"appointment.created","source":"scheduling-api","occurredAt":"2026-12-07T12:00:00.000Z","data":{"appointmentId":"a-1","price":50}}""";
    private const string Signature = "v1,DG80EbvLhesh12+FDtQr/fs2+qLduL5gD5+1vh2PjhI= v1,GDTa0Y76ER9+8oh/TGakjzfSMdqyHFkhfNGDGPCR9Fc=";

    private static readonly DateTimeOffset SentAt = DateTimeOffset.FromUnixTimeSeconds(1796731200);

    [Theory]
    [InlineData(NewSecret)]
    [InlineData(OldSecret)]
    public void SignatureFromWebhookRelay_VerifiesWithEitherSecretDuringRotation(string secret)
    {
        WebhookSignature.Verify(secret, Id, Timestamp, Signature, Body, SentAt.AddSeconds(3)).Should().BeTrue();
    }

    [Fact]
    public void StandardWebhooksReferenceVector_Verifies()
    {
        // Published test vector of the Standard Webhooks specification.
        WebhookSignature.Verify(
            "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw",
            "msg_p5jXN8AQM9LWM0D4loKWxJek",
            "1614265330",
            "v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=",
            """{"test": 2432232314}""",
            DateTimeOffset.FromUnixTimeSeconds(1614265330)).Should().BeTrue();
    }

    [Fact]
    public void TamperedBody_IsRejected()
    {
        var tampered = Body.Replace("\"price\":50", "\"price\":5", StringComparison.Ordinal);

        WebhookSignature.Verify(NewSecret, Id, Timestamp, Signature, tampered, SentAt).Should().BeFalse();
    }

    [Fact]
    public void WrongSecret_IsRejected()
    {
        WebhookSignature.Verify("whsec_d3Jvbmctc2VjcmV0LWZvci10aGUtdGVzdA==", Id, Timestamp, Signature, Body, SentAt)
            .Should().BeFalse();
    }

    [Fact]
    public void ReplayAfterFiveMinutes_IsRejected()
    {
        WebhookSignature.Verify(NewSecret, Id, Timestamp, Signature, Body, SentAt.AddMinutes(6)).Should().BeFalse();
    }
}
