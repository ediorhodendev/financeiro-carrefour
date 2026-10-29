# Assessment mapping

The supplied assessment asks for a robust C# system that registers credit/debit movements and provides a customer's consolidated balance at a chosen instant. It asks the candidate to justify structure, technologies, consistency, concurrency, partial failure behavior, demand, sensitive-data protection and maintainability.

| Mandatory requirement | Implementation/evidence |
|---|---|
| C# implementation | Four source projects targeting net10.0 |
| Automated tests | Domain/application, HTTP, PostgreSQL integration and Docker/RabbitMQ smoke |
| Public GitHub repository | https://github.com/ediorhodendev/financeiro-carrefour |
| Clear local instructions | Root README; one Compose command |
| All documentation in repository | README and docs directory |
| Compile with no errors/warnings | TreatWarningsAsErrors and MSBuildTreatWarningsAsErrors; CI build |
| Credits/debits | RegisterMovementHandler; account invariants and ACID transaction |
| Balance at an instant | AccountQueries and indexed historical aggregation |
| Consistency and concurrent requests | Row locking, uniqueness, idempotency and integration tests |
| Partial failure reasoning | Architecture document and broker outage/recovery smoke |
| Data protection | JWT role/owner enforcement, minimal events, parameterized SQL and local-only demo |

Frontend, messaging, microservices and a particular database are not mandated. RabbitMQ is added to demonstrate reliable integration skills; PostgreSQL remains the financial source of truth. No framework or pattern is included solely to inflate project size.
