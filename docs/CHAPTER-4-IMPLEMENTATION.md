# Chapter 4 — Remaining implementation and verification

The application now uses the [vertical-slice structure](ARCHITECTURE.md). This guide replaces the earlier full-file draft for the layered projects. The current source is the baseline; the old replacement files would reintroduce obsolete interfaces and overwrite reliability fixes.

## What already exists

- [Create booking](../src/Booking/OperaHouse.Booking/Features/CreateBooking/Handler.cs) and [retrieve booking](../src/Booking/OperaHouse.Booking/Features/GetBooking/Handler.cs) handlers.
- [Expiration](../src/Booking/OperaHouse.Booking/Features/ExpireBookings/Handler.cs), with conditional state transition, seat release, and an outbox event in the same transaction.
- Booking-owned [outbox publication](../src/Shared/Persistence/OperaHouse.Persistence/Outbox/OutboxPublisherWorker.cs).
- [Confirmation](../src/Notification/OperaHouse.Notification/Features/SendBookingConfirmation/Handler.cs) and [expiration](../src/Notification/OperaHouse.Notification/Features/SendBookingExpiration/Handler.cs) notification features.
- Shared [notification processing](../src/Notification/OperaHouse.Notification/Processing/NotificationProcessor.cs), with one persistence boundary for inbox and notification outcomes.
- Worker [topology](../src/Notification/OperaHouse.Notification/Messaging/NotificationTopology.cs), [delivery handling](../src/Notification/OperaHouse.Notification/Messaging/NotificationDeliveryHandler.cs), and [session lifetime/recovery](../src/Notification/OperaHouse.Notification/Messaging/Worker.cs).
- Direct rejection of malformed payloads and invalid event fields. Stable IDs are required before inbox processing.
- Confirmed retry publication before ACK, event-specific retry routes, and session restart after callback/channel failure.
- Correct SMTP username/optional authentication, outcome-safe disconnect cleanup, consistent send/save failure handling, and UTC normalization of performance start times.
- The [local regression runner](../tests/README.md), which uses in-process email and RabbitMQ substitutes.

The implemented source does not complete the infrastructure acceptance work below. Payment-driven confirmation remains Chapter 6 work; an enum value is not an implemented purchase workflow.

## Work order and estimates

Work through the five numbered sections in order. These are hands-on implementation estimates, including focused checks; learning the APIs or fixing newly discovered failures can take longer.

| Step | Deliverable | Estimate |
| --- | --- | --- |
| 1 | Permanent email failures go directly to the DLQ; temporary failures retain delayed retry. | 45–90 minutes |
| 2 | Pending unpaid cancellation, atomic seat release, and cancellation notification. | 2–4 hours |
| 3 | A small console tool that inspects or replays one DLQ message. | 1–2 hours |
| 4 | Isolated PostgreSQL integration checks for migrations and reservation transactions. | 2–4 hours |
| 5 | Running-broker recovery checks and a controlled email demonstration. | 1–2 hours |

