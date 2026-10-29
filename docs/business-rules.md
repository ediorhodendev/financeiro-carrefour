# Business rules

These are explicit assumptions where the assessment is open-ended.

1. One BRL account per customer, starting at zero. Identity subject is unique. Customer creation and opening an account require the admin role.
2. Amounts use C# decimal and PostgreSQL numeric(18,2). They must be positive, within 9999999999999999.99 and have at most two fractional digits. No silent rounding.
3. Credit increases balance; debit reduces it only with sufficient funds. Balance may never be negative or exceed storage precision. A rejected movement changes nothing.
4. Confirmed movements cannot be updated or deleted, enforced by domain design, EF validation and a PostgreSQL trigger. Correct a mistake with a new compensating movement and a new idempotency key.
5. Idempotency scope is account + key (1-100 nonblank characters). Same key and same normalized type/amount returns the original movement, timestamp and identifier. Different content returns 409. Keys are retained with the ledger, without expiry. Rejected operations are not remembered as successful operations; a retry may succeed if the balance later changes.
6. Account ownership comes from the authenticated JWT subject, never a client-provided owner field. Wrong owner and missing account share 404. Admin provisioning privileges do not bypass financial ownership checks.
7. A PostgreSQL row lock serializes concurrent writes to one account. Different accounts can progress independently. Account balance, movement and outbox insert share one ACID transaction. Rollback reverts all three. A process failure after commit is resolved by replaying the same key.
8. Current balance reads the account projection. Historical balance sums committed movements whose database-assigned registration timestamp is <= the requested instant, in UTC. The instant must include a timezone, cannot be in the future or precede account creation. Backdated bookings are unsupported.
9. Historical queries describe committed records visible to the query. A transaction registered before the requested instant but committed later can become visible in a later query of that instant. This is registration-time history, not a durable wall-clock snapshot of database visibility.
10. Statements use stable keyset ordering by registration time and ID, with a versioned, account-bound cursor. Page size is 1-100; live pages are not a fixed snapshot under concurrent writes.
11. One MovementRecorded.v1 event is persisted per committed movement; HTTP replay creates no event. Event ID equals movement ID. Events contain no customer name, token or idempotency key.
12. The consumer records a processing receipt as the demonstrative integration effect. Event ID uniqueness and INSERT ON CONFLICT make reprocessing atomic and harmless. Consumer receipts never modify balances.
13. Receipt reads are eventually consistent, limited to the latest 100 and scoped to the account owner. Receipt presence demonstrates consumption, not an external customer notification.
