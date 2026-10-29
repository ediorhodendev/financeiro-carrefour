# Architecture and operational behavior

The solution uses Clean Architecture and lightweight CQRS within one application, with explicit constructor injection. Domain has no EF/HTTP dependencies; Application depends on Domain; Infrastructure implements application contracts; API composes dependencies. Read and write handlers share PostgreSQL. This keeps assessment scope practical while exposing the important decisions.

## Financial consistency

Acquire the owner's account using a parameterized SELECT FOR UPDATE. After the lock, resolve the idempotency key, enforce monetary rules, obtain PostgreSQL clock_timestamp(), then persist balance, immutable movement and outbox in one transaction. PostgreSQL unique constraints are the final defense for identities, accounts and idempotency keys. All external values are parameterized. Locks have a five-second timeout; statements and EF commands have bounded timeouts.

## Messaging

The hosted worker declares a durable fanout exchange, a quorum consumer queue, and a durable quorum dead-letter queue. An Outbox batch selects at most 20 unpublished rows using FOR UPDATE SKIP LOCKED. It publishes persistent messages with mandatory routing and publisher confirmations, then marks rows as published and commits. Multiple API instances do not claim the same locked batch.

A crash after broker confirmation but before database commit can publish a duplicate. This is intentional at-least-once delivery. The consumer validates the versioned payload, atomically INSERTs its receipt with a unique EventId and acknowledges only after persistence. A crash after persistence but before ACK is harmless. Repeated delivery creates one receipt and never another financial movement.

For this small assessment, bounded BasicGet polling avoids another deployment and an event callback concurrency model. Each iteration processes at most 20 publishes and 20 deliveries, then delays 250 ms. A production high-throughput consumer should use BasicConsume, bounded prefetch, separate publisher/consumer connections and measurements. Integration events have no guaranteed account ordering; the demonstrated consumer is order-independent.

Invalid JSON or contract data is rejected to the DLQ immediately. Transient consumption failures NACK/requeue and reconnect after five seconds. Quorum delivery-limit 5 bounds repeated unsuccessful delivery; at-least-once dead-lettering with reject-publish overflow protects transfer to the DLQ. Inspect, diagnose and replay a corrected event deliberately, preserving its EventId; never replay blindly. The Outbox itself retries indefinitely on broker failure so committed events are not discarded. Ten-second publish timeouts bound each attempt. Sustained broker outage grows the database Outbox; alert on age/count and define a retention policy before production. This demo retains all Outbox and receipt records.

## Failure boundaries

| Failure | Behavior |
|---|---|
| Two simultaneous debits | One account lock at a time; insufficient-funds debit is rejected |
| Duplicate HTTP request | Original movement returned without another write/event |
| Financial save/commit fails | Transaction rolls back all financial writes and Outbox |
| Commit succeeds, response lost | Retry same key returns original movement |
| PostgreSQL unavailable | Financial writes fail closed, with controlled error; no fabricated balance |
| RabbitMQ unavailable | Financial API remains usable, Outbox accumulates and retries |
| Publisher crashes after confirm | Possible duplicate delivery, safely deduplicated by consumer |
| Consumer crashes before ACK | Redelivery; receipt uniqueness prevents repeated effect |
| Poison event | Durable dead-letter queue for investigation |

Readiness depends on PostgreSQL/schema; RabbitMQ is deliberately excluded because it is not on the financial consistency path. Liveness only reports process availability. HTTP logs include status, elapsed time and correlation ID, without bodies, JWTs or financial payloads. Worker logs expose error class, not secret connection strings.

## Scaling and production boundaries

Index historical queries by account/time/ID and idempotency by account/key. Row locking creates a serialization point for very hot accounts; measure lock wait percentiles. Historical SUM grows with the account ledger; consider verified snapshots as volume grows. Monitor request latency, DB lock waits, outbox oldest age, queue depth, retry and DLQ counts. Use load testing, capacity planning and backpressure before claiming high-demand readiness.

Production work: replicated PostgreSQL/RabbitMQ and backup drills; OIDC provider and audited authorization; TLS; secret rotation; distinct migration/runtime DB roles; event/schema governance; metrics/tracing; retention/reconciliation procedures. No distributed transaction, Redis cache or Event Sourcing is needed for the required financial behavior.
