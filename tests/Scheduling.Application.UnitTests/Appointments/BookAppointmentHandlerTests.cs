using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Appointments;
using Scheduling.Application.Common;
using Scheduling.Application.UnitTests.Fakes;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Professionals;
using Scheduling.Domain.Services;
using Scheduling.Tests.Common;

namespace Scheduling.Application.UnitTests.Appointments;

public class BookAppointmentHandlerTests
{
    /// <summary>Tuesday 10:00 in São Paulo. "Now" is Monday 09:00 local.</summary>
    private static readonly DateTimeOffset TuesdayTen = TestTime.At(2026, 12, 8, 13);

    private readonly InMemoryProfessionals _professionals = new();
    private readonly InMemoryServices _services = new();
    private readonly InMemoryAppointments _appointments = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Service _service;
    private readonly Professional _professional;
    private readonly BookAppointmentHandler _handler;

    public BookAppointmentHandlerTests()
    {
        _service = new ServiceBuilder().WithDuration(30).WithPrice(50m).Build();
        _professional = new ProfessionalBuilder().Performing(_service).WorkingWeekdays().Build();
        _services.Add(_service);
        _professionals.Add(_professional);

        var context = new AppointmentContext(
            _appointments,
            _unitOfWork,
            Options.Create(new BusinessOptions()),
            Options.Create(new SchedulingOptions()),
            new FakeTimeProvider(TestTime.Now));

        _handler = new BookAppointmentHandler(new BookAppointmentValidator(), _professionals, _services, context);
    }

    private Task<Domain.Common.Result<AppointmentDto>> Book(DateTimeOffset startsAt, Guid? professionalId = null, Guid? serviceId = null, Guid? customerId = null) =>
        _handler.HandleAsync(
            customerId ?? _customerId,
            new BookAppointmentCommand(professionalId ?? _professional.Id, serviceId ?? _service.Id, startsAt),
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task ValidSlot_IsSavedOnce()
    {
        var result = await Book(TuesdayTen);

        result.IsSuccess.Should().BeTrue();
        _appointments.Items.Should().ContainSingle();
        _unitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task EmptyIds_AreValidationErrorsPerField()
    {
        var result = await Book(TuesdayTen, Guid.Empty, Guid.Empty);

        result.Error!.Fields.Should().ContainKeys("professionalId", "serviceId");
        _unitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task UnknownProfessional_IsNotFound()
    {
        (await Book(TuesdayTen, professionalId: Guid.NewGuid())).Error.Should().Be(ProfessionalErrors.NotFound);
    }

    [Fact]
    public async Task UnknownService_IsNotFound()
    {
        (await Book(TuesdayTen, serviceId: Guid.NewGuid())).Error.Should().Be(ServiceErrors.NotFound);
    }

    // R7
    [Fact]
    public async Task InactiveProfessional_IsRejected()
    {
        _professional.Deactivate();

        (await Book(TuesdayTen)).Error.Should().Be(ProfessionalErrors.Inactive);
    }

    [Fact]
    public async Task InactiveService_IsRejected()
    {
        _service.Deactivate();

        (await Book(TuesdayTen)).Error.Should().Be(ServiceErrors.Inactive);
    }

    [Fact]
    public async Task ServiceNotPerformedByProfessional_IsRejected()
    {
        var other = new ServiceBuilder().WithName("Coloring").Build();
        _services.Add(other);

        (await Book(TuesdayTen, serviceId: other.Id)).Error.Should().Be(ProfessionalErrors.DoesNotPerformService);
    }

    // R5 comes from the domain
    [Fact]
    public async Task TooSoon_IsRejectedByTheDomain()
    {
        (await Book(TestTime.Now.AddMinutes(30))).Error!.Code.Should().Be("Appointment.TooSoon");
    }

    // R1
    [Fact]
    public async Task OutsideWorkingHours_IsRejected()
    {
        (await Book(TestTime.At(2026, 12, 8, 21))).Error.Should().Be(AppointmentErrors.OutsideWorkingHours); // 18:00 local
    }

    // R4
    [Fact]
    public async Task DuringTimeOff_IsRejected()
    {
        _professional.AddTimeOff(TuesdayTen.AddHours(-1), TuesdayTen.AddHours(1), null, TestTime.Now);

        (await Book(TuesdayTen)).Error.Should().Be(AppointmentErrors.ProfessionalOnTimeOff);
    }

    // R2
    [Fact]
    public async Task OverlapWithProfessionalsAppointment_IsSlotTaken()
    {
        _appointments.Add(new AppointmentBuilder().WithProfessional(_professional.Id).WithService(_service).StartingAt(TuesdayTen.AddMinutes(-15)).Build());

        (await Book(TuesdayTen)).Error.Should().Be(AppointmentErrors.SlotTaken);
    }

    [Fact]
    public async Task CancelledAppointmentInTheSlot_DoesNotBlock()
    {
        _appointments.Add(new AppointmentBuilder().WithProfessional(_professional.Id).WithService(_service).StartingAt(TuesdayTen).Cancelled().Build());

        (await Book(TuesdayTen)).IsSuccess.Should().BeTrue();
    }

    // R3
    [Fact]
    public async Task CustomerAlreadyBusy_IsCustomerOverlap()
    {
        _appointments.Add(new AppointmentBuilder().ForCustomer(_customerId).WithService(_service).StartingAt(TuesdayTen).Build());

        (await Book(TuesdayTen)).Error.Should().Be(AppointmentErrors.CustomerOverlap);
    }

    // ADR 0003
    [Fact]
    public async Task ConstraintViolationOnSave_IsReportedAsSlotTaken()
    {
        _unitOfWork.FailNextSaveWith = ConflictKind.Overlap;

        (await Book(TuesdayTen)).Error.Should().Be(AppointmentErrors.SlotTaken);
    }

    [Fact]
    public async Task OtherPersistenceConflicts_AreNotSwallowed()
    {
        _unitOfWork.FailNextSaveWith = ConflictKind.Concurrency;

        var book = () => Book(TuesdayTen);

        await book.Should().ThrowAsync<PersistenceConflictException>();
    }
}
