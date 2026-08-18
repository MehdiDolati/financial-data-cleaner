---
description: "Task list for Dataset Quality Scoring"
---

# Tasks: Dataset Quality Scoring

**Input**: Design documents from `/specs/003-dataset-quality-scoring/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Test tasks ARE included and are mandatory. Constitution Principle I
(Test-First) requires a failing test before each behaviour, and Principle II holds
Domain and Application scoring code to 100% line and branch coverage.

**Organization**: Tasks are grouped by user story so each story can be
implemented, tested, and delivered independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)
- Include exact file paths in descriptions

## Path Conventions

Existing four-project Clean Architecture solution at repository root:
`src/Validator.Domain/`, `src/Validator.Application/`,
`src/Validator.Infrastructure/`, `src/Validator.Cli/`, with mirrored test
projects under `tests/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the folders and fixtures every later phase writes into

- [ ] T001 [P] Create scoring source folders `src/Validator.Domain/Scoring/` and `src/Validator.Application/Scoring/`
- [ ] T002 [P] Create scoring test folders `tests/Validator.Domain.Tests/Scoring/` and `tests/Validator.Application.Tests/Scoring/`
- [ ] T003 [P] Add a scoring fixture with independently known counts and populations at `tests/Validator.Cli.Tests/Fixtures/scoring-known-populations.csv` (documented expected counts, accepted rows, examined rows, and expected candles)
- [ ] T004 [P] Add a single-row fixture at `tests/Validator.Cli.Tests/Fixtures/scoring-single-row.csv` so sequence checks cannot run and time-based metrics become not applicable

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Exact arithmetic, populations, the report carrier, the shared summary
lines, and CLI option plumbing that ALL user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Domain Arithmetic Primitives

- [ ] T005 [P] Write failing theories for exact rational arithmetic (GCD normalisation, sign normalisation, add, multiply, divide, exact compare, zero-denominator rejection) in `tests/Validator.Domain.Tests/Scoring/ExactRatioTests.cs`
- [ ] T006 Implement `ExactRatio` over `BigInteger` per data-model.md in `src/Validator.Domain/Scoring/ExactRatio.cs`
- [ ] T007 [P] Write failing theories for two-decimal half-away-from-zero rounding, culture-invariant formatting with trailing zeros (`100.00`, `0.00`), and rejection of values outside 0..100 in `tests/Validator.Domain.Tests/Scoring/ScoreValueTests.cs`
- [ ] T008 Implement `ScoreValue` (unrounded `Exact` plus presented `Rounded`) in `src/Validator.Domain/Scoring/ScoreValue.cs`
- [ ] T009 [P] Extend `tests/Validator.Domain.Tests/Architecture/DependencyRulesTests.cs` to assert no `float`/`double` member appears in any `Scoring` namespace and that scoring types reference no serializer, console, or file-system type

### Populations From the Existing Run

- [ ] T010 [P] Write failing tests asserting the expected open-market candle count is returned from the sequence walk, is `null` when sequence checks did not run, and agrees with the missing-candle count from the same walk, in `tests/Validator.Application.Tests/Scoring/MetricPopulationsTests.cs`
- [ ] T011 Count expected open-market slots inside the existing sequence walk and return it alongside the existing result in `src/Validator.Application/Validation/DetailedValidationOrchestrator.cs` (no new pass, no re-scan)
- [ ] T012 Implement `MetricPopulations` (expected candles, accepted rows, examined rows) sourced from `ScanCoverage` in `src/Validator.Application/Scoring/MetricPopulations.cs`
- [ ] T013 [P] Add `MetricPopulationKind` and `MetricScoreState` enumerations in `src/Validator.Application/Scoring/MetricPopulationKind.cs` and `src/Validator.Application/Scoring/MetricScoreState.cs`

### Report Carrier and Shared Summary Lines

