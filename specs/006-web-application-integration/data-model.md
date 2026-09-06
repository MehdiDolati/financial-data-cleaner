# Phase 1 Data Model: Web Application Integration

**Feature**: 006-web-application-integration | **Plan**: [plan.md](./plan.md)
**Prerequisite**: [research.md](./research.md) complete

## Scope of This Model

This feature adds **no financial concept**. Every candle, finding, score,
tolerance, and benchmark entity already exists in `Validator.Domain` and
`Validator.Application` and is reused byte-for-byte. What is added here is the
*run envelope*: the entities that let a submitted operation become an auditable,
retrievable, deterministic run rather than a page request.

Consequently **no `Validator.Domain` type is added, changed, or removed.** All new
entities live in `Validator.Application/Web/`. This placement is the structural
evidence for FR-021 and FR-022.

### Reused Without Modification

| Existing entity | Layer | Role in this feature |
|---|---|---|
| `SourceIdentity` (FileName, ByteSize, Sha256) | Application.Ingestion | Safe source identity + content fingerprint for uploads; already forbids path components |
| `ValidationContextSnapshot` | Application.Ingestion | Resolved context echoed to the web result |
| `CsvInputOptions` | Application.Ingestion | All source-interpretation options exposed by the web form |
| `ValidationOptions` / `ScoreRequest` / `ScoreWeighting` | Application.Validation / .Scoring | Validation + scoring request options |
| `DetailedValidationReport` / `DetailedValidationOutcome` | Application.Reporting | The successful result; `Succeeded` / `Failed` is the authoritative outcome split |
| `FatalDiagnostic` | Application.Reporting | Every fatal web outcome; codes and stages reused unchanged |
| `DatasetScoreReport` / `MetricScore` / `MetricScoreState` | Application.Scoring | The six metrics, their states, populations, weights, average |
| `BenchmarkSnapshot` / `BenchmarkName` | Application.Benchmark | The immutable benchmark reference |
| `ComparisonReport` / `FieldComparisonResult` / `ComparedField` | Application.Comparison | Matched/missing/extra/tolerated/material distinctions and resolved tolerances |
| `ICompletedFindingCatalog` / `IDetailedFindingCursor` | Application.Abstractions | Streamed finding access; preserved so large reports stay bounded |
| `IApplicationClock` | Application.Abstractions | Audit timestamps only — never an input to a computed result |

---

## New Entities

### 1. `WebRunId`

Deterministic identity of one run.

| Field | Type | Rules |
|---|---|---|
| `Value` | `string` | Exactly 64 lower-case hex characters |

**Derivation** (normative):

```text
WebRunId = SHA-256( SourceIdentity.Sha256 ‖ 0x1F ‖ CanonicalOptionsString ) → lower-case hex
```

`CanonicalOptionsString` is the culture-invariant, field-ordered serialization of
every **resolved** option that materially affects a result: operation kind,
timeframe, market profile, calendar reference, timestamp interpretation
(format/column/offset), delimiter, header handling, report version, scoring
on/off, resolved weights, benchmark name, and resolved tolerance overrides.

**Validation rules**:
- Wall-clock time, sequence numbers, randomness, user identity, file upload name,
  and progress MUST NOT contribute. This is what makes SC-004 provable and
  satisfies Principle IV.
- Two submissions of identical bytes with equivalent resolved options MUST produce
  an equal `WebRunId`. This is the duplicate-submission guard for FR-010.
- A change to any material option MUST produce a different `WebRunId`, so a
  re-run with different options is a distinct run (FR-012).

---

### 2. `WebRunStatus`

The lifecycle state of a run. FR-008 requires all five states to exist and
requires that no non-terminal or failed state can read as clean.

| State | Meaning | Terminal |
|---|---|---|
| `Pending` | Accepted and persisted; work not started | No |
| `Running` | Work in progress | No |
| `CompletedClean` | Reconciled report exists; every category count is zero | Yes |
| `CompletedWithFindings` | Reconciled report exists; at least one category count is non-zero | Yes |
| `Failed` | No trustworthy report; a `FatalDiagnostic` explains why | Yes |

**Allowed transitions** (all others MUST be rejected):

```text
Pending  → Running | Failed
Running  → CompletedClean | CompletedWithFindings | Failed
Failed   → Pending                (deliberate user retry only, FR-010)
CompletedClean         → (none)
CompletedWithFindings  → (none)
```

