namespace Scheduling.Application.Common;

/// <summary>Page request; out-of-range values are clamped instead of rejected.</summary>
public sealed record PageRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public PageRequest(int? page = null, int? pageSize = null)
    {
        Page = Math.Max(1, page ?? 1);
        PageSize = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
    }

    public int Page { get; }

    public int PageSize { get; }

    public int Skip => (Page - 1) * PageSize;
}

public sealed record PagedList<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public PagedList<TOut> Map<TOut>(Func<T, TOut> map) => new(Items.Select(map).ToList(), Page, PageSize, TotalCount);
}
