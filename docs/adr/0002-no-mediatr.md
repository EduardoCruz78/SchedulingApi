# ADR 0002: Plain handlers instead of MediatR (and no AutoMapper)

- **Status:** Accepted
- **Date:** 2026-11-12

## Context

MediatR and AutoMapper are common in .NET codebases. Both moved to commercial licenses in 2025. FluentAssertions did the same from v8.

## Decision

- **Use cases are plain classes** (e.g. `BookAppointmentHandler`) registered in DI and injected directly into controllers. Cross-cutting concerns (validation, logging, error mapping) are handled by ASP.NET Core filters, middleware and `Result` mapping.
- **Mapping is manual** (`ToResponse()` methods next to the contracts).
- **Tests use AwesomeAssertions**, the MIT-licensed fork of FluentAssertions with the same API.

## Consequences

- No licensing risk for anyone who forks or reuses the project.
- "Go to definition" on a controller call lands on the handler: no indirection through a mediator.
- Mapping is explicit and checked by the compiler; a renamed property is a build error, not a runtime surprise.
- A pipeline behavior (e.g. a transaction around every command) would need to be written by hand. Not needed at this size.
