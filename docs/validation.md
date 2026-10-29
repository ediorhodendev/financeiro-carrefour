# Validation

Validation results must be read from the GitHub Actions execution for the delivered commit; do not infer new results from the predecessor repository.

The workflow restores and compiles with warnings treated as errors, runs all test projects against a real isolated PostgreSQL database, collects TRX/Cobertura coverage, executes a bounded load exercise and launches the complete Docker Compose stack. The Docker smoke verifies migrations, demo JWT, HTTP replay, debit/current balance, asynchronous receipts, duplicate-event deduplication, poison-message DLQ and financial operations during broker outage followed by Outbox recovery.

Tests cover monetary validation, insufficient balance, maximum balance, idempotency conflicts/replays, rollback, concurrent debits/credits, immutable ledger, history boundaries, cursor validation, provisioning conflicts, JWT roles/ownership, invalid requests, unavailable database, event contract validation and concurrent inbox deduplication.

Run the suite with `docker compose --profile tests run --build --rm tests`. See the README for local SDK execution. Coverage reports help locate unexercised paths; no 100% coverage or production capacity claim is made. The bounded load script is a reproducible smoke benchmark, not an SLA, endurance or failover certification.

The local authoring environment has no Docker daemon. Local compilation and non-database tests are separate from CI's real-service validation. A successful CI result is required before describing this delivery as end-to-end validated.
