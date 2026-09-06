# Implementation Plan: Web Application Integration

**Branch**: `007-web-application-integration` | **Date**: 2026-09-02 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/006-web-application-integration/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Expose the established validation, detailed-reporting, scoring, and
benchmark-comparison business logic through a web front end with substantive
parity to the existing CLI, without moving a single business rule into
presentation code. The technical approach introduces one new **application-facing
service façade** (`Validator.Application/Web/`) that accepts a transport-neutral
request, drives the existing `IDetailedValidationUseCase`,
`EstablishBenchmarkUseCase`, and `CompareDatasetsUseCase`, and returns a typed
outcome; plus one **run-lifecycle port set** (submit / poll / retrieve /
export) implemented in Infrastructure so a long-running upload becomes a
retrievable run instead of a page request. The host website (to be supplied)
becomes a thin adapter over that façade: it owns HTTP, upload limits, session,
retention, rendering, and accessibility, and owns none of the rules.

The design deliberately avoids a `Validator.Web` project in this plan. The host
website's stack is not yet known, so the plan's deliverable in *this* repository
is the transport-neutral integration boundary plus its contracts and parity
tests. Web-hosting specifics are captured as explicit `NEEDS CLARIFICATION`
items that must be resolved (via `/speckit-clarify` or a plan amendment) before
`/speckit-tasks` can schedule any presentation-layer work.

## Technical Context

**Language/Version**: C# / .NET 10 for Domain, Application, Infrastructure, and
the new integration boundary. Host website language/runtime is
**NEEDS CLARIFICATION** (see research.md R1) — if it is not .NET, the boundary is
consumed over an HTTP/JSON contract instead of an assembly reference.

**Primary Dependencies**: Existing only — `System.Text.Json`, `CsvHelper`,
`NodaTime`, `System.CommandLine` (CLI-only). No new dependency is added by the
integration boundary itself. Web-host dependencies (framework, DI container,
background-work host, object storage client) are
**NEEDS CLARIFICATION** (research.md R1, R4).

**Storage**: Run records, uploaded dataset bytes, and completed report artifacts
require durable storage beyond one request (FR-009, FR-012, SC-007). The plan
defines `IWebRunStore` + `IUploadedDatasetStore` ports; the concrete backing
(filesystem vs. object storage vs. database) and the retention window are
**NEEDS CLARIFICATION** (research.md R4, R5). Benchmarks keep their existing
file-based `IBenchmarkStore`, now guarded by an explicit concurrency contract.

**Testing**: xunit + FluentAssertions, matching every existing test project.
New test surfaces: `tests/Validator.Application.Tests/Web/` (façade unit +
lifecycle state machine), `tests/Validator.Infrastructure.Tests/Web/` (run store,
upload store, concurrency), and a new **CLI↔Web parity suite** that runs the same
fixture through both front ends and asserts substantive equality (SC-001,
SC-004, SC-010). Browser-level accessibility and refresh/disconnect tests
(SC-007, SC-009) belong to the host website's own suite — the harness is
**NEEDS CLARIFICATION** (research.md R7).

**Target Platform**: Cross-platform .NET 10 for the boundary. Host deployment
target, reverse proxy, and request-timeout budget are **NEEDS CLARIFICATION**
(research.md R1, R3).

**Project Type**: Library-grade business logic with two front ends — the existing
CLI and a new web presentation adapter. The boundary added here is a library, not
a web service.

**Performance Goals**: No throughput target. Determinism, auditability, and
bounded memory are the priorities, unchanged from features 001–004. The
integration MUST preserve the existing streaming/spooling behavior so a large
upload does not materialize findings in memory (edge case: very large finding
counts).

**Constraints**: `decimal` for every price/volume value; UTC-normalized
timestamps; culture-invariant parse/format on both the compute and the display
path (FR-025); byte-for-byte source and benchmark immutability (FR-006, SC-008);
no business rule in presentation code (FR-021, FR-024); Domain/Application must
compile and pass unchanged for CLI callers (FR-022, FR-033).

