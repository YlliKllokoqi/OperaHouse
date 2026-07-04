# Project Working Rules

## Purpose

OperaHouse is a learning project for understanding RabbitMQ and gradually mastering its features using bare `RabbitMQ.Client`.

## Development Rules

1. Keep code simple, readable, and understandable.
    - Avoid unnecessary abstractions.
    - Avoid overly complicated patterns.
    - Avoid methods or behavior that are difficult to discover.
    - Prefer explicit code while learning RabbitMQ concepts.
    - If a simpler code solution can satisfy the requirement clearly and safely, choose it.
    - Do not add boilerplate, layers, wrappers, or patterns unless they solve a real problem in the current project.
    - Production-grade does not mean overengineered; prefer the smallest robust solution.
    - Follow standard, recognizable coding patterns where they fit naturally, such as Repository, Factory, Options, Background Worker, and Adapter.
    - Do not force a pattern if it makes the feature harder to understand or weakens the final design.
    - If a standard pattern is not a good fit, explain why and propose a clearer alternative before implementing it.
    - Do not choose a short-term "good enough for now" route when a scalable, production-grade long-term solution is appropriate.
    - Prefer designs that can grow with the final project, even if they require a little more structure, as long as the structure has a clear purpose.
    - If there is tension between simplicity and long-term production quality, explain the tradeoff and recommend the production-grade path.

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
