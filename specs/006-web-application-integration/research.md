# Phase 0 Research: Web Application Integration

**Feature**: 006-web-application-integration | **Branch**: `007-web-application-integration`
**Date**: 2026-09-02 | **Plan**: [plan.md](./plan.md)

## Purpose

Resolve the unknowns in the plan's Technical Context. This feature is unusual:
the spec explicitly states the website "will be provided during planning or
implementation," so some unknowns are **host-supplied facts** rather than
decisions this repository is entitled to make. Each item below is therefore
classified:

- **RESOLVED** — decided here, on this repository's own authority.
- **DEFERRED (BLOCKING)** — host must answer; a safe interim default is recorded
  so design can proceed, and the item blocks a named class of tasks.

Resolving a deferred item requires only a plan/research amendment, not a
redesign, because every one of them sits behind a port rather than inside a rule.

---

## R1. Host website stack and where the web adapter lives

**Status**: DEFERRED (BLOCKING presentation-layer tasks)

**Decision**: Design the integration boundary as a plain .NET library surface in
`Validator.Application/Web/`, consumable two ways without redesign:

1. **In-process** — the host is .NET; it references `Validator.Application` +
   `Validator.Infrastructure` and calls `IValidationWebService` directly.
2. **Out-of-process** — the host is not .NET; a thin .NET HTTP adapter exposes
   the same façade over JSON, and the non-.NET front end consumes that.

Interim default for planning: assume path 1 with an ASP.NET Core adapter, because
the constitution's Technology Standards make .NET the default for every module
and feature 001's NFR-003 already names "an ASP.NET Core minimal API endpoint" as
the canonical alternate front end.

**Rationale**: The façade signature uses only `Stream`, `string`, and existing
Application records. That makes the in-process/out-of-process choice a wiring
decision at the composition root, not a contract change. Committing to a concrete
web framework now would violate Principle VII (no speculative building) and risk
building conventions that clash with the real site (FR-028).

**Alternatives considered**:
- *Scaffold `src/Validator.Web/` now with ASP.NET Core Razor Pages.* Rejected —
  the spec requires compliance with the host's established code style, naming,
  navigation, and interaction conventions (FR-028), which are unknown. Guessing
  produces work that must be thrown away.
- *Define the boundary as HTTP/OpenAPI only.* Rejected — forces a network hop and
  JSON round-trip even when the host is .NET, adding a serialization boundary
  that could silently degrade the `decimal` and UTC guarantees (FR-025).
- *Wait for the website before planning anything.* Rejected — the entire
  Application/Infrastructure boundary, its contracts, and the parity suite are
  host-independent and are the bulk of the work.

**Blocks**: any task that creates a web project, page, controller, view,
component, route, or style. Does **not** block façade, ports, lifecycle,
adapters, or parity tests.

---

## R2. How the website invokes business logic without duplicating rules

**Status**: RESOLVED

**Decision**: One Application-layer façade, `IValidationWebService`, with three
operations mirroring the three existing workflows — validate (with optional
scoring), establish benchmark, compare against benchmark. The façade **composes
existing use cases and adds no rule of its own**: it calls
`DetailedValidationOrchestrator` / `IDetailedValidationUseCase`,
`EstablishBenchmarkUseCase`, and `CompareDatasetsUseCase`, and returns the
existing `DetailedValidationOutcome`, `BenchmarkSnapshot`, and `ComparisonReport`
types wrapped in a run envelope.

**Rationale**: FR-021 demands explicit application-facing contracts; FR-024
forbids re-implementing validation, scoring, tolerance, or benchmark rules in
presentation code. A façade satisfies both while giving the website a single
call site instead of forcing it to replicate `ValidateCommand`'s ~1000 lines of
composition. Crucially, the façade is the *same shape* of composition the CLI
already performs, so parity is structural rather than aspirational.

**Alternatives considered**:
- *Let the website call the use cases directly.* Rejected — the website would
  have to reproduce source preparation, calendar construction, report-writer
  selection, benchmark rollback, and exit-code semantics. That is duplicated
  orchestration, and drift between the two front ends becomes inevitable.
- *Shell out to the CLI from the web server.* Rejected — process invocation with
  user-supplied paths is a safety hazard, exit codes lose the structured
  diagnostic, and it makes the web result depend on filesystem layout rather than
  on the contract.
- *Extract the CLI's composition into a shared helper the CLI also uses.*
  Attractive but rejected for now: refactoring `ValidateCommand` risks regressing
  established CLI behavior (FR-033) inside a feature whose promise is that the CLI
  is untouched. The façade may absorb CLI composition in a later feature once
  parity tests exist to protect it.

---

## R3. Long-running runs, progress, and duplicate submission

**Status**: RESOLVED for the boundary; DEFERRED (NON-BLOCKING) for the host's
background-work mechanism

