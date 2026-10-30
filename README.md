# financeiro-carrefour

A focused financial ledger assessment by Edio Rhoden. C#/.NET 10, PostgreSQL, RabbitMQ, Clean Architecture, explicit dependency injection and lightweight CQRS. All source identifiers and documentation are in English.

## Run everything with one command

Prerequisite: Docker Desktop with Linux containers and Compose v2 (or Docker Engine with Compose v2). No local .NET, PostgreSQL, RabbitMQ or `.env` file is required.

```sh
docker compose up --build -d
```

Open [Swagger](http://localhost:5080/swagger). Compose starts PostgreSQL and RabbitMQ, applies EF migrations, seeds one fictitious customer/account and starts the API. RabbitMQ failure does not block financial writes.

1. Execute `POST /demo/access` in Swagger. Copy `accountId` and `operatorToken`.
2. Click **Authorize**, paste only `operatorToken` and authorize.
3. Execute `POST /api/accounts/{accountId}/movements`, with `Idempotency-Key: first-credit` and body `{"type":"Credit","amount":100}`.
4. Repeat the same request/key: response is 200 with the original movement, without a second credit. New key means a new operation.
5. Submit `{"type":"Debit","amount":30}` with another key.
6. Query `/balance` (70 BRL), `/movements` and `/receipts`. Receipts demonstrate asynchronous, idempotent RabbitMQ consumption; allow a short delay.
7. For historical balance, supply `at` with the exact credit's `registeredAt`; the debit is excluded when its timestamp is later.
8. Use `adminToken` from `/demo/access` to create another customer and open their account. The customer's `subjectId` must match their external identity provider's JWT `sub`.

[ RabbitMQ management ](http://localhost:15672): user `financial_local`, password `rabbit-local-demo-only`. Inspect `financial.receipts.v1` and `financial.receipts.dead.v1`. These are **local demo credentials**, not production secrets. Broker AMQP is internal to Docker. API/management/database host ports bind to loopback.

```sh
docker compose logs -f api
docker compose down
```

`down` preserves data. `docker compose down -v` deletes this project's demo data. Different projects have separate named volumes. Optional overrides: `API_PORT`, `POSTGRES_PORT`, `RABBITMQ_MANAGEMENT_PORT`, `POSTGRES_PASSWORD`, `RABBITMQ_PASSWORD`. Changing a database/broker password on an existing volume also requires changing it in that service; environment variables do not rotate persisted credentials.

## Architecture

| Project | Responsibility |
|---|---|
| `Financial.Domain` | Customer, Account, Movement and monetary invariants |
| `Financial.Application` | Command handlers, queries, persistence contracts and versioned event contract |
| `Financial.Infrastructure` | EF Core/PostgreSQL transactions, row locks, migrations, Outbox and idempotent receipt persistence |
| `Financial.Api` | REST, JWT authorization, Swagger, dependency injection and messaging background worker |

Commands and queries have separate code paths, with one authoritative database. No MediatR, generic repository, distributed transaction or unnecessary microservice deployment. The worker is a hosted service in the API; it can be extracted later without changing the financial transaction.

`HTTP command -> account row lock -> balance + immutable movement + outbox -> COMMIT -> publisher confirm -> RabbitMQ -> inbox/receipt INSERT -> ACK`

Money is committed synchronously. RabbitMQ transports integration events, never decides the balance. Delivery is **at least once**; the consumer effect is idempotent, not an exactly-once transport promise.

## API

| Method | Path | Permission |
|---|---|---|
| POST | `/demo/access` | Local Development demo only |
| POST | `/api/customers` | admin |
| GET | `/api/customers/{customerId}` | admin |
| POST | `/api/customers/{customerId}/accounts` | admin |
| GET | `/api/accounts/{accountId}` | owner or admin |
| POST | `/api/accounts/{accountId}/movements` | operator + ownership; Idempotency-Key required |
| POST | `/api/accounts/{accountId}/movements/{movementId}/reversal` | operator + ownership; full reversal with reason and Idempotency-Key |
| GET | `/api/accounts/{accountId}/balance?at=...` | operator + ownership |
| GET | `/api/accounts/{accountId}/movements?limit=50&cursor=...` | operator + ownership |
| GET | `/api/accounts/{accountId}/receipts` | operator + ownership; latest 100 |
| GET | `/health/live`, `/health/ready` | anonymous; no financial data |

Errors use Problem Details: 400 invalid input, 401 invalid/missing token, 403 missing role, 404 missing/inaccessible account, 409 insufficient funds/idempotency conflict/duplicate provisioning, 503 transient persistence failure. Cancellation and retries reuse the same idempotency key.

## Tests

Run the entire automated suite without installing .NET:

```sh
docker compose --profile tests run --build --rm tests
```

It starts a separate PostgreSQL test service. The CI workflow also runs the real RabbitMQ/Docker smoke test (including broker outage/recovery, duplicate events and DLQ). That shell-based end-to-end check requires Python 3 and Bash on the host; these are not needed to run the application.

With .NET SDK 10.0.403 installed:

```sh
dotnet restore Financial.slnx
dotnet build Financial.slnx -c Release
dotnet test tests/Financial.Domain.Tests -c Release
dotnet test tests/Financial.Api.Tests -c Release --filter "FullyQualifiedName!~ApiPostgresTests"
```

For integration tests, set `FINANCIAL_TEST_CONNECTION` to an **isolated test PostgreSQL database**, then `dotnet test Financial.slnx -c Release --collect:"XPlat Code Coverage"`. Missing database configuration fails the relevant tests rather than silently skipping them. TRX and Cobertura coverage artifacts are uploaded by GitHub Actions. Coverage is evidence of exercised code, not a claim that every failure scenario is covered.

## Decisions, rules and delivery

- [Full reversal rules and Swagger examples](docs/reversals.md)
- [Business rules](docs/business-rules.md)
- [Architecture and failure behavior](docs/architecture.md)
- [Requirement mapping](docs/requirements.md)
- [Versions and alternatives](docs/decisions.md)
- [Validation and production limits](docs/validation.md)

The demo token issuer is disabled outside Development and requires explicit `Demo__Enabled=true`. Production requires a real JWT authority/audience, TLS, managed secrets, least-privilege database users, backups, recovery drills and a replicated deployment. This assessment does not claim production banking certification or measured peak capacity.
