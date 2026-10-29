# Decisions and versions

Versions checked on 2026-10-29 against official release pages and NuGet flat-container metadata. Stable releases are preferred; preview .NET 11 and PostgreSQL 19 RC are intentionally excluded. Exact package versions live in csproj files, SDK in global.json and container tags in Dockerfile/compose.yaml.

- .NET SDK 10.0.403, runtime/ASP.NET Core/EF Core 10.0.14.
- PostgreSQL 18.6 and Npgsql EF provider 10.0.3.
- RabbitMQ 4.4.0-management and official RabbitMQ.Client 7.2.2.
- Swashbuckle.AspNetCore 10.3.1, xUnit 2.9.3, runner 4.0.1, Microsoft.NET.Test.Sdk 18.11.0. xUnit v2 is a maintained test framework line; switching to a different framework package solely for a larger major number is unnecessary.

Sources: https://dotnet.microsoft.com/en-us/download/dotnet/10.0 ; https://www.postgresql.org/docs/release/ ; https://www.rabbitmq.com/release-information ; https://www.nuget.org/packages/RabbitMQ.Client ; https://www.nuget.org/packages/Swashbuckle.AspNetCore .

## Trade-offs

PostgreSQL provides precise decimal storage, ACID transactions, locking and useful indexes. SQL Server would also satisfy the problem; PostgreSQL offers straightforward reproducible containers. One deployment is sufficient for one financial bounded context. CQRS is separate handlers/queries, not separate databases. DI uses the built-in container, not a service locator.

Outbox + idempotent consumer avoid the lost-event gap of saving money and publishing independently. RabbitMQ is deliberately outside the balance path; accepting financial commands asynchronously would require pending/rejected statuses and a more complex API contract. Publisher confirms do not create an exactly-once guarantee.

Pessimistic locking is easy to reason about for short single-account operations. Optimistic concurrency would reduce lock holding time but requires bounded retries under contention. Events are not the ledger source of truth; this is not Event Sourcing. Persistent snapshots, service separation, read replicas and caching are deferred until measurements justify them.