- [ ] T014 Add an optional score section property (absent when scoring is not requested) to `src/Validator.Application/Reporting/DetailedValidationReport.cs`
- [ ] T015 [P] Write a failing test asserting the six summary lines are emitted from one shared label source and are byte-identical between the concise and verbose text writers, in `tests/Validator.Infrastructure.Tests/Reporting/SummaryLineParityTests.cs`
- [ ] T016 Centralise the six summary labels used by `src/Validator.Infrastructure/Reporting/TextReportWriter.cs` and `src/Validator.Infrastructure/Reporting/VerboseReportWriter.cs` into one shared source so they cannot drift

### CLI Option Plumbing and the V1 Conflict

- [ ] T017 [P] Write failing process-level tests for `--score` acceptance, `--score-weights` requiring `--score`, and the `--score` + v1 JSON configuration conflict (exit 2, empty stdout, message naming `--report-version 2`) in `tests/Validator.Cli.Tests/ScoringOptionsE2ETests.cs`
- [ ] T018 Add `--score` and `--score-weights` parsing plus the v1 conflict rejection as `INVALID_ARGUMENT` (Configuration/ArgumentValidation) before the source is opened, in `src/Validator.Cli/Commands/ValidateCommand.cs`
- [ ] T019 Route scored text runs through the detailed pipeline by extending the existing verbose routing condition in `src/Validator.Cli/Commands/ValidateCommand.cs`

**Checkpoint**: Exact arithmetic, populations, the optional report slot, shared
summary lines, and opt-in routing all exist. User story work can begin.

---

## Phase 3: User Story 1 - See Which Quality Dimension Is Weak (Priority: P1) 🎯 MVP

**Goal**: Score each of the six established metrics separately on a 0-to-100
scale, each stating the count and population it came from.

**Independent Test**: Score `scoring-known-populations.csv`; verify untouched
categories score exactly `100.00`, affected categories score exactly the value
their defect rate implies, and every score can be recalculated by hand from the
printed count and population.

### Tests for User Story 1 ⚠️

> Write these tests FIRST and confirm they FAIL before implementing

- [ ] T020 [P] [US1] Write failing theories asserting `score == 100 × (population − count) / population`, that zero defects score exactly `100.00`, and that a total defect rate scores exactly `0.00`, in `tests/Validator.Application.Tests/Scoring/MetricScoreCalculatorTests.cs`
- [ ] T021 [P] [US1] Write failing tests pinning each metric to its fixed population kind (missing candles and time gaps to expected candles; duplicates, invalid OHLC, and closed-market to accepted rows; malformed rows to examined rows) in `tests/Validator.Application.Tests/Scoring/MetricPopulationMappingTests.cs`
- [ ] T022 [P] [US1] Write failing tests asserting a metric whose check did not run is `NotApplicable` carrying the originating check reason and is never credited as `100.00`, in `tests/Validator.Application.Tests/Scoring/MetricApplicabilityTests.cs`
- [ ] T023 [P] [US1] Write failing tests asserting a zero population yields `NotScored` with a reason and is never credited as `100.00`, in `tests/Validator.Application.Tests/Scoring/ZeroPopulationTests.cs`
- [ ] T024 [P] [US1] Write failing tests asserting a count exceeding its population fails the run as `REPORT_RECONCILIATION_FAILED` rather than being clamped, in `tests/Validator.Application.Tests/Scoring/ImpossibleDefectRateTests.cs`
- [ ] T025 [P] [US1] Write failing tests asserting the constructor invariants that a score exists exactly when `Scored` and a reason exists exactly when not `Scored`, in `tests/Validator.Application.Tests/Scoring/MetricScoreInvariantTests.cs`
- [ ] T026 [P] [US1] Write a failing test asserting the text scoring section lists all six metrics in the established category order after the six summary lines, each stating score, count, population, and population kind, in `tests/Validator.Infrastructure.Tests/Reporting/ScoringTextSectionTests.cs`

### Implementation for User Story 1

