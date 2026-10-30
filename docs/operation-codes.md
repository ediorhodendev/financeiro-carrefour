# Stable movement operation codes

The public HTTP contract uses numeric operation codes in both requests and responses:

| Code | English name | Financial effect |
|---|---|---|
| 1 | Credit | Adds funds |
| 2 | Debit | Removes funds, requiring sufficient balance |

C# declares explicit stable values, never implicit ordinal positions:

```csharp
public enum MovementType { Credit = 1, Debit = 2 }
```

Zero is undefined. Negative values, unknown codes, fractional values and string names/codes return HTTP 400 before any financial write. The required type field must be present. New operation names must get new codes; never renumber or reuse existing ones. Swagger documents the mapping, numeric enum and example.

Credit example:

```json
{"type":1,"amount":100}
```

Debit example:

```json
{"type":2,"amount":30}
```

## Compatibility and existing data

This intentionally changes the HTTP contract: callers using `"Credit"` or `"Debit"` strings must send 1/2 instead, and response type is now numeric too. Refresh Swagger after rebuilding Docker. Changing type representation does not change the monetary rules. A reversal still accepts only a reason and derives its code and amount from the original.

PostgreSQL continues to persist the stable names Credit/Debit through EF's string conversion; existing financial rows, checks, balances and historical queries require no data conversion. The reversal schema migration is separate and additive.

Idempotency hashes use the canonical operation names, not their ordinal integer values. Replaying an old operation with the same key and equivalent new numeric payload still resolves the original movement. Do not change the key simply because the representation changed.

## Message compatibility

New Outbox messages use MovementRecorded **Version 2**, with Type 1=Credit/2=Debit. The consumer also reads legacy **Version 1**, whose old producer encoded Type 0=Credit/1=Debit. It translates v1 explicitly before validating the normalized v2 contract. Unknown versions/codes are rejected to the DLQ; ambiguous codes are never interpreted without checking Version. This preserves pending old Outbox and RabbitMQ messages.

Existing durable exchange/queue names remain unchanged to drain old deliveries. The AMQP type header identifies MovementRecorded; the payload's Version determines decoding. Free-text reversal reasons are not sent through messaging. Older consumers that understand only v1 must be upgraded to the dual-version consumer before receiving v2 events in a multi-instance production rollout. The local Compose upgrade replaces the API and its hosted consumer together.
