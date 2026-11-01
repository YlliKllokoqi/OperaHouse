# Architecture

OperaHouse has three named business projects: `OperaHouse.Booking`, `OperaHouse.Performance`, and `OperaHouse.Notification`. Use cases are grouped into feature folders inside those projects. `OperaHouse.Api` is the single executable: it serves HTTP and starts the background services implemented by the other projects.

## Physical and solution folders

```text
src/
  Api/OperaHouse.Api/
  Booking/OperaHouse.Booking/
  Performance/OperaHouse.Performance/
  Notification/OperaHouse.Notification/
  Shared/
    Persistence/OperaHouse.Persistence/
    Contracts/OperaHouse.Contracts/
    Messaging/OperaHouse.Messaging/
tests/
  Regression/OperaHouse.RegressionTests/
```

These directories exist on disk, and the solution view mirrors them. There is no generic Modules project or separate Notification.Worker executable.

## Responsibilities

| Project | Owns |
| --- | --- |
| `OperaHouse.Api` | The two resource controllers, HTTP request binding and error mapping, authentication/authorization, configuration, and service registration. |
| `OperaHouse.Booking` | Booking creation/retrieval, expiration scheduling and orchestration, models, input validation, booking options, and `IBookingRepository`. |
| `OperaHouse.Performance` | Public browsing and admin list/get/create/update/publish/cancel use cases, lifecycle rules, models, validation, and `IPerformanceRepository`. |
| `OperaHouse.Notification` | Confirmation/expiration email features, inbox processing, notification persistence and migrations, email integration, RabbitMQ topology/delivery/session handling, and dependency health logging. |
| `OperaHouse.Persistence` | Shared reservation database context and migrations, concrete booking/performance repositories, atomic reservation/seat/outbox writes, outbox publisher, and booking database health check. |
| `OperaHouse.Contracts` | `BookingCreated`, `BookingExpired`, and transport-independent application results/errors. |
| `OperaHouse.Messaging` | Publisher, broker options, and broker health check. |

Booking and Performance reference Contracts. Persistence references both business projects and implements their repository contracts. Neither business project references Persistence or the API. Notification references Contracts and Messaging, and has no dependency on Booking, Performance, or their database context. The API composes the implementations.

## Feature layout

A typical business use case is:

```text
Booking/OperaHouse.Booking/Features/CreateBooking/
  Handler.cs
  Request.cs
  Validator.cs
```

The handler validates input and orchestrates the use case. It calls the project's repository contract for database access. Its input and validator live beside it; shared entities and DTOs live under `Models`. There are no handler interfaces or mediator pipeline.

HTTP actions remain together in `Api/OperaHouse.Api/Controllers/BookingsController.cs` and `PerformancesController.cs`. Controllers bind HTTP data, apply authorization, invoke handlers, and map results. The booking HTTP request remains in the API because its idempotency key comes from a header. Routes, response shapes, status codes, and admin policies are preserved.

The expiration feature includes its scheduling worker alongside the handler. The outbox publisher lives with shared persistence. The API starts both and the Notification consumer; only one application needs to run.

## Reservation consistency

Booking owns reservation rules; Performance owns the performance lifecycle. Their data participates in one `BookingDbContext` because reserving seats requires an atomic update across both:

- Creation conditionally decrements available seats, inserts the booking, and inserts a `BookingCreated` outbox message in one transaction.
- Expiration conditionally transitions a pending booking, releases seats, and inserts a `BookingExpired` outbox message in one transaction.
- Unique idempotency keys and database constraints remain authoritative under concurrent requests.

`Shared/Persistence` holds this actual shared storage responsibility. Giving each business project a separate context/database would require an inventory consistency design and is outside this folder/project refactor.

## Notification processing

```text
Notification/OperaHouse.Notification/
  Features/
    SendBookingConfirmation/Handler.cs
    SendBookingExpiration/Handler.cs
  Processing/
  Persistence/
    Migrations/
  Email/
  Messaging/
  HealthChecks/
```

The send features validate events and supply their content to `NotificationProcessor`. It checks the inbox, submits email through `IEmailSender`, and saves inbox/notification outcomes through `INotificationStore`. Both records use one Notification context.

`Messaging/NotificationDeliveryHandler` owns ACK/NACK and retry decisions; `NotificationTopology` owns queue/exchange declarations; `Worker` owns the consumer session and reconnection. Meaningful outcomes are logged once with message/correlation context. Healthy polling is quiet.

## Outbox and inbox

The outbox records pending business events alongside reservation writes. Its publisher uses stable message IDs and publisher confirms, then records completion. A crash after publication but before saving can publish the same event again.

Notification deduplicates completed processing by `(MessageId, Consumer)`. These stored consumer identifiers stay unchanged even though the executable was renamed and consolidated:

- Created: `OperaHouse.Notification.Worker`.
- Expired: `OperaHouse.Notification.BookingExpired`.

Delivery is at least once. SMTP acceptance and saving completion are separate operations; a lost save can cause another email on redelivery. `Sent` means SMTP accepted submission, not confirmed delivery to the recipient.

## Configuration and persistence compatibility

The API owns connection strings, broker settings, booking options, email settings, and launch profiles. Notification is a library with no independent Program or appsettings files. Development startup also loads the existing Notification User Secrets, then API secrets, environment variables, and command-line overrides, preserving prior local settings and override precedence.

The contexts retain their existing table/column mappings and migration IDs. CLR namespaces and migration snapshots move together to match project ownership; this does not add a migration or alter the schema. Commands now specify both the migrations project and the API startup project, plus the context because the host registers two contexts. See the [README](../README.md).

## Verification and future scope

The regression runner uses in-process email and RabbitMQ substitutes. It checks routing/controller contracts, validation, failure/duplicate handling, retry/session behavior, migration discovery, model compatibility, and UTC normalization. It contacts no SMTP server. Applied migrations, PostgreSQL concurrency, and live broker behavior still require infrastructure verification.

Payments, ticketing, invoices, and unpaid booking cancellation remain future work. A separate background-service deployment can be introduced when required; business code already lives outside the API host.
