# BusinessOS Restaurant — Web/Flutter KOT/KDS realignment implementation report

Branch: `feat/kot-realignment-web`  
PR: #67  
Ownership: Laravel/Web/SaaS/API + Flutter only. Desktop source was not modified.

## Implemented scope

### KOT rounds and idempotency
- One operational order can produce multiple KOT dispatch rounds.
- SEND dispatches only currently unsent quantities.
- Stable mutation identity makes retries replay-safe.
- Human KOT numbering is branch/business-day scoped and concurrency-safe.
- Historical one-shot KOTs remain compatible as Round 1.

### Kitchen workflow
- Canonical production states: held, queued, active, preparing, ready, completed, voided, cancelled.
- Queue and Preparing are independently switchable.
- All four Queue/Preparing combinations are supported.
- Item-level start/ready transitions drive ticket/order aggregates.
- Warning/late aging is derived from timestamps rather than persisted as another state.
- Expo is a readiness projection rather than a duplicate order model.

### Restaurant operating context
- Service types: dine_in, takeaway, delivery, counter.
- Dine-in requires a table; other service types are tableless.
- Seat, course, structured modifiers, ordinary notes, kitchen instructions and allergy/critical instructions are carried through immutable production snapshots.
- Courses support held → FIRE semantics when enabled.
- Menu modifier min/max rules and price deltas are validated server-side and locally in Flutter.

### Recovery and audit
- Post-KOT void keeps production history and records actor/reason/timestamp.
- Manager approval setting is enforced for post-KOT void.
- Re-fire creates new production linked to the original item and is retry-safe.
- Recall returns ready production to the appropriate active/preparing state.
- Waste records are auditable and only valid after production inventory commitment.

### Inventory and accounting
- KOT send reserves expected recipe stock.
- Production commitment consumes reserved ingredients exactly once.
- Queue/Preparing configuration determines the deterministic consumption boundary.
- Cancellation before production releases reservation.
- Cancellation after consumption does not restore ingredients.
- Re-fire reserves/consumes again.
- Negative stock policy is shared through restaurant settings.
- COGS is posted per production consumption without duplicate accounting.

### Active-order operations
- Transfer dine-in orders between available tables in the same branch.
- Move/split unsent item quantities between compatible open orders.
- Fully merge draft/unsent orders.
- Full merge is blocked after dispatched kitchen production exists.
- Billing remains one financial order/bill path and tableless service orders are supported.

### Web
- Web order entry supports service types, modifiers, seats, courses, notes, allergy and kitchen instructions.
- Web KDS exposes item-level operational controls.
- KDS cards show station, round, human KOT, priority, item state, modifiers, course/seat context, allergy emphasis and elapsed-time warning/late state.
- Kitchen performance reporting includes production counts, average queue/prep time, refires, recalls, voids and waste.

### Flutter
- Existing offline-first SQLite/outbox architecture is preserved.
- Active orders remain editable for later KOT rounds.
- Local line cache tracks dispatched vs unsent quantity.
- Flutter sync supports KOT send, course fire, void, refire, recall, table transfer, item move/split and merge.
- Stable IDs are used for retry-safe target lines during offline moves.
- KOT round history/readiness is cached and displayed.
- Structured modifier, seat/course, allergy and kitchen-instruction entry is supported.
- Takeaway, delivery and counter order entry is supported.
- Waiter order UI was redesigned toward the approved premium tablet-first BusinessOS Restaurant layout while remaining responsive on phones.

## Contract alignment with Desktop

The final Desktop handoff was re-read before shared adoption. Web/Flutter preserves the same canonical round fields, statuses and settings. Existing `order.submit` remains backward compatible while `order.kot.send` is additive.

Desktop-local persisted `expo` is not introduced as a new Web/Flutter canonical production state. Web/Flutter keeps Expo as a derived readiness projection, as documented by the shared contract.

## Validation gates

The final branch must pass:
- Laravel Pint
- tenant migration smoke test
- complete PHPUnit suite
- production config/view cache
- frontend asset build
- Flutter format
- Flutter analyzer
- Flutter tests
- Android debug/release build
- unsigned iOS build/package workflow

See the latest GitHub Actions run on PR #67 for the final commit result.
