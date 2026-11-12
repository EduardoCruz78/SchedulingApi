using FluentValidation;
using Scheduling.Domain.Common;

namespace Scheduling.Application.Common;

public static class ValidationExtensions
{
    /// <summary>Runs the validator and converts failures into a single validation <see cref="Error"/> with per-field messages.</summary>
    public static async Task<Error?> ValidateToErrorAsync<T>(this IValidator<T> validator, T instance, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(instance, cancellationToken);
        if (result.IsValid)
        {
            return null;
        }

        var fields = result.Errors
            .GroupBy(e => ToCamelCase(e.PropertyName), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray(), StringComparer.Ordinal);

        return Error.Validation(fields);
    }

    /// <summary>"WorkingHours[0].Start" becomes "workingHours[0].start", matching the JSON field names.</summary>
    private static string ToCamelCase(string propertyPath) =>
        string.Join('.', propertyPath.Split('.').Select(part => part.Length == 0 ? part : char.ToLowerInvariant(part[0]) + part[1..]));
}

/// <summary>Shared password rule for registration.</summary>
public static class PasswordRules
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MinimumLength(MinLength)
            .MaximumLength(MaxLength)
            .Must(p => p.Any(char.IsLetter) && p.Any(char.IsDigit))
            .WithMessage("Password must contain at least one letter and one digit.");
}
