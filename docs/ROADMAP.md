# OperaHouse Development Roadmap

This document records what has already been built and the planned path toward a production-quality opera and concert ticketing platform.

Status legend:

- `[x]` Implemented
- `[~]` In progress or awaiting final verification
- `[ ]` Planned

## Guiding principles

- Keep the code readable and avoid abstractions that do not solve a current problem.
- Use standard patterns where they have a clear purpose.
- Prefer durable, production-oriented solutions over temporary demonstrations.
- Keep business rules in the Application and Domain projects, not in controllers or infrastructure code.
- Use bare `RabbitMQ.Client` so RabbitMQ concepts remain visible and understandable.
- Do not commit, stage, or push changes without explicit approval.

The complete collaboration rules are in [`PROJECT_RULES.md`](../PROJECT_RULES.md).

## Phase 1 — Booking and basic RabbitMQ flow

- [x] Create the .NET 10 solution and projects.
- [x] Separate Booking into API, Application, Domain, and Infrastructure projects.
- [x] Add PostgreSQL and EF Core.
- [x] Create `Performance` and `Booking` domain models.
- [x] Add `GET /performances`.
- [x] Add `POST /bookings`.
- [x] Add `GET /bookings/{id}`.
- [x] Create the `BookingCreated` integration event.
- [x] Publish `booking.created` to a durable topic exchange.
- [x] Create a durable notification queue and binding.
- [x] Consume with `AsyncEventingBasicConsumer`.
- [x] Use manual ACK and NACK.
- [x] Run RabbitMQ and PostgreSQL through Docker Compose.

## Phase 2 — Reliable messaging and notifications

### 2.1 Outbox and publisher reliability

- [x] Store booking changes and outbox messages in the same PostgreSQL transaction.
- [x] Publish pending outbox records using a background worker.
- [x] Use persistent RabbitMQ messages.
- [x] Add RabbitMQ publisher confirms.
- [x] Track outbox publication attempts and completion.

### 2.2 Retry and dead-letter handling

- [x] Create retry and dead-letter exchanges and queues.
- [x] Add delayed retries using queue TTL and dead-letter routing.
- [x] Limit retry attempts.
- [x] NACK without requeue after retries are exhausted.
- [x] Preserve useful retry metadata in message headers.
- [ ] Classify failures as transient or permanent so permanent failures skip pointless retries.
- [ ] Add an explicit operational process for inspecting and replaying DLQ messages.

### 2.3 Inbox and idempotent consumption

- [x] Split Notification into Worker, Application, Domain, and Infrastructure projects.
- [x] Persist received message IDs in an inbox table.
- [x] Add a unique `(MessageId, Consumer)` constraint.
- [x] Track notification processing status.
- [x] Avoid reprocessing messages already recorded as processed.
- [x] Persist notification records separately from inbox records.

### 2.4 Diagnostics and health

- [x] Propagate correlation IDs through outbox and RabbitMQ metadata.
- [x] Add correlation-aware logging scopes.
- [x] Add PostgreSQL and RabbitMQ health checks.
- [x] Add a health logging worker.
- [x] Add the messaging debugging runbook.
- [ ] Add structured local logging with Serilog and Seq during the observability phase.

### 2.5 Real email delivery

- [x] Keep email delivery behind `IEmailSender`.
- [x] Replace the fake sender with a MailKit SMTP adapter.
- [x] Add strongly typed and startup-validated email options.
- [x] Store Gmail credentials in .NET User Secrets.
- [~] Verify the complete booking-to-Gmail delivery flow.
- [ ] Classify SMTP failures as transient or permanent.
- [ ] Replace Gmail with Azure Communication Services Email for production.
- [ ] Add provider delivery events, bounce handling, and webhook processing.

## Phase 3 — Administration and performance management

Implementation is complete in code. The generated database migration still
needs to be applied and the full lifecycle needs an end-to-end verification
once the local Docker engine is available.

### 3.1 Performance lifecycle

- [x] Add explicit performance states such as `Draft`, `Published`, `Cancelled`, and `Completed`.
- [x] Allow customers to view only published future performances.
- [x] Prevent bookings for draft, cancelled, completed, or past performances.

### 3.2 Admin use cases

- [x] Create a performance.
- [x] Update performance details.
- [x] Publish a performance.
- [x] Cancel a performance.
- [x] List all performances, including non-public ones.
- [ ] Record administrative actions where auditing is important.

### 3.3 API placement

Keep admin HTTP endpoints in an `Admin` area of `OperaHouse.Booking.Api` while reusing the existing Booking Application and Domain layers. Do not create a separate microservice until it has a clear independent data-ownership or scaling requirement.

### 3.4 Admin security

- [x] Add JWT bearer authentication with local development tokens.
- [x] Add an administrator authorization policy.
- [x] Protect all management endpoints.
- [x] Keep public browsing and guest booking anonymous.
- [ ] Prepare for Microsoft Entra ID in Azure.