**Validation rules**:
- `CompletedClean` is reachable **only** when a `DetailedValidationReport` exists
  with `Status == Clean`, i.e. when `DetailedSummary.IsClean` holds. The status may
  never be inferred from the absence of an error (FR-008, SC-003).
- `Failed → CompletedClean` and `Pending → CompletedClean` are forbidden. A
  pending, interrupted, or failed run therefore cannot be presented as clean —
  the core of SC-007.
- A completed state is immutable; a retry after success creates no new state on
  the existing record.
- `Failed → Pending` requires an explicit retry action, never an automatic one, so
  a refresh cannot silently restart work.

---

### 3. `WebRunRecord`

The audit aggregate for one run (FR-026). This is the persisted entity; it is
*not* the view model.

| Field | Type | Rules |
|---|---|---|
| `Id` | `WebRunId` | Required, immutable |
| `Operation` | `WebRunOperation` | `Validate` \| `EstablishBenchmark` \| `Compare` |
| `Status` | `WebRunStatus` | Transitions per the table above |
| `Source` | `SourceIdentity` | Required; safe base name + byte size + SHA-256 |
| `ResolvedOptions` | resolved option snapshot | The exact options applied, not the raw submission |
| `BenchmarkName` | `BenchmarkName?` | Required for `EstablishBenchmark` and `Compare`; null otherwise |
| `ResultReference` | `string?` | Reference to the stored result artifact; null until terminal-success |
| `Diagnostic` | `FatalDiagnostic?` | Non-null exactly when `Status == Failed` |
| `SubmittedAtUtc` | `DateTimeOffset` | From `IApplicationClock`; audit metadata only |
| `TerminalAtUtc` | `DateTimeOffset?` | Set once on reaching a terminal state |
| `SubmittedBy` | `string?` | Opaque host correlation value; never interpreted, never used for authorization (research R6) |

**Validation rules**:
- `Diagnostic` non-null ⇔ `Status == Failed`. A record may never carry both a
  fatal diagnostic and a completed report reference (FR-011).
- `ResultReference` non-null ⇒ `Status ∈ {CompletedClean, CompletedWithFindings}`.
  Partial counts, partial scores, and partial comparison evidence are never
  reachable through a record (FR-011, SC-003).
- Timestamps are UTC-normalized and are **audit metadata**: they MUST NOT appear
  in the substantive comparison surface, so their presence cannot break
  determinism (Principle IV).
- The record retains enough identity and context for a user to distinguish
  separate runs without consulting application internals (FR-012, FR-026).
- Retention/expiry is host policy; a record's absence is reported as an explicit
  unavailable outcome, never as an empty success (research R5, FR-032).

---

### 4. `UploadedDataset`

The retained user source content.

| Field | Type | Rules |
|---|---|---|
| `Identity` | `SourceIdentity` | Safe base name, byte size, SHA-256 |
| `ContentReference` | `string` | Content-addressed locator derived from the SHA-256 |

**Validation rules**:
- Stored bytes are **write-once**. No validation, scoring, reporting, comparison,
  or export path may modify, repair, reorder, or overwrite them (FR-006, SC-008).
- The bytes used for validation MUST be the same bytes that produced
  `Identity.Sha256`; the store replays them rather than re-reading a mutable
  upload location. This preserves the one-pass identity guarantee that
  `IPreparedCandleSource.PrepareAsync` already provides.
- The display name is always the safe base name from `SourceIdentity`; absolute
  paths and path components are impossible by construction (FR-030).
- Empty, header-only, oversized, and unsupported-encoding content are rejected as
  `Failed` runs with the existing diagnostic codes — never as clean runs.

---

### 5. `WebResultView`

The typed, presentation-free projection of a terminal run. It is the answer to
FR-019 and SC-002: every required field is reachable as data, never only as prose.

| Section | Source | Present when |
|---|---|---|
| Run identity + status | `WebRunRecord` | Always |
| Source identity | `SourceIdentity` | Always |
| Resolved context | `ValidationContextSnapshot` | Terminal success |
| Scan coverage | `ScanCoverage` | Terminal success |
| Check execution | `IReadOnlyList<CheckExecution>` | Terminal success |
| Reconciliation | `ReportReconciliation` | Terminal success |
| Six category summaries | `DetailedSummary` | Terminal success |
| Findings (streamed) | `ICompletedFindingCatalog` | Terminal success |
| Scoring section | `DatasetScoreReport` | Scoring requested |
| Benchmark reference | `BenchmarkSnapshot` identity + recorded scores | Compare / establish |
| Comparison evidence | `ComparisonReport` | Compare |
| Fatal diagnostic | `FatalDiagnostic` | `Status == Failed` |
| Export availability | derived | Terminal success only |

