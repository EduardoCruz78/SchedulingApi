namespace Scheduling.Domain.Common;

internal static class Clock
{
    /// <summary>Normalizes any instant to UTC (offset zero), the only representation stored by the domain.</summary>
    public static DateTimeOffset ToUtc(this DateTimeOffset value) => value.ToUniversalTime();
}
