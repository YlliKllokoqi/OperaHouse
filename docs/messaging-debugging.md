# Messaging Debugging Runbook

This document explains how to inspect and debug the OperaHouse messaging flow.

## Expected message flow

```text
POST /bookings
  ↓
Booking is saved in Postgres
  ↓
OutboxMessage is saved in Postgres
  ↓
OutboxPublisherWorker publishes BookingCreated to RabbitMQ
  ↓
RabbitMQ routes message to notification-booking-created.queue
  ↓
Notification.Worker consumes the message
  ↓
Notification inbox records the message as Processed
  ↓
Notification message is marked as Sent
```

## Docker containers

Check running containers:

```powershell
docker compose ps
```

Start infrastructure:

```powershell
docker compose up -d
```

Stop infrastructure:

```powershell
docker compose down
```

View RabbitMQ logs:

```powershell
docker logs operahouse-rabbitmq
```

View Postgres logs:

```powershell
docker logs operahouse-postgres
```

## RabbitMQ Management UI

URL:

```text
http://localhost:15672
```

Credentials:

```text
operahouse / operahouse
```

Important exchanges:

```text
operahouse.events
operahouse.retry
operahouse.dead-letter
```

Important queues:

```text
notification-booking-created.queue
notification-booking-created.retry.queue
notification-booking-created.dead-letter.queue
```

## Inspect RabbitMQ queues from terminal

```powershell
docker exec operahouse-rabbitmq rabbitmqctl list_queues name messages_ready messages_unacknowledged messages arguments
```

Useful meanings:

```text
messages_ready = messages waiting to be consumed
messages_unacknowledged = messages delivered to a consumer but not ACKed yet
messages = total messages in the queue
```

## Inspect a message in RabbitMQ UI

1. Open RabbitMQ Management UI.
2. Go to **Queues and Streams**.
3. Open the queue.
4. Scroll to **Get messages**.
5. Set **Ack mode** to:

```text
Nack message requeue true
```

6. Click **Get Message(s)**.

This lets you inspect the message without removing it.

## Expected RabbitMQ message properties

A published `BookingCreated` message should have:

```text
message_id
correlation_id
type
timestamp
content_type = application/json
delivery_mode = persistent
```

Expected headers:

```text
message-id
correlation-id
message-type
```

## Expected retry/DLQ headers

When a message fails and is retried, it should include:

```text
x-retry-count
x-original-exchange
x-original-routing-key
x-first-failed-at
x-last-failed-at
x-last-error
```

These headers help explain why a message failed and how many times it was retried.

## Booking outbox

Check latest outbox messages:

```powershell
docker exec operahouse-postgres psql -U operahouse -d operahouse -c 'select "MessageId", "CorrelationId", "Type", "RoutingKey", "CreatedAt", "ProcessedAt", "RetryCount", "LastError" from "OutboxMessages" order by "CreatedAt" desc limit 10;'
```

Interpretation:

```text
ProcessedAt has value = message was published
ProcessedAt is null = message is waiting to be published
LastError has value = publisher failed
RetryCount > 0 = publisher had to retry
```

## Notification inbox

Check latest consumed messages:

```powershell
docker exec operahouse-postgres psql -U operahouse -d operahouse -c 'select "MessageId", "Consumer", "Status", "ReceivedAt", "ProcessedAt", "LastError" from notification_inbox_messages order by "ReceivedAt" desc limit 10;'
```

Interpretation:

```text
Processed = message was handled successfully
Failed = processing failed
Processing = message started processing but did not finish
```

## Notification messages

Check latest notification records:

```powershell
docker exec operahouse-postgres psql -U operahouse -d operahouse -c 'select "Recipient", "Subject", "Status", "CreatedAt", "SentAt", "FailedAt", "FailureReason" from notification_messages order by "CreatedAt" desc limit 10;'
```

Interpretation:

```text
Sent = email was sent by the notification sender
Failed = email sending failed
Pending = notification was created but not completed
```

## Happy path test

1. Start infrastructure:

```powershell
docker compose up -d
```

2. Run Booking API.
3. Run Notification Worker.
4. Create booking through Swagger:

```text
POST /bookings
```

5. Check outbox:

```text
OutboxMessage should have ProcessedAt set.
```

6. Check notification inbox:

```text
Inbox row should be Processed.
```

7. Check notification messages:

```text
Notification row should be Sent.
```

8. RabbitMQ main queue should usually be empty because the worker consumed and ACKed the message.

## Retry/DLQ test

Temporarily force the worker to fail by throwing an exception in the notification processing flow.

Example:

```csharp
throw new InvalidOperationException("Testing retry metadata.");
```

Then:

1. Restart Notification Worker.
2. Create a booking.
3. Watch worker logs.
4. Inspect retry queue quickly, or wait until retries are exhausted.
5. Inspect dead-letter queue.

Expected result:

```text
Message is retried.
After max retries, message lands in notification-booking-created.dead-letter.queue.
DLQ message contains retry headers.
```

Important: remove the temporary exception after testing.

## Common issues

### Queue is empty after creating booking

This is usually normal.

If the worker is running and processing succeeds:

```text
message is consumed
ACK is sent
RabbitMQ removes it from the queue
```

Check the notification inbox and notification messages tables instead.

### Message is unacknowledged

If `messages_unacknowledged` is greater than zero:

```text
A consumer received the message but has not ACKed/NACKed it.
```

Possible causes:

```text
Worker is stuck
Worker is debugging at a breakpoint
Worker crashed mid-processing
Long-running handler
```

Restarting the worker usually returns the message to ready state.

### Retry queue is always empty

This may be normal because retry messages only wait for the configured TTL.

Current retry delay:

```text
10000 ms
```

So the message may only stay in the retry queue for 10 seconds before returning to the main queue.

Check the DLQ after max retries.

### Failed to fetch in Swagger

Usually caused by HTTP/HTTPS mismatch or untrusted local dev certificate.

Try opening Swagger with HTTPS:

```text
https://localhost:7193/swagger
```

Or trust dev cert:

```powershell
dotnet dev-certs https --trust
```

### RabbitMQ login fails

Check configured credentials.

Expected local credentials:

```text
operahouse / operahouse
```

If a local RabbitMQ instance is also running outside Docker, it may conflict with Docker port `5672`.

## Useful health endpoints

Booking API:

```text
GET /health
```

Expected:

```text
Healthy
```

Worker:

```text
Health is logged periodically by HealthLoggingWorker.
```
