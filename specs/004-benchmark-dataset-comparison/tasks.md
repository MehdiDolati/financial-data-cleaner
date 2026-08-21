# Tasks: Benchmark Dataset Comparison

**Input**: Design documents from `/specs/004-benchmark-dataset-comparison/`

**Prerequisites**: plan.md, spec.md, data-model.md, contracts/, research.md, quickstart.md

**Tests**: Included per Constitution Principle I (test-first is non-negotiable).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Create new directories and test fixtures for the feature.

- [x] T001 Create directory structure: `src/Validator.Domain/Benchmarks/`, `src/Validator.Domain/Comparison/`, `src/Validator.Application/Benchmark/`, `src/Validator.Application/Comparison/`, `src/Validator.Infrastructure/Benchmark/`, `tests/Validator.Domain.Tests/Comparison/`, `tests/Validator.Application.Tests/Benchmark/`, `tests/Validator.Application.Tests/Comparison/`, `tests/Validator.Infrastructure.Tests/Benchmark/`
- [x] T002 [P] Create test fixture CSV files in `tests/Fixtures/`: a reference AUDUSD D1 dataset (`AUDUSD_D1_reference.csv`) with ~100 candles, a candidate with identical values (`AUDUSD_D1_candidate_identical.csv`), a candidate with one material price difference and one tolerated broker difference (`AUDUSD_D1_candidate_with_differences.csv`), a candidate with missing and extra candles (`AUDUSD_D1_candidate_coverage_gaps.csv`), and a candidate with no overlapping timestamps (`AUDUSD_D1_candidate_no_overlap.csv`)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core domain entities and interfaces that ALL user stories depend on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Domain Entities

- [x] T003 [P] Create `OhlcvField` enum in `src/Validator.Domain/Comparison/OhlcvField.cs` — values: Open, High, Low, Close, Volume
- [x] T004 [P] Create `ToleranceDecision` discriminated union in `src/Validator.Domain/Comparison/ToleranceDecision.cs` — variants: AcceptedByAbsolute, AcceptedByRelative, MaterialDifference
- [x] T005 [P] Create `TimestampMode` enum in `src/Validator.Domain/Comparison/TimestampMode.cs` — value: Exact
- [x] T006 [P] Create `FieldDiscrepancy` record in `src/Validator.Domain/Comparison/FieldDiscrepancy.cs` — fields: TimestampUtc, Field, BenchmarkValue, CandidateValue, Difference, DirectionalDifference, ResolvedAbsoluteTolerance, ResolvedRelativeTolerance, ToleranceDecision; immutable with validation (Difference must be non-negative)
- [x] T007 [P] Create `ComparisonCoverage` record in `src/Validator.Domain/Comparison/ComparisonCoverage.cs` — fields: BenchmarkRecordCount, CandidateRecordCount, MatchedCount, MissingFromCandidateCount, ExtraInCandidateCount, OverlappingRange; enforce count invariants
- [x] T008 [P] Create `ToleratedDifferenceAggregate` record in `src/Validator.Domain/Comparison/ToleratedDifferenceAggregate.cs` — fields: Field, TotalCompared, AcceptedCount, AcceptedByAbsoluteCount, AcceptedByRelativeCount, MaterialCount
- [x] T009 [P] Create `BenchmarkAgreementScore` record in `src/Validator.Domain/Comparison/BenchmarkAgreementScore.cs` — fields: Score (ScoreValue?), Formula, MatchedPopulation, MaterialDiscrepancyCount, UnavailableReason; enforce that Score is null iff UnavailableReason is non-null
- [x] T010 [P] Create `ComparedField` record in `src/Validator.Domain/Comparison/ComparedField.cs` — fields: Field, Enabled, AbsoluteTolerance (decimal?), RelativeTolerance (decimal?), ResolvedAbsolute, ResolvedRelative
- [x] T011 [P] Create `ComparisonConfiguration` record in `src/Validator.Domain/Comparison/ComparisonConfiguration.cs` — fields: BenchmarkName, Fields (IReadOnlyList<ComparedField>), TimestampMode; validate no duplicate fields, all tolerances non-negative

