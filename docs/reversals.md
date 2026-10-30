# Full movement reversals

`POST /api/accounts/{accountId}/movements/{movementId}/reversal`

Requires an operator JWT belonging to the account owner and one `Idempotency-Key` header. Administrators without the operator role/ownership cannot reverse financial operations. Body:

```json
{"reason":"Movement registered incorrectly"}
```

## Business rules

- Only a full reversal is supported. Unknown request fields (including amount/type) are rejected with 400. The client supplies no amount/type: both are derived from the immutable original. A credit generates an equal debit; a debit generates an equal credit.
- The reason is required, trimmed, and must contain 1-500 characters after trimming. It is stored on the compensation and exposed only through authorized account history; free-text reasons are not sent to RabbitMQ.
- A movement can be reversed once. No partial reversal, no reversal of a reversal and no deletion/update of the original are allowed.
- Credit reversal requires sufficient current funds. Debit reversal must not exceed the maximum balance. A rejection changes no financial data and reserves no successful idempotency key; a later retry can succeed once funds are available.
- Original movement must exist in the specified account. Missing/inaccessible account or movement returns 404.
- Idempotency keys are shared with normal movements within the account. Same key + original ID + trimmed reason returns the same compensation with 200/replayed=true. First success returns 201/replayed=false. Changed target/reason, or an ordinary-operation key, returns 409. A new key for an already reversed original also returns 409. Reason case is significant.
- The account row lock serializes reversal, credit and debit operations. Database unique index on ReversalOfMovementId prevents duplicate compensation even if application-level checks are bypassed. A foreign key, metadata check and insertion trigger protect the original reference, same-account pairing, inverse type/equal amount and prohibition on reversing a reversal.
- Balance, compensating movement and Outbox event are committed together. Any failure rolls back all three. Replaying a successful request never creates another movement/event, even if current funds would no longer permit the original action.
- Current balance includes the compensation. Historical queries before its registration retain the original balance; the original timestamp is never rewritten. Statement items identify a reversal using reversalOfMovementId and reversalReason. Query the statement to find the compensation for a given original.
- The compensation emits the existing MovementRecorded.v1 contract, with optional reversalOfMovementId metadata. Old events without this field remain valid. Receipt processing remains idempotent by the compensation's event ID.

## Swagger test

1. Register Credit 100 with key `reversal-demo-credit` and copy its movement ID.
2. Reverse that movement with key `reversal-demo-001` and the reason above: 201, type Debit, amount 100, balance back to the pre-credit value.
3. Repeat identical request/key: 200, same compensation ID and timestamp, replayed=true.
4. Change the reason with the same key: 409. Use a new key for that original: 409.
5. Try reversing the compensation itself: 409.
6. Credit another 100, spend part of it, then try reversing that original credit when total funds are below 100: 409; balance unchanged.
7. Check the statement, pre-reversal historical balance and consumer receipt.

## Upgrade

Run `git pull` followed by `docker compose up --build -d` from the project root. The migrations service applies AddMovementReversals to the existing database. Existing customers, balances, movements and volumes are preserved; old movements get null reversal metadata. Do not use `down -v` when upgrading.