- [ ] T027 [US1] Implement the `MetricScore` record with its state, count, population, population kind, score, reason, and constructor invariants in `src/Validator.Application/Scoring/MetricScore.cs`
- [ ] T028 [US1] Implement the per-metric score calculation over `ExactRatio`, including the impossible-rate failure path, in `src/Validator.Application/Scoring/MetricScoreCalculator.cs`
- [ ] T029 [US1] Implement the fixed metric-to-population-kind mapping in `src/Validator.Application/Scoring/MetricPopulationMap.cs`
- [ ] T030 [US1] Implement score-section assembly producing all six `MetricScore` values in established order from the summary, populations, and check statuses, in `src/Validator.Application/Scoring/ScoreSectionBuilder.cs`
- [ ] T031 [US1] Populate the optional score section on the report from the orchestrator when scoring is requested, in `src/Validator.Application/Validation/DetailedValidationOrchestrator.cs`
- [ ] T032 [US1] Render the labelled per-metric scoring section after the six summary lines, with the scale stated, in `src/Validator.Infrastructure/Reporting/ScoringTextSectionWriter.cs`
- [ ] T033 [US1] Emit the scoring section from both the concise and verbose text writers in `src/Validator.Infrastructure/Reporting/TextReportWriter.cs` and `src/Validator.Infrastructure/Reporting/VerboseReportWriter.cs`
- [ ] T034 [P] [US1] Write a failing end-to-end test scoring `scoring-known-populations.csv` and asserting hand-calculated per-metric scores, then make it pass, in `tests/Validator.Cli.Tests/ScoringE2ETests.cs`

**Checkpoint**: Per-metric scores are fully functional and independently testable
without any average, weighting, or JSON work.

---

## Phase 4: User Story 2 - Judge a Dataset by One Average Score (Priority: P1)

**Goal**: Report one dataset average over exactly the metrics that were scored,
stating its coverage.

**Independent Test**: Score datasets with known per-metric scores and verify the
average equals the documented weighted mean of exactly the scored metrics,
including when some metrics are not applicable.

### Tests for User Story 2 ⚠️

- [ ] T035 [P] [US2] Write failing tests asserting the average equals the mean of all six scores under equal default weights, in `tests/Validator.Application.Tests/Scoring/DatasetAverageTests.cs`
- [ ] T036 [P] [US2] Write failing tests asserting the average covers only scored metrics, reports its metric coverage, and lists excluded metrics with their state and reason, in `tests/Validator.Application.Tests/Scoring/AverageCoverageTests.cs`
- [ ] T037 [P] [US2] Write failing tests asserting the average is exactly `100.00` only when every covered metric scored `100.00`, in `tests/Validator.Application.Tests/Scoring/FlawlessAverageTests.cs`
- [ ] T038 [P] [US2] Write failing tests asserting an unavailable average is reported with its reason and never as `0.00`, `100.00`, or any substitute, in `tests/Validator.Application.Tests/Scoring/UnavailableAverageTests.cs`
- [ ] T039 [P] [US2] Write failing tests asserting the average is computed from unrounded metric scores and rounded once for presentation, in `tests/Validator.Application.Tests/Scoring/AverageRoundingTests.cs`
- [ ] T040 [P] [US2] Write a failing test asserting the average text line states its value and metric coverage, or its explicit unavailability with a reason, in `tests/Validator.Infrastructure.Tests/Reporting/ScoringAverageTextTests.cs`

### Implementation for User Story 2

- [ ] T041 [US2] Implement `DatasetScore` with the average, metric coverage, covered categories, excluded categories, and unavailability reason in `src/Validator.Application/Scoring/DatasetScore.cs`
- [ ] T042 [US2] Implement the weighted-mean average over unrounded `ExactRatio` scores, including both unavailability causes, in `src/Validator.Application/Scoring/DatasetAverageCalculator.cs`
- [ ] T043 [US2] Attach the dataset average and its coverage to the assembled score section in `src/Validator.Application/Scoring/ScoreSectionBuilder.cs`
- [ ] T044 [US2] Render the average line with coverage, excluded metrics, and the unavailable case in `src/Validator.Infrastructure/Reporting/ScoringTextSectionWriter.cs`
- [ ] T045 [P] [US2] Write a failing end-to-end test asserting the average is hand-recalculable from the report alone and that the single-row fixture yields a reduced-coverage average, then make it pass, in `tests/Validator.Cli.Tests/ScoringE2ETests.cs`