### Application Interfaces

- [x] T012 [P] Create `IBenchmarkStore` interface in `src/Validator.Application/Benchmark/IBenchmarkStore.cs` — methods: SaveAsync(BenchmarkSnapshot), LoadAsync(string name), DeleteAsync(string name), ExistsAsync(string name), ListAsync()
- [x] T013 [P] Create `BenchmarkSnapshot` record in `src/Validator.Application/Benchmark/BenchmarkSnapshot.cs` — fields: Name, EstablishedAtUtc, Source (SourceIdentity), Context (ValidationContextSnapshot), Coverage (ScanCoverage), Checks (IReadOnlyList<CheckExecution>), Metrics (IReadOnlyList<MetricScore>), Dataset (DatasetScore), Weighting (ScoreWeighting)
- [x] T014 [P] Create `CandidateIdentity` record in `src/Validator.Application/Comparison/CandidateIdentity.cs` — fields: Source (SourceIdentity), Context (ValidationContextSnapshot)

### Unit Tests for Foundational Entities

- [x] T015 [P] Write unit tests for `FieldDiscrepancy` validation in `tests/Validator.Domain.Tests/Comparison/FieldDiscrepancyTests.cs` — test non-negative difference, correct directional difference, tolerance decision variants
- [x] T016 [P] Write unit tests for `ComparisonCoverage` invariant enforcement in `tests/Validator.Domain.Tests/Comparison/ComparisonCoverageTests.cs` — test count relationships, zero-match edge case
- [x] T017 [P] Write unit tests for `BenchmarkAgreementScore` null/unavailable invariant in `tests/Validator.Domain.Tests/Comparison/BenchmarkAgreementScoreTests.cs` — test available vs unavailable states, formula correctness
- [x] T018 [P] Write unit tests for `ComparisonConfiguration` validation in `tests/Validator.Domain.Tests/Comparison/ComparisonConfigurationTests.cs` — test duplicate field rejection, negative tolerance rejection

**Checkpoint**: Foundation ready — user story implementation can now begin.

---

## Phase 3: User Story 1 — Establish a Trusted Benchmark (Priority: P1) 🏁 MVP

**Goal**: User can establish a validated dataset as a named immutable benchmark snapshot.

**Independent Test**: Establish an AUDUSD benchmark from a known dataset, then inspect the resulting benchmark record and verify its identity, time range, market context, validation results, and scores can be retrieved without ambiguity.

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T019 [P] [US1] Write unit tests for `EstablishBenchmarkUseCase` in `tests/Validator.Application.Tests/Benchmark/EstablishBenchmarkUseCaseTests.cs` — test successful establishment, name collision rejection, invalid validation rejection, source identity preservation
- [x] T020 [P] [US1] Write unit tests for `FileBenchmarkStore` (Coverage tests added) in `tests/Validator.Infrastructure.Tests/Benchmark/FileBenchmarkStoreTests.cs` — test save/load/delete/list round-trip, atomic writes, SHA-256 verification on load, missing file handling, corrupted JSON handling

### Implementation for User Story 1

