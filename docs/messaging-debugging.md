# Messaging debugging runbook

Use this guide to inspect the implemented created/expired event flow. The [architecture guide](ARCHITECTURE.md) explains ownership; the [Chapter 4 guide](CHAPTER-4-IMPLEMENTATION.md) records pending cancellation, failure-classification, replay, and infrastructure checks.

## Expected flow

```text
Booking creation / expiration
  → booking state + seat update + outbox committed together
  → OutboxPublisherWorker publishes with stable message ID and confirms
  → RabbitMQ routes to the event's notification queue
  → Notification Worker dispatches to its send feature
  → shared processor checks inbox, submits email, saves the outcome
  → worker ACKs the delivery
```

The reservation outbox and publisher live under `Shared/Persistence/OperaHouse.Persistence/Outbox`. Notification's inbox and email records live in `Notification/OperaHouse.Notification/Persistence`. Repeated publication/delivery is expected; completed inbox records prevent repeating completed processing.

## Start and inspect infrastructure

Run from the repository root:

```sh
docker compose up -d
docker compose ps
docker logs operahouse-rabbitmq
docker logs operahouse-postgres
```

PostgreSQL's host port is `54400`; RabbitMQ uses `5672`. The Management UI is [http://localhost:15672](http://localhost:15672). Local broker credentials are `operahouse` / `operahouse`, as defined in `docker-compose.yml`.

To stop the local containers while retaining their named volumes:

```sh
docker compose down
```

## Apply migrations and start the application

```sh
dotnet ef database update --project src/Shared/Persistence/OperaHouse.Persistence --startup-project src/Api/OperaHouse.Api --context BookingDbContext
dotnet ef database update --project src/Notification/OperaHouse.Notification --startup-project src/Api/OperaHouse.Api --context NotificationDbContext
```

Start the single application host:

```sh
dotnet run --project src/Api/OperaHouse.Api
```

The API host runs the Notification consumer, outbox publisher, reservation expiration, and health logging. Their implementations live in the business/shared projects. Both contexts use the same local PostgreSQL database with separate models; migration identities and table mappings are preserved.

## Exchanges and routes

| Purpose | Name |
| --- | --- |
| Events exchange | `operahouse.events` |
| Retry exchange | `operahouse.retry` |
| Dead-letter exchange | `operahouse.dead-letter` |
| Created route | `booking.created` |
| Created retry route | `booking.created.retry` |
| Created dead-letter route | `booking.created.dead` |
| Expired route | `booking.expired` |
| Expired retry route | `booking.expired.retry` |
| Expired dead-letter route | `booking.expired.dead` |

| Event | Main queue | Retry queue | Dead-letter queue |
| --- | --- | --- | --- |
| Created | `notification-booking-created.queue` | `notification-booking-created.retry.queue` | `notification-booking-created.dead-letter.queue` |
| Expired | `notification-booking-expired.queue` | `notification-booking-expired.retry.queue` | `notification-booking-expired.dead-letter.queue` |

Inspect queue counts:

```sh
docker exec operahouse-rabbitmq rabbitmqctl list_queues name messages_ready messages_unacknowledged messages arguments
```

- `messages_ready`: queued and waiting for delivery.
- `messages_unacknowledged`: delivered but not yet ACKed/NACKed.
- `messages`: total queued plus unacknowledged messages.

In the Management UI, open **Queues and Streams**, select a queue, and use **Get messages** with **Nack message requeue true** for inspection that requeues the inspected delivery. Avoid destructive acknowledgment modes when investigating.

## Message identity and retry metadata

Published business messages include `message_id`, `correlation_id`, `type`, `timestamp`, `content_type = application/json`, and persistent delivery mode. The body carries stable message, correlation, booking, and performance IDs, customer email, seats, and occurrence time; created events also carry expiration time.

The publisher also adds `message-id`, `correlation-id`, and `message-type` headers. Retry publication preserves identity and adds:

```text
x-retry-count
x-original-exchange
x-original-routing-key
x-first-failed-at
x-last-failed-at
x-last-error
```