**Decision**: Model a run as a persisted lifecycle record with an
`IWebRunQueue` port for handing work off. Ship exactly one implementation now —
`InlineWebRunQueue`, which executes the run and persists the terminal state —
and let the host substitute a real background host (hosted service, Hangfire,
queue worker) without touching Application code. Duplicate submission is
prevented by the deterministic `WebRunId`: resubmitting identical bytes with
identical resolved options resolves to the same run, so a refresh or double-click
joins the existing run instead of starting a second one. A deliberate retry after
a `Failed` run is allowed by explicitly transitioning the existing record back to
`Pending` (FR-010).

**Rationale**: FR-009 requires runs longer than a normal page action to expose a
pending/progress state and stay retrievable after refresh or disconnect; FR-008
requires explicit lifecycle states. Both are properties of the *record*, not of
any framework, so they belong in Application. The queue mechanism genuinely is a
host concern, so it is a port with a trivial default rather than a guess.

Progress is treated strictly as user-facing status. Per the spec's own
assumption, progress does not change the deterministic business result, so it is
excluded from the substantive comparison surface and from `WebRunId`.

**Alternatives considered**:
- *Server-Sent Events / WebSocket progress streaming.* Rejected as speculative
  (Principle VII) — polling a persisted status satisfies FR-009 and does not
  presume the host's transport capabilities.
- *Random GUID run identity with a separate idempotency key.* Rejected — a random
  ID makes SC-004's "deterministic and equivalent to prior runs" harder to assert
  and adds a second concept where a content hash already suffices.
- *In-memory run registry.* Rejected outright — fails refresh, disconnect, and
  restart (SC-007).

**Blocks**: nothing. `InlineWebRunQueue` is sufficient for every test scenario.

---

## R4. Where uploaded bytes, run records, and report artifacts live

**Status**: DEFERRED (BLOCKING production configuration only)

**Decision**: Two ports — `IUploadedDatasetStore` (retain and replay the exact
uploaded bytes) and `IWebRunStore` (persist and retrieve run records) — with
file-based implementations in `Infrastructure/Web/` following the pattern already
proven by `FileBenchmarkStore`. Uploaded bytes are stored **content-addressed by
SHA-256** and never rewritten. The existing `Validator.Infrastructure.Reporting`
writers produce the export artifact; nothing new is invented for export.

**Rationale**: Validation must read the *same stable bytes* it fingerprinted —
`IPreparedCandleSource.PrepareAsync` establishes identity and data in one pass
precisely so later passes cannot disagree. A durable, content-addressed upload
store preserves that guarantee across the request boundary and makes SC-008
(byte-for-byte unchanged source) trivially checkable. File-based first matches the
established repository pattern and needs no new dependency.

**Alternatives considered**:
- *Database-backed storage now.* Rejected — no host database is known, and it
  would add a dependency and a migration story for a feature whose storage need is
  "keep these bytes and this record for a while."
- *Re-upload on retrieval / don't retain the source.* Rejected — the detailed
  report references source lines and the comparison replays candles, so the bytes
  must remain available for the run's lifetime.
- *Store the rendered report only.* Rejected — FR-013/FR-014 require both the
  displayed view and the machine-readable export to carry the same substantive
  result; keeping the report contract as the single source avoids two truths.

**Blocks**: production storage configuration and deployment tasks. Does not block
implementation or tests, which use the file-based adapters.

---

## R5. Retention and deletion of uploads and results

**Status**: DEFERRED (BLOCKING release, not implementation)

**Decision**: Treat retention as host policy, expressed to the boundary as an
explicit, documented value rather than an implicit default. The run record
carries the facts needed to enforce any policy (creation time, terminal time,
source fingerprint, artifact reference); enforcement itself is a host-configured
concern. Interim default for development and tests: retain until explicitly
deleted, with no automatic expiry. A retrieval attempt for a run that no longer
exists returns an explicit "expired or unavailable" outcome — never an empty
success and never a clean result (FR-032).

**Rationale**: The spec's Assumptions require retention behavior to be
"documented during planning rather than silently assumed," and Out of Scope
forbids defining a retention platform the host has not supplied. Recording an
explicit interim default plus an explicit unavailable-state contract satisfies
both: nothing is silently assumed, and nothing is invented.

**Alternatives considered**:
- *Hard-code a 24-hour or 7-day expiry.* Rejected — invents host policy and would
  silently delete a result a user still needs.
- *Never retain (delete on read).* Rejected — breaks refresh/re-open (SC-007) and
  makes the export unavailable after first view.

**Blocks**: release/hardening tasks and the README's retention statement must
carry the host's real answer before public exposure.

---

## R6. Identity, authorization, tenancy, and concurrent benchmark conflicts

**Status**: DEFERRED (BLOCKING public exposure) for identity; RESOLVED for
benchmark concurrency

**Decision (identity)**: Introduce no user, role, or tenant model. Per the spec's
assumption, a deployment with no existing authorization model is treated as a
**trusted internal deployment**. The run record carries an opaque, optional
`SubmittedBy` correlation value the host may populate from its own identity
system; the boundary never interprets it and never uses it for authorization.

