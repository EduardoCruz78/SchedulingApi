# ADR 0001: Clean Architecture with four projects

- **Status:** Accepted
- **Date:** 2026-11-12

## Context

The API has real business rules (availability, overlaps, cancellation windows, time zones) that must be testable without a database or web server, and that should survive changes in infrastructure (ORM, database, hosting).

## Decision

Split the solution into four projects with a strict dependency rule:

```
Api ──► Application ──► Domain
 │           ▲
 └──► Infrastructure
```

| Project | Contains | May depend on |
|---|---|---|
| `Scheduling.Domain` | Aggregates, value objects, business rules, `Result`/`Error` | nothing (BCL only) |
| `Scheduling.Application` | Use cases, availability calculation, abstractions (repositories, unit of work, hasher, tokens), options | Domain |
| `Scheduling.Infrastructure` | EF Core, PostgreSQL, JWT, password hashing, seeding | Application |
| `Scheduling.Api` | HTTP: controllers, contracts, ProblemDetails, auth setup, composition root | Application, Infrastructure |

The rule is **enforced by tests** (`DomainDependencyTests`, `ApplicationDependencyTests`), so a forbidden reference breaks CI.

## Consequences

- Domain and Application are unit-tested in milliseconds with no I/O.
- Infrastructure can change (e.g. another database) without touching business rules.
- More projects and some mapping code than a single-project API. For an app this size that cost is small, and the separation is the point of the exercise.
- Aggregates reference each other by id only (`Appointment.ProfessionalId`), which keeps each aggregate small and independently loadable.