**Scale/Scope**: Single-dataset, single-instrument runs, same as the CLI.
Datasets from hundreds of thousands to low millions of OHLCV records. Concurrent
users and per-user isolation are **NEEDS CLARIFICATION** (research.md R6); the
spec's stated fallback is a trusted internal deployment.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase-0 Gate

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Test-First | ✅ PASS | Every new boundary type, lifecycle transition, and parity assertion gets a failing test first (FR-027). |
| II. Business Logic Framework-Agnostic + 100% Covered | ✅ PASS | The façade and lifecycle state machine live in Application with zero web/transport types; they are inside the merged Domain/Application 100% gate. The web adapter is thin wiring, exempt from the line gate but required to carry integration/E2E coverage. |
| III. Hexagonal Architecture | ✅ PASS | New ports (`IWebRunStore`, `IUploadedDatasetStore`, `IWebRunQueue`) are declared in Application and implemented in Infrastructure; the website depends inward only. |
| IV. Deterministic, Reproducible | ✅ PASS | Run identity is derived from source SHA-256 + resolved options, not wall clock or GUID, so SC-004 is provable. Lifecycle timestamps are audit metadata supplied via `IApplicationClock` and excluded from the substantive result. |
| V. Fail Safe, Never Fail Silent | ✅ PASS | Options are validated before any byte is read (FR-007); a pending or failed run can never render as clean (FR-008); fatal outcomes reuse the existing `FatalDiagnostic` registry with no partial counts. |
| VI. Observable and Auditable | ✅ PASS | Every run persists a `WebRunRecord` with inputs, resolved options, outcome, and result reference (FR-026), separate from any page rendering. |
| VII. Simplicity Now | ⚠️ WATCH | Risk of speculative web scaffolding (roles, tenancy, queues, dashboards) before the host supplies requirements. Mitigation: this plan ships the boundary + contracts only; anything host-specific stays a NEEDS CLARIFICATION item rather than a guess. |
| VIII. Documentation Ships with the Feature | ⚠️ ACTION REQUIRED | `README.md` MUST gain a "Web Application Integration" section covering the supported web workflow, parity boundary, report/export access, retention/configuration expectations, run/build instructions, and — per FR-034 — the location of authoritative web guidance if the site lives in another repository. Affected sections: intro paragraph, **Build**, new **Web Application** section, **Architecture**. |

**Overall Gate**: ⚠️ **CONDITIONAL PASS.** No principle is violated and nothing
requires justification in Complexity Tracking. Two conditions attach:

1. **Principle VIII** is an action item, tracked into `/speckit-tasks` as
   required README work before final validation.
2. Seven `NEEDS CLARIFICATION` items (research.md R1–R7) are **host-supplied
   unknowns, not unresolved design decisions.** Phase 0 resolves every one that
   this repository can decide on its own and records the rest as explicitly
   deferred with a safe default and a blocking marker. `/speckit-tasks` MAY
   schedule all Application/Infrastructure boundary work immediately; it MUST
   NOT schedule presentation-layer tasks until R1 is answered.

### Post-Phase-1 Re-Check

| Principle | Status | Post-design evidence |
|-----------|--------|----------------------|
| I. Test-First | ✅ PASS | [quickstart.md](./quickstart.md) orders every scenario red-before-green; the parity suite is written before the façade exists. |
| II. Business Logic Framework-Agnostic | ✅ PASS | [contracts/web-integration-contract.md](./contracts/web-integration-contract.md) shows the façade signature uses only `Stream`, `string`, and existing Application records — no `HttpContext`, no `IFormFile`, no session type. |
| III. Hexagonal Architecture | ✅ PASS | [data-model.md](./data-model.md) places all four new entities in Application and all three new adapters in Infrastructure; no inward reference is introduced. |
| IV. Deterministic, Reproducible | ✅ PASS | `WebRunId` = SHA-256 of (source fingerprint ‖ canonical resolved-options string); documented in data-model.md and asserted by quickstart scenario 4. |
| V. Fail Safe, Never Fail Silent | ✅ PASS | `WebRunStatus` has no state that reads as clean without a completed reconciled report; the transition table forbids `Failed → CompletedClean`. |
| VI. Observable and Auditable | ✅ PASS | `WebRunRecord` is the audit aggregate; contracts define its required fields and its independence from the view model. |
| VII. Simplicity Now | ✅ PASS | Design adds four Application records, three ports, one façade. No queue implementation, no role model, no tenancy, no charting. Deferred items stayed deferred. |
| VIII. Documentation Ships | ⚠️ ACTION REQUIRED (carried) | Still an implementation-phase obligation; scheduled, not yet done. Correctly remains open at the end of `/speckit-plan`. |

**Post-design gate**: PASS with the README obligation carried forward and the
host-dependent items still explicitly blocked rather than silently assumed.

## Project Structure

### Documentation (this feature)