**Agreed scope:** authentication and authorization are a [Chapter 5 milestone](ROADMAP.md#54-secure-the-first-deployed-api). The six commented admin authorization attributes are not changed in Chapter 4. The regression runner currently stops at the first admin-policy assertion; report that deferral explicitly and keep the assertions. Do not claim the complete runner passes or remove its checks to hide the failure. Keep new reservation/message checks independently runnable so this known failure does not prevent exercising them.

Chapter 4's cancellation API is for local development and testing. Its intended deployed caller is the reservation owner, including a guest with a scoped credential. Enabling deployed access is gated by Chapter 5's ownership/access work; a booking ID is not an access credential.

## 1. Finish email failure classification

**Implemented.** The notification regression checks cover SMTP reply classification, direct dead-lettering for both events, and failure recording. Use the [notification-only test command](../tests/README.md). Live broker and SMTP verification are part of step 5.

Start with [SmtpEmailSender.cs](../src/Notification/OperaHouse.Notification/Email/SmtpEmailSender.cs) and [NotificationDeliveryHandler.cs](../src/Notification/OperaHouse.Notification/Messaging/NotificationDeliveryHandler.cs). Keep `IEmailSender.SendAsync` unchanged.

| File | Implementation |
| --- | --- |
| `Notification/Email/PermanentEmailDeliveryException.cs` (new) | One exception type for a known permanent send rejection, retaining the original exception as its cause. No retry-policy framework or new dependency. |
| `Notification/Email/SmtpEmailSender.cs` | Translate known permanent SMTP authentication/recipient/provider rejections into that exception. Use structured exception/status information, not error-text matching. Temporary SMTP responses, connection failures, and timeouts keep the existing bounded retry path. Unknown failures also retain bounded retry. |
| `Notification/Messaging/NotificationDeliveryHandler.cs` | Catch a permanent delivery exception before the general retry branch; NACK without requeue to the event's DLQ. Retain one outcome log with the original cause and message/correlation IDs. |
| `Notification/Processing/NotificationProcessor.cs` | Preserve the existing failure-state save and shutdown behavior. Change this file only if needed to retain the send cause when saving the failed outcome also fails. Database errors must not become permanent SMTP rejections. |
| `tests/Regression/OperaHouse.RegressionTests/NotificationTests.cs` and `WorkerTests.cs` | Extend the existing sender/channel substitutes; exercise both created and expired routes. |

File paths beginning with `Notification/` above are relative to `src/Notification/OperaHouse.Notification/`.

**Checks:** permanent rejection causes one send attempt, no retry publication, and one NACK to the correct DLQ. Temporary rejection schedules a confirmed retry before ACK. A failed retry publish leaves the original unacknowledged. Shutdown does not record a send failure. An accepted email followed by a database failure remains an uncertain outcome, not a definite send rejection. Retain both causes if sending and recording its failure fail.

**Done when:** those checks pass independently of the deferred authorization assertion, and both event types use the same classification rule. Existing malformed-event handling remains unchanged.

[MailKit exposes SMTP status and error information](https://mimekit.net/docs/html/Properties_T_MailKit_Net_Smtp_SmtpCommandException.htm); classification must distinguish temporary responses from permanent ones rather than treating every recipient or authentication error as permanent.

SMTP acceptance and database persistence are not atomic. A successful submission followed by a lost save can lead to a repeated email. Resolve uncertain outcomes explicitly when a provider reconciliation/idempotency capability is introduced; inbox checks alone cannot guarantee exactly-once email delivery.

## 2. Add unpaid booking cancellation

**Implemented.** All 48 notification checks and six cancellation checks against a disposable PostgreSQL database passed. See the [focused test commands](../tests/README.md). Live RabbitMQ and SMTP verification remains in step 5.

Use `POST /bookings/{id:guid}/cancel` from the local machine in Development. The API returns `200` with the current booking after a successful cancellation or an already-cancelled repeat, `400` for an empty ID, `404` for a missing booking, and `409` for an ineligible state. The expiration worker owns pending reservations at or past their deadline. The booking supplies the seat count and email address.

| Files | Implementation |
| --- | --- |
| `src/Booking/OperaHouse.Booking/Features/CancelBooking/Handler.cs` | Validate the route ID, use `TimeProvider`, and map the repository result to the existing application-result/error types. |
| `src/Booking/OperaHouse.Booking/Persistence/IBookingRepository.cs` and `src/Shared/Persistence/OperaHouse.Persistence/Bookings/BookingRepository.cs` | Use a conditional cancellation update and share the seat release operation with expiration. Return the cancelled booking, a missing result, or an ineligible result. |
| `src/Shared/Contracts/OperaHouse.Contracts/Events/BookingCancelled.cs` (new) | Carry stable message/correlation IDs, booking/performance IDs, email, seat count, and occurrence time, following `BookingExpired`. |
| `src/Notification/OperaHouse.Notification/Features/SendBookingCancellation/Handler.cs` (new), `Processing/BookingEventValidator.cs`, `Messaging/NotificationTopology.cs`, `NotificationDeliveryHandler.cs`, and `Worker.cs` | Add cancellation content, validation, a consumer, and its main/retry/dead-letter routes. Reuse `NotificationProcessor` for inbox/send/outcome persistence. |
| `src/Shared/Messaging/OperaHouse.Messaging/RabbitMqOptions.cs`, `src/Api/OperaHouse.Api/Program.cs`, `Controllers/BookingsController.cs`, and relevant tests/configuration/docs | Add cancellation route settings and registrations, the local action, route/validation checks, topology expectations, and the runbook entry. Keep explicit configuration consistent with option defaults. |

The transaction is small and explicit:

```text
Conditionally update Pending + ExpiresAt > now to Cancelled, setting CancelledAt.
Read the current booking. If no row changed, return repeat/missing/conflict.
If the transition won, release the stored number of seats with a capacity guard.
Insert one BookingCancelled outbox record and commit all three changes together.
```

If seat release or outbox insertion fails, roll back the state change too. Concurrent cancellation requests and expiration must compete through the conditional database update, not a prior in-memory status check. Generate/persist an event only for a winning transition; retries of publication preserve its identity. An already-cancelled request does not create another event.

`Cancelled` and `CancelledAt` already exist on the booking model. Check the EF model against its snapshot before adding any migration; do not add a migration just for the new handler/event. Declare the cancellation topology and add its consumer before enabling its producer.

The event belongs in Contracts. Preserve the original event message ID across retries and replay. A cancellation notification supplies its message content to the existing processor; it does not duplicate inbox and persistence logic.

**Checks:** successful cancellation, repeated/concurrent cancellation, missing/ineligible/due bookings, cancellation competing with expiration, rollback on seat/outbox failure, and one logical event/seat release per winning transition. Test cancellation notification validation, duplicates, permanent/transient failures, and its independent retry route.

**Done when:** cancelling a local pending reservation releases seats once and produces one cancellation event whose notification follows the existing reliable delivery path. Paid cancellations and refunds belong to Chapter 9.

## 3. Add controlled DLQ inspection and replay

Use the [messaging runbook](messaging-debugging.md) for existing inspection. Add one console project at `tools/OperaHouse.MessageReplay`, using the installed RabbitMQ client and existing `RabbitMqOptions`. Keep it out of the public HTTP API.

1. Accept an event alias (`created`, `expired`, or `cancelled`) and an `inspect` or `replay` operation. Resolve the queue and destination route from known configuration. Read credentials from local configuration/environment, not arguments or logs.
2. Fetch one delivery using manual acknowledgment. Inspection displays identity/type/retry metadata and requeues the delivery; it does not log the email body. Inspection can change queue order.
3. For replay, require the expected message ID from inspection. If the next delivery differs, requeue it and stop; do not scan or bulk-drain the queue. Preserve exact body bytes, message/correlation IDs, type, and other relevant properties/headers.
4. Publish persistently to the corresponding business route with confirmation tracking and mandatory routing. Treat an unroutable return, NACK, timeout, or closed channel as failure. ACK the DLQ delivery only after successful publication; on failure leave it recoverable.
5. Preserve retry metadata by default. An explicit `--reset-retry-count` option resets only the application's retry count and reports that choice; preserve failure/death history for diagnosis. Document usage and limits in the runbook.

Do not force raw broker deliveries through `RabbitMqPublisher.PublishJsonAsync`: that method accepts a JSON string and constructs new properties. The tool needs to preserve the original bytes/properties, so use a direct confirmed RabbitMQ channel without refactoring the business publisher.

**Checks:** inspection retains the message, a mismatched expected ID refuses replay, replay preserves identity/body, failed/unroutable publication retains the original, and retry-count reset happens only when explicitly requested. Add a small substitute-based self-check and repeat the publication/ACK checks against a test broker in step 5.

**Done when:** one deliberately selected message can be inspected or replayed without deleting it on publication failure. [RabbitMQ confirmations and returns](https://www.rabbitmq.com/docs/confirms) must both be respected; a publisher confirmation alone does not establish successful routing.

A replay after a successful publish but before DLQ acknowledgment can duplicate delivery. The existing inbox still recognizes completed processing by stable identity.

## 4. Verify schema and transaction compatibility

Add an assertion-based runner at `tests/Integration/OperaHouse.IntegrationTests`, following the existing regression-runner style and using the existing EF/Npgsql/RabbitMQ dependencies. Add it to the solution and document its separate command in `tests/README.md`. Keep the in-process runner usable without infrastructure; no new test framework or container-management library is needed for this chapter.

Point integration checks at dedicated test databases and a test RabbitMQ vhost through environment configuration. Refuse to reset a database/vhost outside the documented test names. Use separate DbContext instances for competing requests and fixed/controlled times rather than timing-dependent sleeps. Do not reset the application's local database or its broker queues.

The project move updates CLR namespaces and EF snapshots together while preserving table mappings and migration IDs. Keep the current migration history and verify both an empty database and an upgrade copy of the prior schema.

```sh
dotnet ef database update --project src/Shared/Persistence/OperaHouse.Persistence --startup-project src/Api/OperaHouse.Api --context BookingDbContext
dotnet ef database update --project src/Notification/OperaHouse.Notification --startup-project src/Api/OperaHouse.Api --context NotificationDbContext
```

Use dedicated local test databases for destructive/reset checks. The architecture move itself is not a reason to create another initial migration or erase existing data.

Add PostgreSQL checks for:

- Concurrent requests for the final seats.
- Repeated and conflicting idempotency keys.
- Creation that rolls back both seat allocation and outbox insertion on failure.
- Repeated expiration and competing expiration workers.
- Cancellation racing expiration once that slice exists.
- One logical event per committed transition, with repeated publication/delivery allowed.

Group the database checks into five independently runnable cases:

| Case | Passing result |
| --- | --- |
| Fresh schema and upgrade | Both contexts migrate on an empty test database; migrating a seeded prior-schema copy retains data and migration history. Existing model/snapshot checks still agree. |
| Final seats | Concurrent requests never allocate more than capacity; failed requests leave no booking/outbox rows. |
| Idempotency | Identical repeated/concurrent keys return the original booking; conflicting details return conflict with no extra seat allocation/event. |
| Transaction rollback | A test-controlled failure during booking/seat/outbox persistence leaves all transaction participants unchanged. Use test interceptors/constraints, not production-only test hooks. |
| Competing terminal transitions | Repeated expiration, competing workers, and cancellation races end in one eligible terminal state, one seat release, and one logical terminal event. |

**Done when:** the PostgreSQL cases pass repeatedly on isolated data. In-memory substitutes or model comparisons do not prove transaction/concurrency behavior.

## 5. Verify the running messaging flow

Build and run the local substitute-based checks first:

```sh
dotnet build OperaHouse.sln
dotnet run --project tests/Regression/OperaHouse.RegressionTests
```

Record the known admin-authorization assertion separately until Chapter 5. Run new feature checks before that assertion or as independently selectable groups; keep unrelated failures visible. The integration runner must exercise reservation/messaging cases without depending on the deferred admin-policy assertion.

Then perform PostgreSQL/RabbitMQ acceptance in a controlled local environment:

1. Start Docker infrastructure and apply both contexts' migrations.
2. Start the API host; it runs HTTP, notification consumption, outbox publication, and expiration.
3. Verify published future performances are browseable and the local performance lifecycle works. Admin authentication/authorization acceptance belongs to Chapter 5.
4. Reserve seats with a fresh idempotency key and inspect the booking, available seats, and outbox.
5. Allow an unpaid reservation to expire and inspect the winning state transition, seat release, and expiration outbox event.
6. Exercise invalid payloads, completed duplicates, delayed retries, exhausted retries, failed retry publication, and a missing route.
7. Restart the broker/worker during processing and verify that pending/outstanding work remains recoverable.
8. Verify both event-specific routes and their DLQs; retain message identity during any controlled replay.

SMTP tests were excluded from the architecture refactor. A real confirmation/expiration/cancellation email to a controlled recipient remains a separate Chapter 4 acceptance check, recorded separately from an in-process sender result.

Extend the integration runner with broker cases for created, expired, and cancelled events: completed duplicates, retry delay/exhaustion, direct permanent-failure DLQ routing, failed retry publication, missing bindings, controlled replay, and recovery after session/broker restarts. Use a fake sender for repeatable failure injection; use SMTP only for the controlled real-email demonstration. In the missing-binding case, prove the outbox remains pending and is published after the route is restored.

Record each case's result with message/booking IDs, the final booking/seat state, outbox/inbox status, and relevant queue counts. Retain one brief verification record in this guide or the test documentation, distinguishing substitutes, PostgreSQL/broker integration, and real SMTP results.

**Done when:** booking → expiration or cancellation → seat release → notification passes against running services, along with the transaction and recovery checks above. The Chapter 5 identity deferral remains visible; it does not become a hidden requirement for completing local Chapter 4 work.
