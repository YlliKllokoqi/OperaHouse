# Regression checks

Run from the repository root:

```sh
dotnet run --project tests/Regression/OperaHouse.RegressionTests
```

Run the notification and worker checks independently.

```sh
dotnet run --project tests/Regression/OperaHouse.RegressionTests -- --notifications
```

For cancellation transaction checks, create a fresh disposable PostgreSQL database named
`operahouse_cancel_test` or with that prefix and set `OPERAHOUSE_CANCELLATION_TEST_DB`
to its connection string. The runner creates the schema in the empty database and leaves the
test rows available for inspection. Use a fresh database for each run.

```sh
dotnet run --project tests/Regression/OperaHouse.RegressionTests -- --cancellation-db
```

This small assertion-based runner uses the application's existing dependencies.
It checks malformed events, notification status after email or database failures,
duplicate processing, worker session recovery, exact queue topology/retry routing, and UTC normalization.
It also checks moved feature validation and HTTP contracts, DTO mapping, original migration
discovery, business project dependency boundaries, and schema compatibility against both EF model snapshots without opening a connection.
Email and RabbitMQ use in-process substitutes. No SMTP connection, PostgreSQL,
or real broker is required.

SMTP remains at least once: a crash or lost database save after server acceptance
can cause a later delivery to send the email again.