Invalid JSON, invalid required event fields, and permanent SMTP rejections go directly to the event's DLQ. Temporary SMTP failures and other retryable processing failures use delayed retry up to the configured maximum.

## Booking outbox

Inspect pending/publication outcomes using the actual outbox column names:

```sh
docker exec operahouse-postgres psql -U operahouse -d operahouse -c 'select "MessageId", "CorrelationId", "Type", "RoutingKey", "OccurredAt", "ProcessedAt", "PublishAttempts", "LastError" from "OutboxMessages" order by "OccurredAt" desc limit 10;'
```

`ProcessedAt` records successful publication, not consumer completion. A null value means publication is still pending. `LastError` and `PublishAttempts` describe publication failures. A crash after broker acceptance but before saving completion can publish the same message again.

## Notification inbox and outcomes

```sh
docker exec operahouse-postgres psql -U operahouse -d operahouse -c 'select "MessageId", "Consumer", "Status", "ReceivedAt", "ProcessedAt", "LastError" from notification_inbox_messages order by "ReceivedAt" desc limit 10;'
docker exec operahouse-postgres psql -U operahouse -d operahouse -c 'select "Recipient", "Subject", "Status", "CreatedAt", "SentAt", "FailedAt", "FailureReason" from notification_messages order by "CreatedAt" desc limit 10;'
```

Inbox `Processed` means the notification processor recorded completion. `Failed` records a processing failure; `Processing` is the unfinished state. The unique key is `(MessageId, Consumer)`. Keep these persisted consumer identifiers stable:

- Created: `OperaHouse.Notification.Worker`.
- Expired: `OperaHouse.Notification.BookingExpired`.

Notification `Sent` means SMTP accepted submission. It does not confirm delivery to the recipient's inbox. A successful submission followed by a lost database save may lead to another email on redelivery. Sending and persistence are separate operations.

## Logs and health

The worker logs one readiness summary and one processing outcome per delivery. Retry warnings and exhausted/transport errors retain the original exception and message/correlation context. Queue-declaration, receive, and ACK narration is omitted. Healthy polling is quiet; `HealthLoggingWorker` logs unhealthy dependency entries.

The API exposes `GET /health`. A healthy dependency check means the dependency is reachable; it does not prove consumer progress, empty outboxes, or successful email delivery. Inspect records and queue counts as well.

## Local regression checks

```sh
dotnet build OperaHouse.sln
dotnet run --project tests/Regression/OperaHouse.RegressionTests
```

These checks exercise processing, duplicate, malformed-message, retry/session, and UTC cases with in-process substitutes. They contact no SMTP server and require no running PostgreSQL or broker. SMTP testing is deliberately skipped for the architecture refactor.

Broker/transaction acceptance remains separate. Use dedicated test data and a controlled environment for restart, missing-route, retry, and recovery checks; follow the [Chapter 4 verification steps](CHAPTER-4-IMPLEMENTATION.md). A controlled DLQ replay tool is still pending.

## Common issues

### A queue is empty after creating a booking

This is normal when the consumer has completed and ACKed it. Inspect the outbox, inbox, and notification tables. Confirm the relevant outbox event existed before investigating routing.

### A delivery remains unacknowledged

A handler may be running, paused in a debugger, or waiting on a dependency. The worker restarts a failed session after callback/channel failure; closing the session makes outstanding deliveries recoverable. Check the preceding outcome/session error before restarting anything.

### The retry queue looks empty

The default delay is 10,000 ms, so messages may have already returned to their main queue. Inspect retry headers and the event's DLQ after the configured retry maximum.

### Swagger reports “Failed to fetch”

In Development, use [HTTP Swagger](http://localhost:5286/swagger). HTTP requests stay on the same origin instead of redirecting endpoint calls to HTTPS. HTTPS redirection remains enabled outside Development.

If using the HTTPS launch URL, trust the local development certificate first:

```sh
dotnet dev-certs https --trust
```

### RabbitMQ login fails

Check configured credentials and Docker port bindings. A second local RabbitMQ process can conflict with port `5672`. Use the credentials/environment expected by the running container rather than assuming a different broker instance has the same configuration.