**Validation rules**:
- The view carries **typed values only**. It MUST NOT contain pre-rendered markup
  or escaped strings; escaping happens in the host at render time (FR-030,
  reusing feature 002's Application Invariant 5).
- Findings are exposed through the streaming catalog, not a materialized list, so
  a very large finding count stays navigable and is never silently truncated.
- A candidate's independent quality score and its benchmark-agreement score are
  **separate members**; the benchmark's recorded scores are a third, separate
  member (FR-016).
- Not-applicable, not-scored, unavailable, and insufficient-coverage states are
  carried as explicit states with their reasons. They MUST NOT be replaced by a
  perfect score, a zero, or an inferred value (FR-018).
- Numeric and date/time values are culture-invariant and UTC-normalized in the
  view. Display localization or user-time-zone rendering is a host concern and
  MUST NOT change any computed value (FR-025).
- When `Status` is non-terminal or `Failed`, the view exposes no category counts,
  no scores, and no export action (FR-008, FR-011).

---

## New Ports (Application-declared, Infrastructure-implemented)

Dependency direction is inward only; each port is owned by Application, satisfying
Principle III and FR-023.

| Port | Responsibility | Key invariant |
|---|---|---|
| `IWebRunStore` | Persist and retrieve `WebRunRecord` by `WebRunId`; apply guarded status transitions | A rejected transition MUST fail rather than silently coerce the state |
| `IUploadedDatasetStore` | Retain uploaded bytes content-addressed; replay them for a run | Write-once; replayed bytes are byte-identical to those hashed |
| `IWebRunQueue` | Hand an accepted run to background execution | Accepting a run MUST have already persisted it as `Pending`, so a crash cannot lose it |

`IBenchmarkStore` is **not** replaced. Its establishment path gains an explicit
atomic create-if-absent contract so two concurrent web attempts on one name yield
one success and one deterministic conflict (research R6, spec edge case).

---

## Entity Relationships

```text
UploadedDataset ──1:1── SourceIdentity
       │
       │ (content-addressed, write-once)
       ▼
   WebRunRecord ──1:1── WebRunId ── derived from ── SourceIdentity.Sha256
       │                                        └── CanonicalOptionsString
       │ 1:1 (terminal success)
       ▼
   WebResultView ──▶ DetailedValidationReport ──▶ ICompletedFindingCatalog (streamed)
       │                     └──▶ DatasetScoreReport (optional)
       │
       ├──▶ ComparisonReport      (Compare only)
       ├──▶ BenchmarkSnapshot     (Compare / EstablishBenchmark)
       └──▶ FatalDiagnostic       (Failed only, mutually exclusive with the above)
```

One `UploadedDataset` may back many `WebRunRecord`s (same bytes, different
options). One `WebRunRecord` yields at most one `WebResultView`. A `WebResultView`
carries either a report-bearing result **or** a fatal diagnostic — never both.

---

## Cross-Cutting Invariants

1. **No rule duplication.** Every count, score, tolerance decision, and comparison
   distinction originates in Domain/Application. The web entities carry and
   reference results; they never recompute them (FR-024).
2. **Determinism.** Identical bytes + equivalent resolved options ⇒ identical
   `WebRunId`, identical substantive result, identical finding order (SC-001,
   SC-004).
3. **Source immutability.** Uploaded bytes and established benchmarks are
   byte-for-byte unchanged by every path including export and comparison (SC-008).
4. **Fail-safe status.** No state, projection, or default renders a pending,
   interrupted, or failed run as clean (FR-008, SC-003, SC-007).
5. **Auditability.** Every run persists inputs, resolved options, outcome, and
   result reference independently of any view (FR-026).
6. **Culture and time invariance.** Computation is invariant and UTC; presentation
   choices never alter a value (FR-025).
7. **CLI non-regression.** No existing entity is modified, so existing non-web
   callers keep their behavior and contracts (FR-022, FR-033, SC-010).
