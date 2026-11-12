using Scheduling.Domain.Common;

namespace Scheduling.Domain.Services;

/// <summary>Something the business offers (haircut, beard trim...), with a fixed duration and price.</summary>
public sealed class Service : AggregateRoot
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;
    public const int MinDurationMinutes = 15;
    public const int MaxDurationMinutes = 480;
    public const int DurationStepMinutes = 15;

    private Service(Guid id, string name, string? description, int durationMinutes, decimal price)
        : base(id)
    {
        Name = name;
        Description = description;
        DurationMinutes = durationMinutes;
        Price = price;
        IsActive = true;
    }

    private Service()
    {
    }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public int DurationMinutes { get; private set; }

    public decimal Price { get; private set; }

    /// <summary>R12: inactive services leave the public catalog but past appointments keep pointing to them.</summary>
    public bool IsActive { get; private set; }

    public TimeSpan Duration => TimeSpan.FromMinutes(DurationMinutes);

    public static Result<Service> Create(string name, string? description, int durationMinutes, decimal price)
    {
        var validation = Validate(ref name, ref description, durationMinutes, price);
        return validation.IsFailure
            ? validation.Error
            : new Service(Guid.CreateVersion7(), name, description, durationMinutes, price);
    }

    public Result Update(string name, string? description, int durationMinutes, decimal price)
    {
        var validation = Validate(ref name, ref description, durationMinutes, price);
        if (validation.IsFailure)
        {
            return validation;
        }

        Name = name;
        Description = description;
        DurationMinutes = durationMinutes;
        Price = price;
        return Result.Success();
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    private static Result Validate(ref string name, ref string? description, int durationMinutes, decimal price)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > NameMaxLength)
        {
            return ServiceErrors.InvalidName;
        }

        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (description?.Length > DescriptionMaxLength)
        {
            return ServiceErrors.InvalidDescription;
        }

        if (durationMinutes is < MinDurationMinutes or > MaxDurationMinutes || durationMinutes % DurationStepMinutes != 0)
        {
            return ServiceErrors.InvalidDuration;
        }

        if (price < 0)
        {
            return ServiceErrors.InvalidPrice;
        }

        return Result.Success();
    }
}

public static class ServiceErrors
{
    public static readonly Error InvalidName =
        Error.Validation("Service.InvalidName", $"Name is required and must have at most {Service.NameMaxLength} characters.");

    public static readonly Error InvalidDescription =
        Error.Validation("Service.InvalidDescription", $"Description must have at most {Service.DescriptionMaxLength} characters.");

    public static readonly Error InvalidDuration = Error.Validation(
        "Service.InvalidDuration",
        $"Duration must be between {Service.MinDurationMinutes} and {Service.MaxDurationMinutes} minutes, in steps of {Service.DurationStepMinutes}.");

    public static readonly Error InvalidPrice =
        Error.Validation("Service.InvalidPrice", "Price cannot be negative.");

    public static readonly Error NotFound =
        Error.NotFound("Service.NotFound", "Service not found.");

    public static readonly Error Inactive =
        Error.BusinessRule("Service.Inactive", "This service is no longer offered.");
}
