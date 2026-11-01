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

## 1. Finish email failure classification

Extend the existing email boundary and delivery handler only where needed:

- Distinguish retryable connection/timeouts from permanent authentication, recipient, or provider rejection failures.
- Reject permanent failures to the appropriate dead-letter route without consuming every delayed retry.
- Preserve the original exception and message/correlation identifiers in the single outcome log.
- Preserve the existing rule that a failure saving the outcome after SMTP acceptance is not recorded as a definite email rejection.
- Keep shutdown cancellation separate from an ordinary send failure.
- Add focused checks for each classification and for failed retry publication.

SMTP acceptance and database persistence are not atomic. A successful submission followed by a lost save can lead to a repeated email. Resolve uncertain outcomes explicitly when a provider reconciliation/idempotency capability is introduced; inbox checks alone cannot guarantee exactly-once email delivery.

## 2. Add unpaid booking cancellation

Add a focused `CancelBooking` slice under `Booking/OperaHouse.Booking/Features/CancelBooking`, using the existing reservation transaction approach:

1. Define the authorized caller and API access contract. Guest cancellation needs ownership or a scoped credential before public deployment.
2. Conditionally change only an eligible pending reservation to cancelled.
3. Release seats only when that state transition wins.
4. Commit the booking state, released seats, and cancellation outbox event together.
5. Make repeated cancellation and cancellation racing expiration safe.
6. Declare the cancellation topology and add its Notification feature before enabling its producer.

The event belongs in Contracts. Preserve the original event message ID across retries and replay. A cancellation notification supplies its message content to the existing processor; it does not duplicate inbox and persistence logic.

Test repeated requests, a competing expiration, a losing transition, and the absence of extra seat release or extra logical events. Paid cancellations and refunds belong to Chapter 9.

## 3. Add controlled DLQ inspection and replay

Use the [messaging runbook](messaging-debugging.md) for read-only inspection. A replay tool remains future work and should:

- Operate on one selected message from a known dead-letter queue.
- Preserve the body, message ID, correlation ID, and original event type.
- Publish to the corresponding business route with publisher confirms and mandatory routing.
- Acknowledge removal from the DLQ only after successful replay publication.
- Make resetting retry metadata a deliberate, visible action.
- Keep credentials out of arguments and logs; avoid bulk replay defaults.

A replay after a successful publish but before DLQ acknowledgment can duplicate delivery. The existing inbox still recognizes completed processing by stable identity.

## 4. Verify schema and transaction compatibility

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

## 5. Verify the running messaging flow

Build and run the local substitute-based checks first:

```sh
dotnet build OperaHouse.sln
dotnet run --project tests/Regression/OperaHouse.RegressionTests
```

Then perform PostgreSQL/RabbitMQ acceptance in a controlled local environment:

1. Start Docker infrastructure and apply both contexts' migrations.
2. Start the API host; it runs HTTP, notification consumption, outbox publication, and expiration.
3. Verify published future performances are browseable and admin authorization is enforced.
4. Reserve seats with a fresh idempotency key and inspect the booking, available seats, and outbox.
5. Allow an unpaid reservation to expire and inspect the winning state transition, seat release, and expiration outbox event.
6. Exercise invalid payloads, completed duplicates, delayed retries, exhausted retries, failed retry publication, and a missing route.
7. Restart the broker/worker during processing and verify that pending/outstanding work remains recoverable.
8. Verify both event-specific routes and their DLQs; retain message identity during any controlled replay.

SMTP tests are deliberately skipped for this architecture refactor. A real confirmation/expiration email to a controlled recipient is a separate future acceptance check, and must be recorded separately from an in-process sender result.

Chapter 4 is complete when unpaid cancellation is implemented and the reservation → expiration/cancellation → seat release → notification flow passes its transaction and messaging acceptance checks.