**Decision (benchmark concurrency)**: RESOLVED — benchmark immutability is
enforced deterministically at the store boundary. Establishment is an
atomic create-if-absent: two concurrent attempts on the same name produce one
success and one explicit conflict, never a silent replacement and never a
partially written benchmark. This extends the existing rule (a second benchmark
with the same name already fails) to the concurrent case, matching the CLI's
existing rollback behavior on failure.

**Rationale**: FR-023 and the spec's Out of Scope both forbid inventing an
identity or tenancy model. But the concurrency edge case ("two users or browser
sessions attempt conflicting benchmark operations at the same time; the website
must preserve benchmark immutability and report the conflict deterministically")
is a *business invariant*, not a host policy — it must be resolved here, and it
belongs at the store boundary where immutability already lives.

**Alternatives considered**:
- *Build a minimal user/role model.* Rejected — explicitly out of scope, and a
  conflicting account model is worse than none.
- *Last-write-wins on benchmark names.* Rejected — directly violates benchmark
  immutability and FR-006.
- *Optimistic concurrency with a version field.* Rejected — benchmarks are
  immutable, so there is no second version to reconcile; create-if-absent is the
  correct primitive.

**Blocks**: public/multi-tenant exposure requires host requirements before
release, as the spec already states.

---

## R7. Proving parity, accessibility, and safe rendering

**Status**: RESOLVED for parity and safe rendering; DEFERRED (NON-BLOCKING) for
the browser accessibility harness

**Decision (parity)**: Add a dedicated `Validator.Parity.Tests` project that runs
each fixture through both front ends and asserts equality of the **substantive
surface** — the six category counts, report status, finding evidence and order,
all six metric states/counts/populations/weights, the average and its coverage,
and every comparison distinction (matched/missing/extra/tolerated/material with
resolved tolerance). Presentation-only differences (labels, layout, ordering of
visual sections, progress) are explicitly outside the compared surface, per the
spec's own assumption that web labels may be arranged differently.

**Decision (safe rendering)**: Treat all data-derived text — file names, source
values, finding messages — as **data until a writer escapes it**, which is
already Application Invariant 5 from feature 002. The boundary never emits
pre-rendered markup; the view model carries typed values and the host escapes at
render time. Source identity keeps using the existing `SourceIdentity`, which
already rejects path components and never exposes an absolute path — that is the
upload-name safety guarantee (FR-030) reused rather than reinvented.

**Decision (accessibility)**: Keyboard-only completion and assistive-technology
availability (FR-031, SC-009) are host-surface properties and must be tested
against the real UI. The harness choice (Playwright, axe, or the host's existing
tooling) is deferred to the host's established conventions (FR-028).

**Rationale**: SC-001/SC-002/SC-004/SC-010 are all equality claims, and an
equality claim needs one authoritative comparison surface — otherwise every
future report change reopens the argument about what "parity" covers. Defining
that surface now is the single highest-value output of this research. Safe
rendering needs no new mechanism because the existing contract already forbids
Application from emitting escaped strings.

**Alternatives considered**:
- *Compare rendered CLI stdout to rendered HTML.* Rejected — compares
  presentation, not substance, and would fail on a harmless label change while
  passing on a real numeric divergence.
- *Compare only the JSON exports.* Close, and the JSON export is the primary
  vehicle — but insufficient alone, because SC-002 requires the *displayed* view
  to expose every required field without parsing prose. The view model is
  therefore compared too.
- *Skip accessibility testing until the site exists.* Partially adopted, but the
  criteria stay in scope and are named as host-suite obligations so they cannot be
  quietly dropped.

---

## Consolidated Findings

| ID | Item | Status | Interim default | Blocks |
|----|------|--------|-----------------|--------|
| R1 | Host stack / adapter location | DEFERRED | .NET in-process + ASP.NET Core adapter | Presentation tasks |
| R2 | Invocation without rule duplication | RESOLVED | Application façade over existing use cases | — |
| R3 | Long runs, progress, duplicates | RESOLVED | `InlineWebRunQueue`, deterministic run id | — |
| R4 | Upload / run / artifact storage | DEFERRED | Content-addressed file stores | Production config |
| R5 | Retention and deletion | DEFERRED | Retain until deleted; explicit unavailable state | Release + README statement |
| R6 | Identity / tenancy | DEFERRED | Trusted internal deployment, opaque correlation only | Public exposure |
| R6 | Benchmark concurrency | RESOLVED | Atomic create-if-absent, deterministic conflict | — |
| R7 | Parity + safe rendering | RESOLVED | Defined substantive comparison surface | — |
| R7 | Accessibility harness | DEFERRED | Host's established tooling | — |

## Effect on the Constitution Check

No deferred item requires a business rule to be guessed, duplicated, or weakened.
Every one sits behind a port or is a documented host policy, so the pre-Phase-0
gate stands and Phase 1 proceeds. The two `⚠️` entries in the plan's gate remain
exactly as recorded: Principle VII is watched (and the design honors it by adding
no speculative web scaffolding), and Principle VIII's README work is an
implementation obligation.

**Output**: All Technical Context unknowns are either resolved or explicitly
deferred with a safe default, a rationale, and a named blocking scope.
