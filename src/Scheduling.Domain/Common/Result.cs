using System.Diagnostics.CodeAnalysis;

namespace Scheduling.Domain.Common;

/// <summary>
/// Outcome of an operation that can fail for an expected reason.
/// Exceptions are reserved for bugs and infrastructure failures.
/// </summary>
public class Result
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => !IsSuccess;

    public static Result Success() => new(null);

    public static Result Failure(Error error) => new(error);

    public static Result<T> Success<T>(T value) => new(value, null);

    public static Result<T> Failure<T>(Error error) => new(default, error);

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>Outcome carrying a value on success.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T? value, Error? error) : base(error) => _value = value;

    /// <summary>The value. Throws when read on a failed result, which is always a bug.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({Error.Code}).");

    public static implicit operator Result<T>(T value) => new(value, null);

    public static implicit operator Result<T>(Error error) => new(default, error);
}
