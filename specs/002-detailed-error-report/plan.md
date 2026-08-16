# Implementation Plan: Detailed Dataset Error Report

**Branch**: `002-detailed-error-report` | **Date**: 2026-08-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/002-detailed-error-report/spec.md`

## Summary

Extend the existing .NET 10 validator with a complete, actionable report for
every successful scan and a trustworthy fatal diagnostic for incomplete scans.
Verbose text and an explicitly selected JSON v2 contract expose source identity,
resolved validation context, check coverage, reconciled category totals, typed
evidence, deterministic finding references, and remediation guidance. Existing
concise text and unversioned JSON v1 remain compatible.

The implementation replaces report-sized `List<T>` and `string` materialization
with replayable, normalized temporary spools for findings, child evidence, and
relationships. Application computes deterministic report data and verifies
reconciliation before rendering; Infrastructure stages and streams reports so
memory is bounded by configured buffers rather than input or finding count.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (`net10.0`)

**Primary Dependencies**: Existing CsvHelper, NodaTime, xUnit,
FluentAssertions, and Coverlet dependencies; `System.Security.Cryptography`
and `System.Text.Json` from the BCL for SHA-256 identity and streaming JSON. No
new production package is required.

**Storage**: No database. Source CSV and optional calendar files remain inputs.
Bounded temporary binary/JSONL runs store sorted candles, findings, child
evidence, relationships, and staged report output. All temporary artifacts are
owned through Application ports and deleted on success, fatal failure, or
cancellation.

**Testing**: Test-first xUnit theories for Domain/Application models, IDs,
reconciliation, fatal-state transitions, and evidence rules; Infrastructure
integration tests for spools, source fingerprints, escaping, schemas, and atomic
report writes; process-level CLI tests for compatibility, streams, exit codes,
and 100,000-finding bounded-memory output. Domain and Application retain 100%
line and branch coverage.

**Target Platform**: Windows, Linux, and macOS with the .NET 10 runtime; offline
execution with no network dependency.

**Project Type**: Existing reusable Clean Architecture library plus CLI.

**Performance Goals**: Emit at least 100,000 complete detailed findings without
truncation and without memory increasing with total input rows, findings,
duplicate-group size, or gap size. No hard latency target; sequential I/O and
bounded external-sort buffers are preferred.

**Constraints**: Preserve JSON v1 shape and existing six category meanings;
require explicit v2 opt-in; deterministic output has no wall-clock fields;
fixed-point `decimal` evidence and UTC timestamps; source line/count types are
64-bit; source files are never modified; fatal runs emit no successful report;
v2 fatal JSON is exactly one stderr document with empty stdout/destination;
environment access remains behind Application-owned ports.

**Scale/Scope**: One dataset per run, six established checks and categories,
text summary/verbose text/JSON v1/JSON v2, millions of source records, at least
100,000 findings, arbitrarily large duplicate groups and missing-candle gaps,
and one fatal diagnostic per incomplete run.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

### Pre-Research Gate

| Principle | Result | Plan Evidence |
|---|---|---|
| I. Test-First | PASS after task-graph remediation | The revised tasks place a failing unit, integration, contract, or E2E test immediately before every behavior implementation; interface-only ports and compile-only composition stubs are explicitly separated from behavior. |
| II. Framework-Agnostic, Fully Covered Business Logic | PASS | Detailed report/fatal models, evidence construction, check status, IDs, and reconciliation live in Domain/Application with no serializer or console dependency and remain under the 100% gate. |
| III. Clean Architecture | PASS | Application owns source-identity, replayable finding/evidence, report-audit, writer, and atomic-destination contracts. Infrastructure implements filesystem, hashing, spool, and rendering adapters; CLI only binds options and routes streams. |
| IV. Deterministic Results | PASS | SHA-256 identifies source bytes; IDs derive from canonical finding keys; output uses category/timestamp/line/tie-break order; source values are invariantly escaped; no generated-at time or random public identifier is emitted. |
| V. Fail Safe | PASS | Reconciliation or rendering failure prevents report commit. Fatal outcomes identify class, stage, code, guidance, and unfinished checks; v2 fatal output cannot be mistaken for a successful report. |
| VI. Observable and Auditable | PASS | V2 exposes run context, scan coverage, check execution, per-category count and entry count, typed evidence, relationships, and source traceability as documented fields. |
| VII. Simplicity with Cheap Extension Points | PASS | The feature extends the existing report/finding abstractions and six checks. Normalized spools are introduced only to meet the explicit completeness and bounded-memory requirements. |

No constitutional violations require a complexity exception.

### Post-Design Re-check

PASS after task-graph remediation. `data-model.md` makes completion,
reconciliation, source identity, evidence, and fatal state explicit. The revised
tasks add test-first ownership for the six-check finding pipeline, report/context
aggregates, verbose/v1 compatibility, operational failure classes, source alias
protection, and the 100,000-finding bounded-memory acceptance path. The
application/CLI and JSON contracts preserve v1 while defining v2 without
serializer leakage into business logic. `quickstart.md` validates compatibility,
completeness, deterministic replay, source protection, atomic completion, fatal
stream routing, and bounded memory. No constitutional violation remains after
the revised dependency order is applied.

## Project Structure

### Documentation (this feature)

```text
specs/002-detailed-error-report/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── application-api.md
│   ├── cli.md
│   ├── detailed-report-v2.schema.json
│   └── fatal-diagnostic-v2.schema.json
└── tasks.md                         # Created by /speckit-tasks, not this command
```

### Source Code (repository root)

```text
src/
├── Validator.Domain/
│   └── Findings/                    # Typed finding detail, evidence, IDs, relationships
├── Validator.Application/
│   ├── Abstractions/                # Source identity, spool, report writer/destination ports
│   ├── Ingestion/                   # Resolved input context and scan coverage
│   ├── Reporting/                   # V2 report/fatal models, check status, reconciliation
│   └── Validation/                  # Streaming orchestration and enriched six-rule output
├── Validator.Infrastructure/
│   ├── Csv/                         # Hashing, row statistics, original values, resolved context
│   ├── Findings/                    # Canonical normalized finding/evidence/relationship spools
│   ├── Reporting/                   # Concise/verbose text, JSON v1/v2, fatal and atomic staging
│   └── Sorting/                     # Bounded external sorting and temporary storage
└── Validator.Cli/
    └── Commands/                    # Version option, composition, stdout/stderr/destination routing

tests/
├── Validator.Domain.Tests/          # Evidence and invariant theories
├── Validator.Application.Tests/     # IDs, statuses, reconciliation, fatal orchestration
├── Validator.Infrastructure.Tests/  # Hash/spool/writer/schema/atomic integration tests
├── Validator.Cli.Tests/             # Compatibility and process-level stream/exit tests
└── Fixtures/                        # Cross-category, hostile text, fatal and large generated data
```

**Structure Decision**: Retain the four existing Clean Architecture projects.
Feature 002 is a reporting/provenance extension to feature 001, not a separate
module. Domain models category-specific evidence; Application assembles and
audits complete outcomes through replayable ports; Infrastructure owns source
bytes, temporary storage, schemas, encoding, and rendering; CLI selects a
representation and commits it to the requested process/file destination.

## Complexity Tracking

No constitution violations require justification.
