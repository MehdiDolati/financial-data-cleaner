# Implementation Plan: Benchmark Dataset Comparison

**Branch**: `004-benchmark-dataset-comparison` | **Date**: 2026-08-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/004-benchmark-dataset-comparison/spec.md`

## Summary

This feature adds the ability to establish a validated dataset as an immutable named benchmark snapshot, then compare a candidate dataset against that benchmark to detect material OHLCV value discrepancies while tolerating acceptable broker-level differences. The comparison produces separate candidate quality scores, benchmark-agreement scores, and a full audit trail of tolerance decisions, discrepancies, and coverage statistics. The feature extends the existing validation and scoring pipeline without replacing or mutating either.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: System.Text.Json (existing), System.Numerics (existing for ExactRatio), no new external dependencies required

**Storage**: File-based persistence for benchmark snapshots (JSON + original source bytes stored alongside); no database

**Testing**: xunit (existing pattern across all test projects), FluentAssertions where already used

**Target Platform**: .NET 10 console application / CLI tool

**Project Type**: CLI tool (library-grade business logic with CLI front end)

**Performance Goals**: Deterministic and reproducible; no specific throughput target — correctness and auditability are the performance priorities

**Constraints**: All numeric comparison must use `decimal`; all timestamps must be UTC-normalized; culture-invariant formatting throughout; must remain fully testable and deterministic

**Scale/Scope**: Single-instrument benchmark comparison per run; datasets typically hundreds of thousands to low millions of OHLCV records; no concurrent access required

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Test-First | ✅ PASS | All new behavior will have failing tests before implementation |
| II. Business Logic Framework-Agnostic | ✅ PASS | Benchmark comparison logic lives in Domain/Application layers with no CLI, UI, or infrastructure dependencies |
| III. Hexagonal Architecture | ✅ PASS | Benchmark entities in Domain, comparison use case in Application, file-based persistence in Infrastructure, CLI wiring in Cli layer |
| IV. Deterministic, Reproducible | ✅ PASS | Deterministic discrepancy ordering, byte-identical reports with identical inputs, exact rational scoring carried through |
| V. Fail Safe, Never Fail Silent | ✅ PASS | Invalid configuration rejected before data read; incompatible datasets produce explicit diagnostics; no partial scores on failure |
| VI. Observable and Auditable | ✅ PASS | Every discrepancy carries timestamp, field, values, tolerance decision, and source references; aggregate counts of accepted differences are exposed |
| VII. Simplicity Now, Extension Points Where Cheap | ✅ PASS | No speculative multi-benchmark, marketplace, or auto-repair features; clean interface boundaries for future extension |
| VIII. Documentation Ships with the Feature | ⚠️ ACTION REQUIRED | README.md must be updated with new CLI options (`--benchmark`, `--compare`, `--tolerances`), new output sections, and usage examples |

**Overall Gate**: PASS — no constitution violations that require justification. README update (Principle VIII) is included in the task plan.

## Project Structure

### Documentation (this feature)

```text
specs/004-benchmark-dataset-comparison/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── benchmark-contract.md
│   └── comparison-report-contract.md
└── tasks.md             # Phase 2 output (/speckit-tasks command)
```

### Source Code (repository root)

```text
src/
├── Validator.Domain/
│   ├── Benchmarks/                    # NEW: Benchmark identity and snapshot records
│   ├── Comparison/                    # NEW: Comparison result records and field discrepancy types
│   ├── Candles/                       # Existing: PriceCandle
│   ├── Calendars/                     # Existing
│   ├── Findings/                      # Existing
│   ├── Scoring/                       # Existing: ExactRatio, ScoreValue
│   └── Timeframes/                    # Existing
│
├── Validator.Application/
│   ├── Abstractions/                  # Existing + NEW: benchmark and comparison use case interfaces
│   ├── Benchmark/                     # NEW: EstablishBenchmarkUseCase, BenchmarkStore interface
│   ├── Comparison/                    # NEW: CompareDatasetsUseCase, tolerance resolution, field comparator
│   ├── Ingestion/                     # Existing: SourceIdentity, ValidationContextSnapshot
│   ├── Reporting/                     # Existing + NEW: BenchmarkComparisonReport, comparison report writers
│   ├── Scoring/                       # Existing: DatasetScoreReport (reused for both datasets)
│   └── Validation/                    # Existing: ValidateMarketDataUseCase (unchanged)
│
├── Validator.Infrastructure/
│   ├── Benchmark/                     # NEW: File-based benchmark store
│   ├── Csv/                           # Existing
│   ├── Calendars/                     # Existing
│   ├── Findings/                      # Existing
│   ├── Reporting/                     # Existing + NEW: comparison report writers
│   └── Sorting/                       # Existing
│
└── Validator.Cli/
    ├── Commands/
    │   ├── ValidateCommand.cs         # Existing (extended with --benchmark / --compare options)
    │   └── BenchmarkCommand.cs        # NEW: benchmark establishment subcommand
    └── Program.cs                     # Existing (extended to route new subcommand)

tests/
├── Validator.Domain.Tests/
│   └── Comparison/                    # NEW: tolerance evaluation, field comparison unit tests
├── Validator.Application.Tests/
│   ├── Benchmark/                     # NEW: benchmark use case tests
│   └── Comparison/                    # NEW: comparison use case tests
├── Validator.Infrastructure.Tests/
│   └── Benchmark/                     # NEW: benchmark store tests
├── Validator.Cli.Tests/
│   └── Benchmark/                     # NEW: CLI command integration tests
└── Fixtures/                          # Existing + NEW: benchmark and candidate sample data
```

**Structure Decision**: The existing hexagonal layout (Domain → Application → Infrastructure → Cli) is extended with new `Benchmarks/` and `Comparison/` modules inside each appropriate layer. No new projects are created. The structure follows the established convention of feature-specific directories within each layer.

## Complexity Tracking

> No constitution violations that require justification. README update is standard per Principle VIII.
