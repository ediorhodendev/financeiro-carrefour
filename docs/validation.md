# Validation

## Verified execution

[GitHub Actions run 40971701334](https://github.com/ediorhodendev/financeiro-carrefour/actions/runs/40971701334), source commit `7839e5b`, passed on 2026-10-29:

- Build: zero errors and zero warnings.
- 71 automated tests passed, zero skipped: 42 domain/application, 14 PostgreSQL integration and 15 HTTP/API tests.
- Complete Docker stack passed: migrations, demo access, credit/debit, replay, receipts, duplicate-event deduplication, malformed-event DLQ and broker outage/recovery. Final balance: 75 BRL.
- Bounded exercise: 100 balance reads and 100 credits, no HTTP failures; 100 credits of 0.01 BRL produced exactly 1.00 BRL.
- TRX, coverage and Compose logs are attached as the test-results CI artifact (14-day retention).

These are measured assessment checks, not peak-capacity or production-certification claims. Consult the workflow run for the current delivered commit when modifying code.

The workflow restores and compiles with warnings treated as errors, runs all test projects against a real isolated PostgreSQL database, collects TRX/Cobertura coverage, executes a bounded load exercise and launches the complete Docker Compose stack. The Docker smoke verifies migrations, demo JWT, HTTP replay, debit/current balance, asynchronous receipts, duplicate-event deduplication, poison-message DLQ and financial operations during broker outage followed by Outbox recovery.

Tests cover monetary validation, insufficient balance, maximum balance, idempotency conflicts/replays, rollback, concurrent debits/credits, immutable ledger, history boundaries, cursor validation, provisioning conflicts, JWT roles/ownership, invalid requests, unavailable database, event contract validation and concurrent inbox deduplication.

Run the suite with `docker compose --profile tests run --build --rm tests`. See the README for local SDK execution. Coverage reports help locate unexercised paths; no 100% coverage or production capacity claim is made. The bounded load script is a reproducible smoke benchmark, not an SLA, endurance or failover certification.

The local authoring environment has no Docker daemon. Local compilation and non-database tests are separate from CI's real-service validation. A successful CI result is required before describing this delivery as end-to-end validated.