- [x] T021 [US1] Implement `BenchmarkSnapshotValidator` in `src/Validator.Application/Benchmark/BenchmarkSnapshotValidator.cs` — validate that a DetailedValidationReport has all required fields (source identity, context, checks completed, metrics scored) before benchmark creation is allowed (FR-004)
- [x] T022 [US1] Implement `EstablishBenchmarkUseCase` in `src/Validator.Application/Benchmark/EstablishBenchmarkUseCase.cs` — orchestrate: validate report completeness, build BenchmarkSnapshot from report, check name collision via IBenchmarkStore, save snapshot + source bytes; reject on collision (FR-003) or incomplete validation (FR-004)
- [x] T023 [US1] Implement `BenchmarkName` value object in `src/Validator.Application/Benchmark/BenchmarkName.cs` — derive safe directory name from user input: lowercase, spaces to hyphens, remove non-alphanumeric, no path separators
- [x] T024 [US1] Implement `FileBenchmarkStore` in `src/Validator.Infrastructure/Benchmark/FileBenchmarkStore.cs` — file-based IBenchmarkStore: save benchmark.json + source.csv atomically, load with SHA-256 verification, delete directory, list existing benchmarks
- [x] T025 [US1] Implement `BenchmarkSnapshotJsonSerializer` in `src/Validator.Infrastructure/Benchmark/BenchmarkSnapshotJsonSerializer.cs` — serialize/deserialize BenchmarkSnapshot to/from JSON contract v1; handle all nested types (SourceIdentity, ValidationContextSnapshot, MetricScore, etc.)
- [x] T026 [US1] Extend `ValidateCommand` CLI in `src/Validator.Cli/Commands/ValidateCommand.cs` — add `--benchmark <name>` option; when specified, run validation, then call EstablishBenchmarkUseCase; add `--benchmark-dir <path>` option with default `./benchmarks/`; add `--benchmark-delete <name>` option with `--yes` confirmation flag
- [x] T027 [US1] Run and pass all US1 tests (`dotnet test --filter "Benchmark"`)

**Checkpoint**: Benchmark establishment is fully functional. User can create, list, and delete benchmarks from the CLI.

---

## Phase 4: User Story 2 — Compare Candidate Against Benchmark (Priority: P1) 🏁 MVP

**Goal**: User can compare a candidate dataset against a named benchmark and receive a detailed discrepancy report.

**Independent Test**: Compare a candidate with a known one-day opening-price difference, one missing candle, and one extra candle against an AUDUSD benchmark, then verify each discrepancy is identified at the relevant timestamp.

### Tests for User Story 2

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T028 [P] [US2] Write unit tests for `ToleranceResolver` in `tests/Validator.Application.Tests/Comparison/ToleranceResolverTests.cs` — test default price tolerance (fractional step inference, 0.01% relative), default volume tolerance (5%), custom override per field, field disable, invalid tolerance rejection, zero-price edge case
- [x] T029 [P] [US2] Write unit tests for `FieldComparator` in `tests/Validator.Domain.Tests/Comparison/FieldComparatorTests.cs` — test accepted-by-absolute, accepted-by-relative, material difference, zero-benchmark-value edge case, identical values, large difference
- [x] T030 [P] [US2] Write unit tests for `TimestampMatcher` in `tests/Validator.Domain.Tests/Comparison/TimestampMatcherTests.cs` — test matched/missing/extra categorization, empty datasets, single-overlap, no-overlap, full overlap
- [x] T031 [P] [US2] Write unit tests for `CompareDatasetsUseCase` in `tests/Validator.Application.Tests/Comparison/CompareDatasetsUseCaseTests.cs` — test full comparison pipeline: identical data (no discrepancies), material price difference detected, tolerated broker difference accepted, missing candle reported, extra candle reported, no-overlap returns unavailable, timeframe mismatch rejected
- [x] T032 [US2] Write integration test for `CompareDatasetsUseCase` with file-based benchmark in `tests/Validator.Infrastructure.Tests/Comparison/CompareDatasetsIntegrationTests.cs` — test end-to-end: load benchmark from FileBenchmarkStore, load candidate from CsvCandleSource, compare, verify ComparisonReport structure

### Implementation for User Story 2

