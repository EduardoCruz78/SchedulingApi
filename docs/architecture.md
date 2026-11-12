# Architecture

## Layers

```
┌──────────────────────────────────────────────────────────────┐
│ Scheduling.Api                                               │
│ Controllers · Contracts · ProblemDetails · JWT bearer setup  │
│ Rate limiting · OpenAPI + Scalar · composition root          │
└───────────────┬──────────────────────────────┬───────────────┘
                │                              │
                ▼                              ▼
┌───────────────────────────────┐  ┌───────────────────────────────┐
│ Scheduling.Application        │◄─┤ Scheduling.Infrastructure     │
│ Use-case handlers · validators│  │ EF Core + Npgsql · migrations │
│ AvailabilityCalculator        │  │ Repositories · UnitOfWork     │
│ Abstractions (ports) · options│  │ JWT issuing · password hasher │
└───────────────┬───────────────┘  │ Seeder · health checks        │
                │                  └───────────────────────────────┘
                ▼
┌───────────────────────────────┐
│ Scheduling.Domain             │
│ Aggregates · business rules   │
│ Result / Error · policy       │
└───────────────────────────────┘
```

Arrows point to dependencies. The Domain depends on nothing; Infrastructure implements the interfaces declared by Application ("ports and adapters"). The rule is enforced by architecture tests.

## Aggregates

| Aggregate | Owns | References by id |
|---|---|---|
| `User` | - | - |
| `Service` | - | - |
| `Professional` | working hours, time off, performed service ids | `Service` |
| `Appointment` | status and cancellation data | `User` (customer), `Professional`, `Service` |

`Appointment` is its own aggregate instead of a child of `Professional`: a professional accumulates thousands of appointments, and bookings by different customers should not contend for one large object. Consistency across appointments (no overlaps) is guaranteed by the database ([ADR 0003](adr/0003-overlap-exclusion-constraint.md)).

## Request flow: booking an appointment

```
POST /api/appointments  { professionalId, serviceId, startsAt }
  │
  ├─ JwtBearer          → 401 if no/invalid token
  ├─ Customer policy    → 403 if not a customer
  ├─ AppointmentsController.Book
  │     └─ BookAppointmentHandler
  │          1. validator ............................ 400
  │          2. load professional, service ........... 404
  │          3. active? performs service? (R7) ....... 422
  │          4. Appointment.Book: grid, lead time (R5, R6, R8) ... 422
  │          5. inside working hours (R1) ............ 422
  │          6. not on time off (R4) ................. 422
  │          7. professional free (R2) ............... 409
  │          8. customer free (R3) ................... 409
  │          9. UnitOfWork.SaveChanges
  │               └─ PostgreSQL exclusion constraint (23P01) → 409
  │
  └─ 201 Created + Location + AppointmentDto
```

## Errors

Use cases return `Result<T>`. A failure carries an `Error(Code, Message, Type, Fields?)`; `ApiController.Failure` maps `Type` to a status code and writes RFC 9457 ProblemDetails with the stable `code` and the `traceId`:

```json
{
  "type": "https://tools.ietf.org/html/rfc4918#section-11.2",
  "title": "Business rule violated",
  "status": 422,
  "detail": "The selected time is outside the professional's working hours.",
  "code": "Appointment.OutsideWorkingHours",
  "traceId": "00-6f0c...-01"
}
```

Unexpected exceptions go through `GlobalExceptionHandler` and become a 500 with a trace id and no stack trace.

## Time

- `TimeProvider` is the only source of "now" (`FakeTimeProvider` in tests, including JWT lifetime validation).
- Instants are UTC; working hours are local to `Business:TimeZone` and converted per date ([ADR 0004](adr/0004-store-utc-business-timezone.md)).

## Testing pyramid

| Project | Scope | Dependencies |
|---|---|---|
| `Scheduling.Domain.UnitTests` | Invariants and state transitions | none |
| `Scheduling.Application.UnitTests` | Availability algorithm (incl. DST) and use cases with hand-written fakes | none |
| `Scheduling.Api.IntegrationTests` | Full HTTP pipeline, EF Core mappings, migrations, constraints, concurrency | PostgreSQL 17 via Testcontainers |
