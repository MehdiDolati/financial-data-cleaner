# Implementation Plan: OHLCV / Forex CSV Data-Quality Validator

**Branch**: `001-ohlcv-data-quality-validator` | **Date**: 2026-08-11 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-ohlcv-data-quality-validator/spec.md`

## Summary

Build a cross-platform .NET 10 CLI and reusable Application/Domain library that
ingests MT4-style or header-mapped OHLCV CSV data, normalizes valid candles to
UTC chronological order, applies six independently testable quality checks, and
emits deterministic text or JSON reports without changing the source file.

The implementation uses Clean Architecture, strict culture-invariant parsing,
a replayable external-merge-sort dataset for bounded-memory handling of
unsorted multi-million-row inputs, and streaming finding storage/reporting so
memory use is bounded by configured sort buffers rather than file or finding
count.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (`net10.0`)

**Primary Dependencies**: CsvHelper (strict CSV tokenization), System.CommandLine
(CLI binding/help), Microsoft.Extensions.DependencyInjection (composition root),
NodaTime (pinned TZDB for cross-platform `America/New_York` conversion), xUnit,
FluentAssertions, Coverlet, and ReportGenerator. Domain references the BCL only;
Application references Domain only.

**Storage**: No database or persistent application state. Input CSV, optional
calendar JSON, and report files are filesystem inputs/outputs. Bounded temporary
binary runs and finding spools are created through Application-owned storage
ports and deleted when the validation session is disposed or fails.

**Testing**: xUnit theory-driven Domain/Application unit tests; real-file
Infrastructure integration tests; built-executable CLI end-to-end tests;
Coverlet line and branch thresholds of 100% for Domain and Application only.

**Target Platform**: Windows, Linux, and macOS with the .NET 10 runtime; offline
execution with no network dependency.

**Project Type**: Reusable library plus command-line application, organized as
four Clean Architecture projects.

**Performance Goals**: Correctly process a few million M1 rows with no hard
latency target; sequential I/O and bounded sort chunks; memory bounded by an
implementation-configured chunk and merge fan-in rather than input size.

**Constraints**: Test-first development; 100% line/branch coverage for Domain
and Application; fixed-point `decimal` prices/volume; UTC internal timestamps;
strict UTF-8 and invariant parsing; deterministic output ordering; no source
mutation; environment access only through Application ports; exit codes limited
to `0` clean, `1` findings, and `2` fatal/usage failure.

**Scale/Scope**: One CSV per invocation, seven default MT4 columns (or six with
a combined timestamp), four market profiles, six summary categories, text and
JSON report formats, and files ranging from empty to several million rows.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

### Pre-Research Gate

| Principle | Result | Plan Evidence |
|---|---|---|
| I. Test-First | PASS | Tasks must place a failing unit, integration, or E2E test before each behavior implementation; red-green-refactor is part of the quickstart validation workflow. |
| II. Framework-Agnostic, Fully Covered Business Logic | PASS | Domain is BCL-only; Application references Domain only; CI applies 100% line/branch thresholds to both projects. Adapters and CLI are tested at integration/E2E level. |
| III. Clean Architecture | PASS | Four projects enforce inward references. Application owns all source, temp-storage, finding-store, report-writer, and time-zone ports. CLI command handlers use Application contracts; the composition root's only concrete-adapter knowledge is DI registration. |
| IV. Deterministic Results | PASS | Values use `decimal`, timestamps normalize to UTC, TZDB dependency is pinned, sorting has a source-line tie-breaker, ambiguous timeframe/delimiter detection fails, and finding/output order is canonical. |
| V. Fail Safe | PASS | Usage/configuration, unreadable/non-UTF-8/structurally invalid CSV, ambiguous delimiter, and ambiguous timeframe conditions produce actionable fatal results rather than inferred guesses. Row value failures alone become malformed-row findings. |
| VI. Observable and Auditable | PASS | JSON is a documented contract; every finding has a category and traceability fields; source metadata, range, timeframe, counts, and cleanliness are always available after successful ingestion. |
| VII. Simplicity with Cheap Extension Points | PASS | Only specified profiles/checks/formats are planned. `IValidationRule`, `IMarketCalendar`, source, finding-store, and report-writer ports isolate required variation without speculative features. |

No constitutional violations require a complexity exception.

### Post-Design Re-check

PASS. `data-model.md` keeps business values immutable and invariant; contracts
make the CLI, JSON, custom calendar, and reusable Application boundary explicit;
and `quickstart.md` validates all architectural and behavioral gates. The
external sort/finding spool adds Infrastructure complexity only where FR-007 and
NFR-020 jointly require bounded-memory handling of unsorted data. No new
constitutional violation was introduced by Phase 1.

## Project Structure

### Documentation (this feature)

```text
specs/001-ohlcv-data-quality-validator/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── application-api.md
│   ├── cli.md
│   ├── market-calendar.schema.json
│   └── validation-report.schema.json
└── tasks.md                         # Created by /speckit-tasks, not this command
```

### Source Code (repository root)

```text
FinancialDataCleaner.sln
Directory.Build.props
Directory.Packages.props
src/
├── Validator.Domain/
│   ├── Candles/
│   ├── Calendars/
│   ├── Findings/
│   └── Timeframes/
├── Validator.Application/
│   ├── Abstractions/
│   ├── Ingestion/
│   ├── Validation/
│   └── Reporting/
├── Validator.Infrastructure/
│   ├── Csv/
│   ├── Sorting/
│   ├── Calendars/
│   ├── Findings/
│   └── Reporting/
└── Validator.Cli/
    ├── Commands/
    └── Program.cs

tests/
├── Validator.Domain.Tests/
├── Validator.Application.Tests/
├── Validator.Infrastructure.Tests/
│   └── Fixtures/
└── Validator.Cli.Tests/
    └── Fixtures/
```

**Structure Decision**: Use the four-project layout required by NFR-001. Domain
contains immutable financial/calendar concepts and pure rules. Application owns
use-case orchestration, DTOs, and all ports. Infrastructure supplies CSV,
external-sort, time-zone, finding-spool, and report adapters. CLI contains
argument binding, process exit mapping, console interaction, and the DI
composition root. Tests mirror each production boundary; only Domain and
Application participate in the 100% coverage gate.

## Complexity Tracking

No constitution violations require justification.