**Checkpoint**: Both P1 stories are complete. The MVP delivers per-metric scores
and one average in human-readable text.

---

## Phase 5: User Story 3 - Weight the Metrics for My Own Priorities (Priority: P2)

**Goal**: Let a caller supply all six weights so the average reflects their
priorities, while per-metric scores stay untouched.

**Independent Test**: Score the same dataset with default and supplied weights and
verify only the average changes, that it matches the weighted mean by hand, and
that every invalid weight input is rejected before scanning begins.

### Tests for User Story 3 ⚠️

- [ ] T046 [P] [US3] Write failing theories covering every rejected weight input — omitted metric, unknown name, duplicate name, negative value, non-numeric value, unparseable input, all-zero weights — each asserting the specific problem and the accepted form are stated, in `tests/Validator.Application.Tests/Scoring/ScoreWeightParsingTests.cs`
- [ ] T047 [P] [US3] Write failing tests asserting default weights are equal for all six metrics and are reported as resolved, in `tests/Validator.Application.Tests/Scoring/DefaultWeightingTests.cs`
- [ ] T048 [P] [US3] Write failing tests asserting supplied weights change only the average and leave every per-metric score, count, population, and applicability state unchanged, in `tests/Validator.Application.Tests/Scoring/WeightIsolationTests.cs`
- [ ] T049 [P] [US3] Write failing tests asserting a zero weight still scores and reports its metric while contributing nothing to the average, in `tests/Validator.Application.Tests/Scoring/ZeroWeightTests.cs`
- [ ] T050 [P] [US3] Write failing tests asserting normalised shares are reported only for metrics included in the average and sum to `1.00` after rounding, in `tests/Validator.Application.Tests/Scoring/NormalisedShareTests.cs`
- [ ] T051 [P] [US3] Write failing process-level tests asserting invalid weights exit `2` before any dataset content is read and produce no report, in `tests/Validator.Cli.Tests/ScoringWeightRejectionTests.cs`

### Implementation for User Story 3

- [ ] T052 [US3] Implement `MetricWeight` and `ScoreWeighting` with source, resolved weights, and normalised shares in `src/Validator.Application/Scoring/ScoreWeighting.cs`
- [ ] T053 [US3] Implement invariant-culture parsing and full validation of the six `metric=weight` pairs in `src/Validator.Application/Scoring/ScoreWeightParser.cs`
- [ ] T054 [US3] Implement default equal weighting and normalised-share resolution over the scored metrics in `src/Validator.Application/Scoring/ScoreWeightResolver.cs`
- [ ] T055 [US3] Apply the resolved weighting to the average and attach it to the score section in `src/Validator.Application/Scoring/ScoreSectionBuilder.cs`
- [ ] T056 [US3] Wire `--score-weights` parsing failures to `INVALID_ARGUMENT` before the source is opened in `src/Validator.Cli/Commands/ValidateCommand.cs`
- [ ] T057 [US3] Echo each metric's resolved weight and normalised share in the text scoring section in `src/Validator.Infrastructure/Reporting/ScoringTextSectionWriter.cs`
- [ ] T058 [P] [US3] Write a failing end-to-end test asserting per-metric scores are identical under default and custom weights while the average differs and is hand-recalculable, then make it pass, in `tests/Validator.Cli.Tests/ScoringWeightsE2ETests.cs`

**Checkpoint**: Weighting refines the average; all three earlier stories still
pass unchanged.

---

## Phase 6: User Story 4 - Trust, Reproduce, and Automate the Scores (Priority: P3)

**Goal**: Expose every scoring value as a documented v2 field, and prove
determinism, v1 immutability, source safety, and no-score-on-fatal.

**Independent Test**: Score identical bytes repeatedly and confirm byte-identical
output, then read every score, weight, population, and applicability value from
documented machine-readable fields alone.