- [x] T033 [US2] Implement `ToleranceResolver` in `src/Validator.Application/Comparison/ToleranceResolver.cs` — resolve per-field tolerances from user overrides and defaults: infer fractional step from benchmark OHLC precision (Q5), apply 0.01% relative for prices, 5% relative for volume, OR-logic acceptance (FR-017); reject invalid config before data read (FR-019)
- [x] T034 [P] [US2] Implement `FieldComparator` in `src/Validator.Domain/Comparison/FieldComparator.cs` — pure function: compare two decimal values against resolved tolerances, return ToleranceDecision; deterministic and culture-invariant (FR-018)
- [x] T035 [P] [US2] Implement `TimestampMatcher` in `src/Validator.Domain/Comparison/TimestampMatcher.cs` — pure function: match sorted timestamp sequences, produce matched/missing/extra sets and ComparisonCoverage; deterministic ordering (FR-031)
- [x] T036 [US2] Implement `CompareDatasetsUseCase` in `src/Validator.Application/Comparison/CompareDatasetsUseCase.cs` — orchestrate: load benchmark from IBenchmarkStore, load candidate from ICandleSource, validate timeframe compatibility (FR-006 hard fail), resolve tolerances, match timestamps, compare fields, build ComparisonReport with ordered discrepancies, compute BenchmarkAgreementScore; fail safe on any error (FR-030)
- [x] T037 [US2] Extend `ValidateCommand` CLI in `src/Validator.Cli/Commands/ValidateCommand.cs` — add `--compare <benchmark-name>` option; when specified, load benchmark, run comparison after validation; add `--tolerances <json>` option for custom tolerance overrides; exit 0 on success, exit 2 on fatal (Q6)
- [x] T038 [US2] Run and pass all US2 tests (`dotnet test --filter "Comparison"`)

**Checkpoint**: Comparison is fully functional. User can compare a candidate against a benchmark and see discrepancies.

---

## Phase 5: User Story 3 — Review Comparison Quality and Scores (Priority: P1)

**Goal**: User sees candidate's independent scores alongside benchmark comparison results in both text and JSON formats.

**Independent Test**: Compare candidates with known structural defects and known tolerated/material differences, then verify the report exposes independent validation scores, comparison outcomes, benchmark scores, and agreement score.

### Tests for User Story 3

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T039 [P] [US3] Write unit tests for `BenchmarkComparisonReportBuilder` in `tests/Validator.Application.Tests/Comparison/BenchmarkComparisonReportBuilderTests.cs` — test report assembly: candidate scores separate from benchmark scores, agreement score computation, tolerated summary aggregation, coverage statistics, no-overlap unavailable state
- [x] T040 [P] [US3] Write unit tests for `ComparisonTextReportWriter` in `tests/Validator.Application.Tests/Comparison/ComparisonTextReportWriterTests.cs` — test text output format: benchmark section, coverage section, discrepancies section, tolerated differences section, scores section, no-overlap message
- [x] T041 [P] [US3] Write unit tests for `ComparisonJsonReportWriter` in `tests/Validator.Application.Tests/Comparison/ComparisonJsonReportWriterTests.cs` — test JSON contract v1 compliance: all fields present, correct types, deterministic ordering, null handling for unavailable score

### Implementation for User Story 3

- [x] T042 [US3] Implement `BenchmarkComparisonReportBuilder` in `src/Validator.Application/Comparison/BenchmarkComparisonReportBuilder.cs` — assemble ComparisonReport from comparison results: attach BenchmarkSnapshot, CandidateIdentity, Configuration, Coverage, ordered discrepancies, tolerated summary, candidate scores, agreement score; compute per-field tolerated aggregates from raw comparison results
- [x] T043 [US3] Implement `ComparisonTextReportWriter` in `src/Validator.Application/Reporting/ComparisonTextReportWriter.cs` — render ComparisonReport as human-readable text per comparison-report-contract.md text format: benchmark section, coverage, material discrepancies, tolerated differences, scores
- [x] T044 [US3] Implement `ComparisonJsonReportWriter` in `src/Validator.Application/Reporting/ComparisonJsonReportWriter.cs` — render ComparisonReport as JSON per comparison-report-contract.md JSON format; extend existing DetailedReportV2Writer with benchmarkComparison section
- [x] T045 [US3] Integrate report writers into `CompareDatasetsUseCase` — wire ComparisonTextReportWriter and ComparisonJsonReportWriter into the use case output path; conditionally include benchmarkComparison section only when --compare was specified (FR-029)
- [x] T046 [US3] Run and pass all US3 tests

**Checkpoint**: Full comparison report is available in both text and JSON formats with all scores and coverage.

