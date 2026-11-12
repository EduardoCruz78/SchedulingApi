# ADR 0004: Store instants in UTC, interpret working hours in the business time zone

- **Status:** Accepted
- **Date:** 2026-11-12

## Context

Appointments are instants in time. Working hours ("Mondays 09:00–18:00") are wall-clock times in the establishment's location, and the UTC equivalent of "09:00" changes when daylight saving time starts or ends.

## Decision

- **Instants** (`StartsAt`, `EndsAt`, `CreatedAt`, time off) are `DateTimeOffset` normalized to UTC in the domain and stored as `timestamptz`.
- **Working hours** are `TimeOnly` values meaning local time in the business time zone, configured as an IANA id: `"Business:TimeZone": "America/Sao_Paulo"`.
- Local working hours are converted to UTC **per date** (`WorkingCalendar.ToUtc(date, time, zone)`), so each day uses the offset in force on that day.
- Local times that do not exist (inside a "spring forward" gap) are skipped as slot candidates. Ambiguous local times ("fall back") resolve to standard time.
- The API accepts and returns ISO 8601 timestamps with an offset; availability responses include both the UTC instant and the local time.
- "Now" comes from `TimeProvider`, never from `DateTime.UtcNow`, so tests can freeze and move time.

## Consequences

- Comparisons and overlap checks are simple and correct: everything is UTC.
- Daylight saving transitions are handled; covered by tests in the `America/New_York` zone (Brazil has had no DST since 2019).
- One deployment serves one time zone (single-tenant). Multi-tenant support would move the time zone onto the tenant.
- Shifts crossing midnight (22:00–02:00) are not supported: they must be split into two intervals.