```text
specs/006-web-application-integration/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── web-integration-contract.md   # The façade the website calls
│   ├── web-run-lifecycle.md          # Run states, transitions, retrieval
│   └── web-result-view-contract.md   # Machine-readable view/export shape
├── checklists/
│   └── requirements.md  # Existing spec-quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── Validator.Domain/                  # UNCHANGED — no Domain change required
│
├── Validator.Application/
│   ├── Abstractions/
│   │   ├── IApplicationClock.cs       # Existing — reused for audit timestamps
│   │   ├── IWebRunStore.cs            # NEW port: persist/retrieve run records
│   │   ├── IUploadedDatasetStore.cs   # NEW port: retain + replay source bytes
│   │   └── IWebRunQueue.cs            # NEW port: hand a run to background work
│   ├── Web/                           # NEW — the integration boundary
│   │   ├── IValidationWebService.cs   # Façade the website calls
│   │   ├── ValidationWebService.cs    # Drives existing use cases only
│   │   ├── WebRunRequest.cs           # Transport-neutral request + option DTO
│   │   ├── WebRunOptionsValidator.cs  # Pre-read option validation (FR-007)
│   │   ├── WebRunRecord.cs            # Audit aggregate (FR-026)
│   │   ├── WebRunId.cs                # Deterministic run identity
│   │   ├── WebRunStatus.cs            # Lifecycle states + transition guard
│   │   └── WebResultView.cs           # Typed, presentation-free result view
│   ├── Benchmark/                     # Existing — reused unchanged
│   ├── Comparison/                    # Existing — reused unchanged
│   ├── Reporting/                     # Existing — reused unchanged
│   ├── Scoring/                       # Existing — reused unchanged
│   └── Validation/                    # Existing — reused unchanged
│
├── Validator.Infrastructure/
│   ├── Web/                           # NEW adapters for the new ports
│   │   ├── FileWebRunStore.cs
│   │   ├── FileUploadedDatasetStore.cs
│   │   └── InlineWebRunQueue.cs       # Simplest safe default; see research R3
│   ├── Benchmark/                     # Existing + concurrency guard (FR/edge)
│   ├── Csv/                           # Existing — CsvCandleSource reused
│   └── Reporting/                     # Existing writers reused for export
│
└── Validator.Cli/                     # UNCHANGED — proves FR-022 / FR-033

tests/
├── Validator.Application.Tests/Web/   # NEW: façade, options, lifecycle, identity
├── Validator.Infrastructure.Tests/Web/# NEW: run store, upload store, concurrency
├── Validator.Cli.Tests/               # UNCHANGED, must keep passing (FR-033)
├── Validator.Parity.Tests/            # NEW project: CLI↔Web substantive parity
└── Fixtures/                          # Existing fixtures reused for parity
```

**Structure Decision**: Extend the established hexagonal layout rather than
introduce a web project now. A new `Web/` folder in Application holds the
transport-neutral façade and lifecycle model; three new ports in
`Abstractions/` are implemented under a new `Infrastructure/Web/` folder. One new
test project, `Validator.Parity.Tests`, is added because CLI↔Web equivalence
(SC-001, SC-004, SC-010) is the feature's central claim and cannot live inside
either front end's own suite without depending on the other. `Validator.Domain`
and `Validator.Cli` are untouched, which is itself the evidence for FR-022 and
FR-033. The host website's project — whether `src/Validator.Web/` in this
repository or an adapter in the website's own repository — is intentionally left
unspecified until research item R1 is answered.

## Complexity Tracking

> No constitution violation requires justification. The two entries below are
> recorded because they add structure beyond the minimum and should be revisited
> if the host website makes them redundant.

| Addition | Why Needed | Simpler Alternative Rejected Because |
|----------|------------|--------------------------------------|
| New `Validator.Parity.Tests` project | SC-001/SC-004/SC-010 require asserting that CLI and web outputs are substantively identical; the assertion must reference both front ends. | Putting parity tests in `Validator.Cli.Tests` would make the CLI suite depend on the web boundary, coupling the front end this feature must prove is independent. |
| Run-lifecycle ports (`IWebRunStore`, `IUploadedDatasetStore`, `IWebRunQueue`) | FR-009/FR-012 and SC-007 require a completed run to survive refresh, disconnect, and a duration longer than one request — impossible with request-scoped state. | Running validation synchronously inside the request and holding results in memory fails the refresh/disconnect and long-run criteria outright, and would report a timeout as a validation failure. |
| README "Web Application Integration" section | Principle VIII + FR-034. | None; documentation is mandatory, not optional. |
