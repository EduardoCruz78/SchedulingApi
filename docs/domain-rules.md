# Domain rules

Every business rule has an id (R1, R2...). Tests reference these ids in comments so a rule can be traced from this table to the code that enforces it and the tests that prove it.

| # | Rule | Enforced in | Error code | HTTP |
|---|---|---|---|---|
| R1 | The appointment must fit entirely inside one of the professional's working intervals for that weekday (business time zone). | Application (`BookAppointment`) | `Appointment.OutsideWorkingHours` | 422 |
| R2 | No overlap with another non-cancelled appointment of the **same professional**. | Application **and** a PostgreSQL exclusion constraint ([ADR 0003](adr/0003-overlap-exclusion-constraint.md)) | `Appointment.SlotTaken` | 409 |
| R3 | A customer cannot have two overlapping non-cancelled appointments. | Application | `Appointment.CustomerOverlap` | 409 |
| R4 | The appointment cannot fall inside a professional's time off. | Application | `Appointment.ProfessionalOnTimeOff` | 422 |
| R5 | Minimum lead time of 1 hour, maximum advance of 60 days (configurable). | Domain (`Appointment.Book`) | `Appointment.TooSoon` / `Appointment.TooFarAhead` | 422 |
| R6 | Start times follow a 15-minute grid (09:00, 09:15...). | Domain (`SchedulingPolicy.IsOnGrid`) | `Appointment.NotOnGrid` | 422 |
| R7 | The professional must perform the service, and both must be active. | Application | `Professional.DoesNotPerformService`, `Professional.Inactive`, `Service.Inactive` | 422 |
| R8 | `EndsAt = StartsAt + service duration`; the price is copied at booking time. | Domain (`Appointment.Book`) | - | - |
| R9 | A customer can cancel up to 2 hours before the start; an admin can cancel at any time. | Domain (`Appointment.Cancel`) | `Appointment.CancellationWindowClosed` | 422 |
| R10 | `Scheduled → Cancelled / Completed / NoShow`. `Completed` and `NoShow` only after the start. Final states never change. | Domain | `Appointment.InvalidTransition`, `Appointment.NotStartedYet` | 422 |
| R11 | A day's working intervals need `Start < End` and cannot overlap each other. | Domain (`Professional.SetWorkingHours`) | `Professional.InvalidWorkingHoursInterval`, `Professional.OverlappingWorkingHours` | 400 |
| R12 | A deactivated service leaves the public catalog, but past appointments stay valid. | Application | - | - |
| R13 | Time off cannot be added over non-cancelled appointments: they must be cancelled or moved first. | Application (`AddTimeOff`) | `Professional.TimeOffConflictsWithAppointments` | 409 |

## Conventions

- **Intervals are half-open** `[start, end)`. An appointment from 09:00 to 09:30 does not overlap one from 09:30 to 10:00. The same convention is used by the database constraint (`tstzrange(starts_at, ends_at, '[)')`).
- **All instants are stored in UTC.** Working hours are `TimeOnly` values in the business's local time zone and are converted per date, so daylight saving time changes are handled ([ADR 0004](adr/0004-store-utc-business-timezone.md)).
- **"Now" is always a parameter.** The domain never reads the system clock; the Application layer passes `TimeProvider.GetUtcNow()`.
- **Expected failures are values, not exceptions.** Domain methods return `Result`/`Result<T>` with a typed `Error`; the API maps the error type to the HTTP status shown above.

## Appointment lifecycle

```
              ┌──────────── Cancel (customer: ≥ 2h before; admin: any time)
              │
Scheduled ────┼──────────── Complete  (only after StartsAt) ───► Completed
              │
              └──────────── MarkNoShow (only after StartsAt) ──► NoShow

Cancelled, Completed and NoShow are final.
A NoShow still blocks the slot (the professional's time was used); a Cancelled one does not.
```
