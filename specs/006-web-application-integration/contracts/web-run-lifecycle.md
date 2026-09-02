# Contract: Web Run Lifecycle

**Feature**: 006-web-application-integration | **Plan**: [../plan.md](../plan.md)

Defines how a submitted operation becomes a durable, retrievable run. This is the
contract that makes FR-008, FR-009, FR-010, FR-012, FR-032, and SC-007 testable.

## States

| State | Meaning | Terminal | Result available | Reads as clean |
|---|---|---|---|---|
| `Pending` | Persisted, work not started | No | No | **Never** |
| `Running` | Work in progress | No | No | **Never** |
| `CompletedClean` | Reconciled report; all six counts zero | Yes | Yes | Yes |
| `CompletedWithFindings` | Reconciled report; ≥1 non-zero count | Yes | Yes | No |
| `Failed` | No trustworthy report; diagnostic explains why | Yes | Diagnostic only | **Never** |

The last column is the contract's central safety property: only
`CompletedClean` — which requires an existing reconciled report whose
`DetailedSummary.IsClean` holds — may be presented as clean.

## Transitions

```text
            ┌──────────────────────────────► Failed ──┐
            │                                  ▲      │ RetryAsync
         Pending ────────► Running ────────────┘      │ (explicit only)
            ▲                 │                       │
            └─────────────────┼───────────────────────┘
                              ├──────► CompletedClean          (immutable)
                              └──────► CompletedWithFindings   (immutable)
```

| From | To | Trigger | Guard |
|---|---|---|---|
| — | `Pending` | `SubmitAsync` | Options already validated; record durably created |
| `Pending` | `Running` | Queue picks up the run | — |
| `Pending` | `Failed` | Pre-processing failure | Diagnostic required |
| `Running` | `CompletedClean` | Use case returned `Succeeded` | Report exists **and** `Summary.IsClean` |
| `Running` | `CompletedWithFindings` | Use case returned `Succeeded` | Report exists **and** `!Summary.IsClean` |
| `Running` | `Failed` | Use case returned `Failed`, or run aborted | Diagnostic required |
| `Failed` | `Pending` | `RetryAsync` | Explicit user action only |

**Every other transition MUST be rejected**, notably:

- `Pending → CompletedClean` — a run that never executed cannot be clean.
- `Failed → CompletedClean` / `Failed → CompletedWithFindings` — a failed run has
  no trustworthy report; only an explicit retry may restart it.
- `CompletedClean → *`, `CompletedWithFindings → *` — success is immutable.
- `Running → Pending` — a refresh or reconnect never silently rewinds work.

Rejection MUST surface as a failure, not a silently coerced state. This is the
Principle V posture applied to the lifecycle itself.

## Idempotency and Retry

`WebRunId` is derived deterministically from the source fingerprint and the
canonical resolved-options string (see [../data-model.md](../data-model.md)).
Therefore:

1. **Refresh / double-click / retried POST** with identical bytes and options
   resolves to the *same* `WebRunId`. `TryCreateAsync` returns `false`, and
   `SubmitAsync` returns `Accepted(id, JoinedExistingRun: true)`. No second run
   is created, no duplicate benchmark is established, and no duplicate work is
   queued (FR-010, spec edge case).
2. **Deliberate retry after failure** requires `RetryAsync`, the only permitted
   `Failed → Pending` transition. It reuses the same record and the same id, so
   the audit trail shows one run that failed and was retried, not two unrelated
   runs.
3. **Different options** produce a different id, so a genuine re-run with changed
   settings is a distinct, separately retrievable run (FR-012).

## Long-Running Runs and Interruption

FR-009 and SC-007 require a run to outlive the request that started it.

| Event | Required behavior |
|---|---|
| Run exceeds a normal interactive page action | `GetStatusAsync` returns `Pending`/`Running`; the host shows an explicit pending/progress state. A request timeout MUST NOT be presented as a validation failure. |
| User refreshes the page | `GetStatusAsync`/`GetResultAsync` on the same id return the run's real state. Nothing restarts. |
| User navigates away and returns | Same as refresh, subject to retention. |
| Connection drops mid-run | The run continues or ends in `Failed` with a diagnostic. It never lands in a clean state by default. |
| Process restarts mid-run | The record remains `Pending`/`Running` in the store. It MUST NOT be reported as clean or as completed. Recovery is a host operational concern; the record's honesty is not. |
| Retention window elapses | `Unavailable` with a reason — never an empty success and never a clean result (FR-032). |

Progress is user-facing status only. It never contributes to `WebRunId`, never
enters the substantive comparison surface, and never changes a computed value.

## Retrieval Semantics

| Call | `Pending`/`Running` | `CompletedClean`/`CompletedWithFindings` | `Failed` | Unknown/expired |
|---|---|---|---|---|
| `GetStatusAsync` | `Known(status)` | `Known(status)` | `Known(Failed)` | `Unavailable(reason)` |
| `GetResultAsync` | `NotReady(status)` | `Ready(view)` | `Ready(view)` with diagnostic only | `Unavailable(reason)` |
| `ExportAsync` | `NotAvailable(reason)` | `Written(representation)` | `NotAvailable(reason)` | `NotAvailable(reason)` |

- A `Failed` run's view carries the fatal diagnostic and **no** category counts,
  scores, or comparison evidence (FR-011, SC-003).
- Export is offered only for a terminal success, so an incomplete or fatal run is
  never downloadable as if it were a complete report (FR-014, spec US2 scenario 5).
- `Unavailable` always states a reason and offers a recovery path; user-entered
  context is preserved by the host where safe (FR-032).

## Audit Record

Each run persists a `WebRunRecord` containing inputs, resolved options, outcome,
and result reference, sufficient to explain what happened without reading
application internals (FR-026). Required fields and their invariants are defined
in [../data-model.md](../data-model.md).

Two record-level invariants are enforced at every transition:

1. `Diagnostic` is non-null **exactly** when `Status == Failed`.
2. `ResultReference` is non-null **only** for a terminal success.

Together these make "partial success" unrepresentable rather than merely
discouraged.

## Retention

Retention is host policy (research R5). The contract fixes only the observable
behavior:

- The interim development/test default is retain-until-deleted with no automatic
  expiry.
- A run that no longer exists is `Unavailable` with a reason.
- Deletion removes the record, its stored upload, and its result artifact
  together; a dangling result reference is a contract violation.
- The host's real retention window MUST be documented in `README.md` before public
  exposure (FR-034, Principle VIII).

## Concurrency

| Scenario | Required behavior |
|---|---|
| Two identical submissions race | Exactly one `TryCreateAsync` succeeds; the loser joins the existing run |
| Two benchmark establishments race on one name | Exactly one succeeds; the other fails deterministically with no partial benchmark directory |
| Status polled during a transition | The observed status is always one of the valid states, never a partial or blended state |
| Result read while a run is finishing | Either `NotReady` or the complete `Ready` view — never a partially populated view |