## Phase 4 — Booking and seat consistency

- [ ] Atomically reserve seats when creating a booking.
- [ ] Prevent overselling under concurrent requests.
- [ ] Define `Pending`, `Confirmed`, `Expired`, and `Cancelled` booking states.
- [ ] Add booking expiration.
- [ ] Release seats when an unpaid booking expires or is cancelled.
- [ ] Add database-level concurrency protection and tests.

This phase must precede payment integration because payment must not succeed for seats the system cannot guarantee.

## Phase 5 — Payments

- [ ] Add Payment Domain, Application, Infrastructure, and processing components where justified.
- [ ] Create a payment session for a pending booking.
- [ ] Integrate a payment provider sandbox.
- [ ] Receive and verify provider webhooks.
- [ ] Make webhook processing idempotent.
- [ ] Publish `payment.succeeded` and `payment.failed` events.
- [ ] Confirm the booking only after successful payment.
- [ ] Store provider secrets outside source control and later in Azure Key Vault.

## Phase 6 — Ticketing

- [ ] Issue tickets only for confirmed bookings.
- [ ] Generate unique ticket identifiers.
- [ ] Add QR or barcode values.
- [ ] Generate PDF tickets.
- [ ] Store ticket documents in Azure Blob Storage.
- [ ] Publish `ticket.issued`.
- [ ] Send the final ticket email from `ticket.issued`, not `booking.created`.

## Phase 7 — Expanded production notifications

- [ ] Add booking received, payment, confirmation, ticket, cancellation, and refund notifications.
- [ ] Add maintainable HTML email templates.
- [ ] Track provider message IDs and delivery status.
- [ ] Process delivered, bounced, blocked, and complaint events.
- [ ] Avoid logging customer email bodies or credentials.

## Phase 8 — Invoicing

- [ ] Create invoices after successful payment.
- [ ] Preserve an immutable financial and customer snapshot.
- [ ] Generate invoice numbers and PDF documents.
- [ ] Store invoices in Azure Blob Storage.
- [ ] Publish `invoice.created`.
- [ ] Notify customers using secure links or attachments.

## Phase 9 — Cancellation and refunds

- [ ] Define cancellation deadlines and business rules.
- [ ] Cancel bookings and release seats.
- [ ] Integrate payment refunds.
- [ ] Invalidate issued tickets.
- [ ] Handle complete performance cancellation.
- [ ] Publish cancellation, refund, and ticket invalidation events.

## Phase 10 — Observability

### Local development

- [ ] Add Serilog structured logging.
- [ ] Add Seq for searching and visualizing logs.
- [ ] Add OpenTelemetry traces and metrics.
- [ ] Trace API, PostgreSQL, RabbitMQ publishing, consumption, and external calls.

### Azure production

- [ ] Export telemetry to Azure Monitor and Application Insights.
- [ ] Add dashboards, alerts, and availability checks.
- [ ] Preserve correlation across APIs, workers, messages, and databases.

## Phase 11 — Testing and operational reliability

- [ ] Add Domain and Application unit tests.
- [ ] Add PostgreSQL integration tests.
- [ ] Add RabbitMQ integration tests.
- [ ] Add end-to-end booking tests.
- [ ] Test outbox, inbox, retries, DLQ, concurrency, and duplicate delivery.
- [ ] Add graceful shutdown and migration validation.
- [ ] Separate liveness and readiness checks where deployment requires it.

## Phase 12 — Security and privacy hardening

- [ ] Add rate limiting and comprehensive input validation.
- [ ] Use secure guest-booking access links.
- [ ] Verify all external webhook signatures.
- [ ] Define personal-data retention and deletion rules.
- [ ] Keep personal data out of logs and RabbitMQ headers.
- [ ] Use HTTPS and production secret management.
- [ ] Add audit records for sensitive administrative actions.

## Phase 13 — Azure deployment

Planned service mapping:

| Component | Azure service |
| --- | --- |
| Booking API and workers | Azure Container Apps |
| Container images | Azure Container Registry |
| PostgreSQL | Azure Database for PostgreSQL |
| RabbitMQ | Managed RabbitMQ provider or a carefully managed RabbitMQ deployment |
| Tickets and invoices | Azure Blob Storage |
| Secrets | Azure Key Vault |
| Administrator identity | Microsoft Entra ID |
| Production email | Azure Communication Services Email |
| Logs, traces, and metrics | Application Insights and Azure Monitor |

Deployment work:

- [ ] Add production Dockerfiles.
- [ ] Define Azure infrastructure with Bicep.
- [ ] Use managed identities where supported.
- [ ] Add GitHub Actions build, test, and deployment pipelines.
- [ ] Define database migration, backup, and recovery procedures.
- [ ] Configure scaling, dashboards, alerts, and cost controls.