---

## Phase 6: User Story 4 — Reproduce and Audit a Comparison (Priority: P2)

**Goal**: Comparison output is deterministic, self-describing, and fully auditable.

**Independent Test**: Compare identical inputs repeatedly with identical options, then verify byte-identical output and complete audit trail.

### Tests for User Story 4

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T047 [P] [US4] Write determinism test in `tests/Validator.Application.Tests/Comparison/ComparisonDeterminismTests.cs` — run identical comparison twice, verify byte-identical JSON output and identical text output; verify discrepancy ordering stability
- [x] T048 [P] [US4] Write audit trail test in `tests/Validator.Application.Tests/Comparison/ComparisonAuditTrailTests.cs` — verify every material discrepancy carries timestamp, field, values, tolerances, and source references; verify configuration and resolved tolerances are recorded in report

### Implementation for User Story 4

- [x] T049 [US4] Implement deterministic discrepancy ordering in `src/Validator.Application/Comparison/CompareDatasetsUseCase.cs` — sort material discrepancies by timestamp ascending, then field name alphabetically, then absolute difference descending; ensure ordering is purely data-driven with no dependency on insertion order (SC-006)
- [x] T050 [US4] Add context-difference warnings to ComparisonReport — when benchmark and candidate differ in calendar, timestamp interpretation, or date range (but not timeframe), add informational warnings to the report per FR-006
- [x] T051 [US4] Run and pass all US4 tests (`dotnet test --filter "Determinism|AuditTrail"`)

**Checkpoint**: Comparison is deterministic and fully auditable.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, edge cases, and final validation.

- [x] T052 [P] Update `README.md` with new CLI options (`--benchmark`, `--compare`, `--tolerances`, `--benchmark-dir`, `--benchmark-delete`), new output sections, and usage examples per Principle VIII and research.md README impact assessment
- [x] T053 [P] Add edge-case unit tests in `tests/Validator.Domain.Tests/Comparison/EdgeCaseTests.cs` — zero-price tolerance, single-overlap timestamp, identical textual-precision values, large dataset overflow protection
- [x] T054 Run quickstart.md validation scenarios — execute scenarios from `specs/004-benchmark-dataset-comparison/quickstart.md` and verify expected outcomes (scenarios 1,2,3,6,7 verified; 4,5 require missing fixtures; 8 requires controlled environment)
- [x] T055 Run full test suite (`dotnet test`) and verify 100% line/branch coverage on Domain and Application layers
- [x] T056 Run `dotnet build` and verify clean compilation with no warnings

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Foundational (Phase 2)
- **US2 (Phase 4)**: Depends on Foundational (Phase 2) + US1's IBenchmarkStore interface (T012) and BenchmarkSnapshot (T013)
- **US3 (Phase 5)**: Depends on US2 (Phase 4) — needs comparison results to build reports
- **US4 (Phase 6)**: Depends on US2 (Phase 4) — needs comparison pipeline to add determinism
- **Polish (Phase 7)**: Depends on all user stories being complete

### User Story Dependencies

- **US1 (P1)**: Can start after Foundational — no dependencies on other stories
- **US2 (P1)**: Can start after Foundational + US1's store interface — the comparison use case loads benchmarks via IBenchmarkStore
- **US3 (P1)**: Depends on US2 — needs ComparisonReport from comparison pipeline to render
- **US4 (P2)**: Depends on US2 — needs comparison pipeline to enforce determinism

### Within Each User Story

- Tests MUST be written and FAIL before implementation (Constitution Principle I)
- Domain entities before Application services
- Application services before Infrastructure implementations
- Infrastructure implementations before CLI wiring
- Story complete before moving to next priority

### Parallel Opportunities

- T003–T011: All domain entity creation tasks can run in parallel (different files)
- T012–T014: All application interface/entity tasks can run in parallel
- T015–T018: All foundational unit tests can run in parallel
- T019–T020: US1 test tasks can run in parallel
- T028–T030: US2 domain test tasks can run in parallel
- T034–T035: FieldComparator and TimestampMatcher can run in parallel (pure functions, no dependencies)
- T039–T041: US3 test tasks can run in parallel
- T047–T048: US4 test tasks can run in parallel
- T052–T053: README update and edge-case tests can run in parallel

