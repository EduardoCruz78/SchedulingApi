# ADR 0003: Prevent double booking with a PostgreSQL exclusion constraint

- **Status:** Accepted
- **Date:** 2026-11-12

## Context

Rule R2: a professional cannot have two non-cancelled appointments that overlap in time.

Checking in code ("is there an overlapping appointment?" then "insert") is a **check-then-act race**. Two requests for the same slot can both pass the check before either inserts, and both succeed.

Alternatives considered:

| Option | Problem |
|---|---|
| Check in code only | Race condition under concurrency. |
| Unique index on `(professional_id, starts_at)` | Only blocks identical start times; 09:00–10:00 and 09:30–10:30 still overlap. |
| `SERIALIZABLE` transactions | Works, but every booking pays the cost and must retry on serialization failures; the guarantee lives in each caller, not in the schema. |
| Row lock on the professional (`SELECT ... FOR UPDATE`) | Works, but serializes all bookings of a professional and is easy to forget in a new code path. |
| **Exclusion constraint** | The database itself guarantees the rule, for every writer, with no extra locking in the application. |

## Decision

```sql
CREATE EXTENSION IF NOT EXISTS btree_gist;

ALTER TABLE appointments
    ADD CONSTRAINT ex_appointments_professional_overlap
    EXCLUDE USING gist (
        professional_id WITH =,
        tstzrange(starts_at, ends_at, '[)') WITH &&
    ) WHERE (status <> 'Cancelled');
```

- `tstzrange(starts_at, ends_at, '[)')` is a half-open range, so back-to-back appointments (09:00–09:30 and 09:30–10:00) do not conflict.
- `&&` means "ranges overlap"; `=` on `professional_id` scopes it per professional. `btree_gist` allows mixing the two in one GiST index.
- The partial `WHERE` frees the slot when an appointment is cancelled.
- EF Core has no API for exclusion constraints, so it is added with `migrationBuilder.Sql(...)` in the initial migration.

The application **still checks** for overlaps before inserting (R2). That gives a friendly error in the common case. The constraint is the guarantee for the rare concurrent case: PostgreSQL raises `23P01 exclusion_violation`, `UnitOfWork` translates it to `PersistenceConflictException(ConflictKind.Overlap)`, and the API returns **409 Conflict**.

## Consequences

- Correct under any concurrency, proven by an integration test that fires two bookings for the same slot in parallel and expects exactly one 201 and one 409.
- Ties the schema to PostgreSQL. Acceptable: the database is a deliberate choice.
- The customer-overlap rule (R3) is only checked in code. Doing the same for customers would be a second constraint on `customer_id`; left out because a customer racing against themselves is not a realistic threat.