### Tests for User Story 4 ⚠️

- [ ] T059 [P] [US4] Write a failing test validating a scored v2 document against `specs/003-dataset-quality-scoring/contracts/scoring-v2.schema.json` and asserting `contractVersion` remains `2`, in `tests/Validator.Cli.Tests/SchemaValidationTests.cs`
- [ ] T060 [P] [US4] Write a failing test asserting an unscored v2 document contains no `scoring` member and still validates, in `tests/Validator.Cli.Tests/DetailedReportV2E2ETests.cs`
- [ ] T061 [P] [US4] Write a failing test asserting every score, count, population, population kind, state, reason, resolved weight, normalised share, average, metric coverage, excluded metrics, and unavailability reason is a separate documented field, in `tests/Validator.Infrastructure.Tests/Reporting/ScoringV2WriterTests.cs`
- [ ] T062 [P] [US4] Write a failing test asserting repeated scored runs over identical bytes produce byte-identical output including formatting, in `tests/Validator.Cli.Tests/DeterminismTests.cs`
- [ ] T063 [P] [US4] Write a failing test asserting an unscored run's output is byte-identical to the recorded golden output and that a scored run's six summary lines, findings, finding order, and exit code are byte-identical to the same run without `--score`, in `tests/Validator.Cli.Tests/ScoringAdditiveOutputTests.cs`
- [ ] T064 [P] [US4] Write a failing test asserting v1 output is unchanged by this feature, in `tests/Validator.Cli.Tests/ReportCompatibilityTests.cs`
- [ ] T065 [P] [US4] Write a failing test asserting a fatal run with scoring requested emits no score on any stream and its diagnostic makes clear scoring did not occur, in `tests/Validator.Cli.Tests/FatalV2RoutingTests.cs`
- [ ] T066 [P] [US4] Write a failing test asserting the source dataset hash is unchanged by a scored run, in `tests/Validator.Cli.Tests/ScoringSourceProtectionTests.cs`

### Implementation for User Story 4

- [ ] T067 [US4] Emit the optional `scoring` object with all documented fields, omitted entirely when scoring is not requested, in `src/Validator.Infrastructure/Reporting/DetailedReportV2Writer.cs`
- [ ] T068 [US4] Apply the additive optional `scoring` property to the v2 success schema per `specs/003-dataset-quality-scoring/contracts/detailed-report-v2-amendment.md` in `specs/002-detailed-error-report/contracts/detailed-report-v2.schema.json`
- [ ] T069 [US4] Confirm no score is constructed on any fatal path in `src/Validator.Cli/Commands/ValidateCommand.cs` and `src/Validator.Application/Validation/DetailedValidationOrchestrator.cs`

**Checkpoint**: All four stories are independently functional; scoring is
automatable and auditable.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T070 [P] Document `--score` and `--score-weights` in the Options table, add a scored Usage example, and document the text scoring section, the optional v2 `scoring` field, and the v1 conflict in `README.md`
- [ ] T071 Enforce 100% line and branch coverage for the new scoring code in `Validator.Domain` and `Validator.Application` using `tools/coverage-run.ps1` and close any gap reported by `tools/coverage-gaps.ps1`
- [ ] T072 Run `tools/doc-status.ps1` and resolve any documentation drift the feature introduced
- [ ] T073 Execute every step of `specs/003-dataset-quality-scoring/quickstart.md` and confirm each expected outcome, including the validation checklist table
- [ ] T074 Run `dotnet build FinancialDataCleaner.slnx --configuration Release` and confirm zero warnings, then run the full suite with `dotnet test FinancialDataCleaner.slnx --configuration Release`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends on Foundational
- **User Story 2 (Phase 4)**: Depends on Foundational; consumes US1 metric scores
- **User Story 3 (Phase 5)**: Depends on Foundational; refines the US2 average
- **User Story 4 (Phase 6)**: Depends on Foundational; renders whatever sections exist
- **Polish (Phase 7)**: Depends on all delivered stories