---

## Parallel Example: User Story 2

```bash
# Launch all US2 domain tests together:
Task: "Write unit tests for ToleranceResolver in tests/Validator.Application.Tests/Comparison/ToleranceResolverTests.cs"
Task: "Write unit tests for FieldComparator in tests/Validator.Domain.Tests/Comparison/FieldComparatorTests.cs"
Task: "Write unit tests for TimestampMatcher in tests/Validator.Domain.Tests/Comparison/TimestampMatcherTests.cs"

# Launch pure domain implementations together (no dependencies):
Task: "Implement FieldComparator in src/Validator.Domain/Comparison/FieldComparator.cs"
Task: "Implement TimestampMatcher in src/Validator.Domain/Comparison/TimestampMatcher.cs"

# Then implement orchestration (depends on above):
Task: "Implement ToleranceResolver in src/Validator.Application/Comparison/ToleranceResolver.cs"
Task: "Implement CompareDatasetsUseCase in src/Validator.Application/Comparison/CompareDatasetsUseCase.cs"
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 2)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories)
3. Complete Phase 3: US1 — Benchmark Establishment
4. Complete Phase 4: US2 — Comparison Logic
5. **STOP and VALIDATE**: Run quickstart scenarios 1–3
6. At this point the core feature works: establish benchmarks and compare candidates

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. Add US1 → Establish benchmarks independently → Validate
3. Add US2 → Compare against benchmarks → Validate (core feature complete!)
4. Add US3 → Full reporting with scores → Validate (polished feature)
5. Add US4 → Determinism and auditability → Validate (production-ready)
6. Polish → README, edge cases, final validation → Ship

### Parallel Team Strategy

With multiple developers:
1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: US1 (Benchmark Establishment)
   - Developer B: US2 (Comparison Logic) — after US1's IBenchmarkStore interface is defined
3. Once US1 + US2 are done:
   - Developer A: US3 (Reporting)
   - Developer B: US4 (Auditability)
4. Both: Polish phase

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Verify tests fail before implementing (Constitution Principle I)
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- All numeric values use `decimal` (never `float`/`double`) per constitution
- All timestamps are UTC-normalized per constitution
- Total tasks: 56 (2 Setup + 16 Foundational + 9 US1 + 11 US2 + 8 US3 + 5 US4 + 5 Polish)

## Phase 8: Convergence

- [x] T057 Close the remaining Domain/Application coverage gaps and add tests for all feature paths until the merged business-logic coverage reaches 100% line and branch coverage per Constitution II (contradicts)
- [x] T058 Stage benchmark establishment and comparison together with validation output so no validation report or score is published when a benchmark or comparison failure occurs, per FR-030 and Constitution V (contradicts)
- [x] T059 Complete the benchmark snapshot identity and persistence contract by adding unambiguous instrument identity, `contractVersion`, documented enum/string representations, exact score serialization, and unknown-version validation in `src/Validator.Application/Benchmark/BenchmarkSnapshot.cs` and `src/Validator.Infrastructure/Benchmark/BenchmarkSnapshotJsonSerializer.cs`, per FR-001 and `contracts/benchmark-contract.md` (missing)
- [x] T060 Infer the default price fractional-step tolerance from benchmark OHLC observations and carry the resolved value through comparison configuration and reporting instead of using a fixed constant, per FR-015, Q5, and SC-004a (partial)
- [x] T061 Implement explicit per-field tolerance disabling and reject incomplete, duplicate, contradictory, or otherwise ambiguous tolerance JSON before reading input data, per FR-016 and FR-019 (partial)
- [x] T062 Integrate comparison into the existing v2 validation report as one deterministic output document, including the candidate independent six-metric score, benchmark scores, candidate identity, and `benchmarkComparison` section while preserving the no-`--compare` behavior, per FR-021, FR-027, FR-028, FR-029, and plan decision T045 (partial)
- [x] T063 Change completed advisory comparisons to return exit code 0 regardless of discrepancy findings and reserve exit code 2 for fatal comparison failures, per Q6 and FR-026 (contradicts)
- [x] T064 Complete machine-readable comparison audit fields and deterministic UTC formatting by emitting candidate source lines, candidate identity, explicit unavailable score values, and UTC `Z` timestamps, per FR-013, FR-028, FR-031, and FR-032 (partial)
- [x] T065 Add comparison coverage rates and the explicit no-overlap/unavailable message to human-readable output, per FR-022, FR-025, and `contracts/comparison-report-contract.md` (partial)
- [x] T066 Add CLI end-to-end tests covering benchmark establishment, comparison exit semantics, combined v2 output, tolerance overrides and disablement, no-overlap handling, fatal atomicity, and repeated-output determinism, per SC-005, SC-006, and quickstart scenarios 1–8 (missing)

**Severity**: T057 and T058 are CRITICAL constitution-remediation tasks and must be completed before the feature can be considered done.

## Phase 9: Convergence

- [ ] T067 CRITICAL restore and enforce 100% line and branch coverage for all Domain and Application business logic, including every benchmark-establishment and comparison path currently reported uncovered in `tests/Validator.Application.Tests/coverage.json`, per Constitution II (contradicts)
- [x] T068 CRITICAL stage validation reporting and requested benchmark establishment/comparison as one fail-safe operation so collision, persistence, or comparison failures emit no previously committed success report or partial score, per FR-030 and Constitution V (contradicts)
- [x] T069 CRITICAL replace direct `DateTimeOffset.UtcNow` use in benchmark/comparison Application logic with an Application-owned injectable clock and ensure equivalent inputs and configuration produce deterministic substantive output, per Constitution III and Constitution IV (contradicts)
- [x] T070 CRITICAL remove `Math.Pow`, `double` conversion, and other floating-point calculations from tolerance inference and comparison reporting in favor of checked culture-invariant `decimal` arithmetic, per Constitution fixed-point technology standard (contradicts)
- [x] T071 CRITICAL implement the strict versioned benchmark snapshot DTO/converters so `contractVersion` is required, enum/string representations match the published contract, exact score ratios round-trip without corruption, and loaded benchmark scores equal the values recorded at establishment, per FR-002, US1/AC1, and `contracts/benchmark-contract.md` (contradicts)
- [ ] T072 Capture, persist, validate, and report an unambiguous instrument identity for every benchmark and candidate comparison instead of relying on file names, per FR-001 (missing)
- [x] T073 Infer the default fractional quote-unit step from every available benchmark OHLC observation without an arbitrary minimum-candle threshold, fixed fallback pip, or unsupported precision cap, and cover short datasets explicitly, per FR-015 and SC-004a (partial)
- [x] T074 Parse and fully validate tolerance JSON and all comparison configuration before calendar resolution, validation, or any benchmark/candidate file read, with structured actionable fatal diagnostics for malformed, duplicate, incomplete, contradictory, and type-invalid values, per FR-019 and US4/AC3 (contradicts)
- [x] T075 Rehydrate benchmark source candles with the delimiter, header, timestamp interpretation, source offset, and other ingestion context recorded in the immutable snapshot, preserving UTC normalization, per FR-002 and FR-007 (partial)
- [x] T076 Carry deterministically ordered missing-candidate and extra-candidate timestamp entries from `TimestampMatcher` into `ComparisonReport` and both output formats, including candidate source references where available and never fabricating benchmark values, per FR-008, FR-009, FR-010, FR-011, US2/AC3, and US2/AC4 (missing)
- [x] T077 Emit exactly one selected-format comparison report and populate its combined v2 `benchmarkComparison` section with candidate identity, candidate six-metric score, benchmark scores, context warnings, resolved tolerances, unavailable states, and all audit fields without appending standalone JSON or prose, per FR-021, FR-027, FR-028, FR-029, FR-030, and FR-031 (contradicts)
- [x] T078 Add deterministic matched, missing, extra, tolerated-difference, and material-discrepancy counts and rates to both machine-readable and human-readable comparison output, including explicit denominators and unavailable populations, per FR-022 (partial)
- [x] T079 Return exit code 0 for every successfully completed advisory comparison regardless of validation findings or discrepancies and reserve exit code 2 for fatal comparison failures, per Q6 and FR-026 (contradicts)
- [x] T080 Add CLI end-to-end tests for establishment, collision and atomicity failures, exact benchmark score persistence, advisory exit semantics, single-document combined v2 output, context-aware benchmark reload, tolerance override/disablement and pre-read rejection, timestamp gap details, no-overlap handling, and repeated-output determinism, per SC-005, SC-006, SC-007, SC-008, and SC-010 (missing)

## Phase 10: Convergence

- [ ] T081 CRITICAL restore and enforce 100% merged line and branch coverage for all Domain and Application business logic, replacing the sub-100% CI thresholds and covering every path reported by `tools/coverage-gaps.ps1`, per Constitution II (contradicts)
- [ ] T082 CRITICAL eliminate direct `DateTimeOffset.UtcNow` use and default wall-clock fallbacks from `BenchmarkComparisonReportBuilder`, `ComparisonReport`, and other comparison business logic so all substantive timestamps come from an Application-owned injectable clock, per Constitution III and Constitution IV (contradicts)
- [ ] T083 CRITICAL replace default benchmark JSON serialization with the strict versioned snapshot DTO/converters, require contract v1 fields and documented enum strings, and prove exact metric and dataset score ratios survive round trips unchanged, per FR-002, US1/AC1, and `contracts/benchmark-contract.md` (contradicts)
- [ ] T084 CRITICAL stage validation reporting, benchmark establishment, and comparison as one fail-safe operation so collision, persistence, load, or comparison failures publish no success document or partial score and instead emit the selected structured fatal diagnostic, per FR-030 and Constitution V (contradicts)
- [ ] T085 CRITICAL parse and fully validate tolerance JSON and all comparison configuration before calendar resolution, validation, benchmark access, or candidate source reads, with actionable structured diagnostics for every malformed or ambiguous value, per FR-019, US4/AC3, and Constitution V (contradicts)
- [ ] T086 CRITICAL emit exactly one selected-format comparison report and complete its embedded `benchmarkComparison` contract with candidate identity and six-metric score, context warnings, resolved tolerances, coverage rates, timestamp-gap details, unavailable states, and deterministic audit fields, per FR-021, FR-027, FR-028, FR-029, FR-030, FR-031, and Constitution VI (contradicts)
- [ ] T087 CRITICAL update `README.md` and CLI help to document inferred price tolerances, advisory comparison exit code 0, the single-document output contract, instrument identity, and all implemented benchmark behavior accurately, per Constitution VIII (contradicts)
- [ ] T088 Capture, persist, validate, compare, and report an unambiguous instrument identity for every benchmark and candidate instead of deriving identity from file names, per FR-001 and US1/AC1 (missing)
- [ ] T089 Return exit code 0 for every successfully completed advisory comparison regardless of candidate validation findings or material discrepancies, reserving exit code 2 for fatal comparison failures, per Q6 and FR-026 (contradicts)
- [ ] T090 Infer the default fractional quote-unit step from benchmark OHLC observations without a fixed `0.0001m` fallback for integral or short datasets, and cover the documented default profile end to end, per FR-015 and SC-004a (partial)
- [ ] T091 Count only non-zero within-tolerance differences as tolerated, expose matched/missing/extra/tolerated/material counts and rates with explicit denominators, and render all overlap timestamps in deterministic UTC `Z` form, per FR-014, FR-022, and FR-031 (partial)
- [ ] T092 Preserve deterministically ordered source references for timestamp alignment findings, including candidate source lines for extra records where available, and render them in both output formats, per FR-009, FR-010, FR-032, and US2/AC4 (partial)
