using FluentValidation;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Common;
using Scheduling.Domain.Common;
using Scheduling.Domain.Services;

namespace Scheduling.Application.Services;

public sealed record ServiceDto(Guid Id, string Name, string? Description, int DurationMinutes, decimal Price, bool IsActive)
{
    public static ServiceDto From(Service s) => new(s.Id, s.Name, s.Description, s.DurationMinutes, s.Price, s.IsActive);
}

public sealed record SaveServiceCommand(string Name, string? Description, int DurationMinutes, decimal Price, bool IsActive = true);

public sealed class SaveServiceValidator : AbstractValidator<SaveServiceCommand>
{
    public const decimal MaxPrice = 99_999_999.99m;

    public SaveServiceValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(Service.NameMaxLength);
        RuleFor(c => c.Description).MaximumLength(Service.DescriptionMaxLength);
        RuleFor(c => c.DurationMinutes)
            .InclusiveBetween(Service.MinDurationMinutes, Service.MaxDurationMinutes)
            .Must(d => d % Service.DurationStepMinutes == 0)
            .WithMessage($"Duration must be a multiple of {Service.DurationStepMinutes} minutes.");
        RuleFor(c => c.Price).InclusiveBetween(0, MaxPrice).PrecisionScale(10, 2, ignoreTrailingZeros: true);
    }
}

/// <summary>R12: the public catalog lists only active services; admins can include inactive ones.</summary>
public sealed class ListServicesHandler(IServiceRepository services)
{
    public async Task<PagedList<ServiceDto>> HandleAsync(bool includeInactive, PageRequest page, CancellationToken cancellationToken = default) =>
        (await services.ListAsync(includeInactive, page, cancellationToken)).Map(ServiceDto.From);
}

/// <summary>Returns inactive services too: past appointments still need to show what was booked (R12).</summary>
public sealed class GetServiceHandler(IServiceRepository services)
{
    public async Task<Result<ServiceDto>> HandleAsync(Guid id, CancellationToken cancellationToken = default) =>
        await services.GetByIdAsync(id, cancellationToken) is { } service ? ServiceDto.From(service) : ServiceErrors.NotFound;
}

public sealed class CreateServiceHandler(IValidator<SaveServiceCommand> validator, IServiceRepository services, IUnitOfWork unitOfWork)
{
    public async Task<Result<ServiceDto>> HandleAsync(SaveServiceCommand command, CancellationToken cancellationToken = default)
    {
        if (await validator.ValidateToErrorAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var created = Service.Create(command.Name, command.Description, command.DurationMinutes, command.Price);
        if (created.IsFailure)
        {
            return created.Error;
        }

        if (!command.IsActive)
        {
            created.Value.Deactivate();
        }

        services.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ServiceDto.From(created.Value);
    }
}

public sealed class UpdateServiceHandler(IValidator<SaveServiceCommand> validator, IServiceRepository services, IUnitOfWork unitOfWork)
{
    /// <summary>Changing the price or duration does not touch existing appointments: they keep PriceAtBooking and EndsAt (R8).</summary>
    public async Task<Result<ServiceDto>> HandleAsync(Guid id, SaveServiceCommand command, CancellationToken cancellationToken = default)
    {
        if (await validator.ValidateToErrorAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var service = await services.GetByIdAsync(id, cancellationToken);
        if (service is null)
        {
            return ServiceErrors.NotFound;
        }

        var updated = service.Update(command.Name, command.Description, command.DurationMinutes, command.Price);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        if (command.IsActive)
        {
            service.Activate();
        }
        else
        {
            service.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ServiceDto.From(service);
    }
}

/// <summary>R12: "deleting" a service deactivates it, so appointment history keeps a valid reference.</summary>
public sealed class DeactivateServiceHandler(IServiceRepository services, IUnitOfWork unitOfWork)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var service = await services.GetByIdAsync(id, cancellationToken);
        if (service is null)
        {
            return ServiceErrors.NotFound;
        }

        service.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
