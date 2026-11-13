# ADR 0005: Publish appointment events with a Transactional Outbox

- **Status:** Accepted
- **Date:** 2026-11-12

## Context

Other systems need to know when an appointment is booked or cancelled (notifications through
[WebhookRelay](https://github.com/EduardoCruz78/WebhookRelay), portfolio project 2; the admin dashboard). The obvious
implementation, calling WebhookRelay from the handler right after `SaveChanges`, is a **dual write**:
two systems updated without a shared transaction.

| Order | Failure | Result |
|---|---|---|
| Save, then call the relay | Relay down, timeout, process killed after the commit | Appointment exists, event **lost** |
| Call the relay, then save | Save fails (slot taken, database down) | Event published for an appointment that **does not exist** |

Retrying inside the request does not fix either case: the process can die between the two steps,
and a slow relay would make booking slow.

## Decision

1. `Appointment` raises domain events (`AppointmentBooked`, `AppointmentCancelled`) when, and only
   when, the state change succeeds.
2. `UnitOfWork.SaveChangesAsync` converts the events of tracked aggregates into rows of
   `outbox_messages` **before** calling `SaveChanges`. EF Core writes the appointment and its outbox
   row in **one transaction**: both are committed, or neither is.
3. `OutboxDispatcher` (a `BackgroundService`) polls pending rows and publishes them as
   `POST /events` on WebhookRelay. Rows are claimed with
   `SELECT ... FOR UPDATE SKIP LOCKED`, so several API instances can dispatch concurrently without
   sending a message twice or waiting on each other.
4. On success the row gets `processed_at`. If the relay is down, rows stay pending and polling backs
   off exponentially (5 s up to 5 min). A permanent rejection (4xx other than 408/429) closes the
   row with `last_error`, so one bad message cannot block the queue.
5. The dispatcher is enabled only when `Outbox:RelayUrl` and `Outbox:ApiKey` are configured.
   Messages are always written, so enabling it later publishes the backlog.

## Consequences

- **At-least-once publication.** If the relay accepted a message but `processed_at` could not be
  saved (crash, connection lost), the message is sent again. The outbox id is sent as the event id,
  and WebhookRelay deduplicates on it (its rule D1): the repeat is answered `200` and creates nothing.
- **Order** is kept within one dispatcher batch (oldest first; a transient failure stops the batch),
  but not across instances. Subscribers compare `occurredAt` (WebhookRelay rule D10).
- **Latency** of publication is the poll interval (5 s by default). LISTEN/NOTIFY or a CDC tool
  (Debezium reading the WAL) would lower it; not needed here.
- The table grows: processed rows should be deleted after a retention period (roadmap).
- The dispatcher holds row locks while it calls the relay (at most `BatchSize` requests, each with a
  10 s timeout). The locked rows are outbox rows only, so bookings are never blocked by it.

## Alternatives

| Option | Why not |
|---|---|
| Call WebhookRelay in the handler | The dual write above. |
| Distributed transaction (2PC) | Not supported by HTTP APIs; couples availability of both systems. |
| Change Data Capture (Debezium) | Robust, but needs Kafka Connect or similar infrastructure the free tier cannot host. |
| Message broker (RabbitMQ) instead of an HTTP relay | Same dual write problem between the database and the broker; the outbox would still be needed. |