### User Story Dependencies

- **US1 (P1)**: Independent once Foundational is done — the only story that needs nothing from another story
- **US2 (P1)**: Needs scored metrics from US1 to average; independently testable through its own average behaviour
- **US3 (P2)**: Needs the US2 average to reweight; per-metric scores from US1 must remain provably unchanged
- **US4 (P3)**: Independent of US2 and US3 in principle; renders and pins whichever sections exist

### Within Each User Story

- Tests are written and MUST fail before implementation
- Domain values before Application services
- Application services before Infrastructure rendering
- Rendering before end-to-end assertions

### Critical Sequencing Notes

- T011 must precede T012: populations cannot be resolved before the expected-candle count is returned from the walk.
- T016 must precede T033: the summary labels must be centralised before a second writer emits the scoring section, otherwise SC-006 cannot be guaranteed.
- T018 must precede T019: the options must parse before routing can branch on them.
- T030, T043, and T055 all edit `ScoreSectionBuilder.cs` and must run sequentially, never in parallel.
- T032, T044, and T057 all edit `ScoringTextSectionWriter.cs` and must run sequentially.
- T068 amends a published schema and must land in the same commit as T067 so the contract and its producer never disagree.

### Parallel Opportunities

- All Setup tasks (T001–T004) can run in parallel
- T005, T007, T009, T010, T013, T015, T017 can be written in parallel within Foundational
- All test tasks within a single story phase are marked [P] and can be written in parallel
- Once Foundational completes, US1 and US4's rendering tests can be drafted in parallel by different developers

---

## Parallel Example: User Story 1

```bash
# Write all failing tests for User Story 1 together:
Task: "Score formula theories in tests/Validator.Application.Tests/Scoring/MetricScoreCalculatorTests.cs"
Task: "Population mapping tests in tests/Validator.Application.Tests/Scoring/MetricPopulationMappingTests.cs"
Task: "Applicability tests in tests/Validator.Application.Tests/Scoring/MetricApplicabilityTests.cs"
Task: "Zero-population tests in tests/Validator.Application.Tests/Scoring/ZeroPopulationTests.cs"
Task: "Impossible-rate tests in tests/Validator.Application.Tests/Scoring/ImpossibleDefectRateTests.cs"
Task: "Text section tests in tests/Validator.Infrastructure.Tests/Reporting/ScoringTextSectionTests.cs"
```

---

## Implementation Strategy

### MVP First (Both P1 Stories)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories)
3. Complete Phase 3: User Story 1 → per-metric scores
4. Complete Phase 4: User Story 2 → the dataset average
5. **STOP and VALIDATE**: Run quickstart steps 4, 5, 6, and 10
6. The MVP is shippable: scores and one average in human-readable text, with the
   unscored path proven byte-identical

### Incremental Delivery

1. Setup + Foundational → opt-in plumbing and exact arithmetic ready
2. Add US1 → per-metric scores → validate → demo
3. Add US2 → one average with coverage → validate → demo (MVP complete)
4. Add US3 → caller weighting → validate → demo
5. Add US4 → v2 fields, determinism, and immutability proofs → validate → demo

### Constitution Guardrails

- Every behaviour begins with a failing test (Principle I)
- Domain and Application scoring code holds 100% line and branch coverage (Principle II)
- Rendering stays in Infrastructure; option parsing stays in the CLI (Principle III)
- Exact rationals only, no `float`/`double`, invariant two-decimal output (Principle IV)
- Impossible rates fail, absent values state a reason, no clamping (Principle V)
- Every value is a documented v2 field (Principle VI)
- No new check, fatal code, stage, port, persistence, or package (Principle VII)
- `README.md` ships with the feature in T070 (Principle VIII)

---

## Notes

- [P] tasks touch different files and have no incomplete dependencies
- Scoring is additive: never change a summary count, a finding, the finding order,
  the source bytes, or an exit code
- Verify each test fails before implementing the behaviour it requires
- Commit after each task or logical group
- Stop at any checkpoint to validate a story independently
