# Project Working Rules

## Purpose

OperaHouse is a learning project for understanding RabbitMQ and gradually mastering its features using bare `RabbitMQ.Client`.

## Development Rules

1. Keep code simple, readable, and understandable.
    - Avoid unnecessary abstractions.
    - Avoid overly complicated patterns.
    - Avoid methods or behavior that are difficult to discover.
    - Prefer explicit code while learning RabbitMQ concepts.

2. Do not create commits, push changes, or perform other Git operations without explicit approval.

3. Ask before making changes to the solution.

4. Before each implementation:
    - Explain the proposed changes.
    - Ask whether I want the changes implemented in the solution or prefer to write them myself with guidance.

## Current Scope

The current phase covers only:

- Creating bookings.
- Publishing `booking.created` events.
- Consuming those events in the notification worker.
- Logging a simulated booking confirmation email.

Do not implement ticketing, payments, or invoicing yet.