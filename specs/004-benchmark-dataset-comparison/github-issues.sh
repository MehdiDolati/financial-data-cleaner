#!/bin/bash
# GitHub Issue Creation Commands for Benchmark Dataset Comparison
# Generated from tasks.md — run from the repository root
# Requires: GitHub CLI (gh) authenticated with appropriate permissions
# Repository: MehdiDolati/financial-data-cleaner

set -e

REPO="MehdiDolati/financial-data-cleaner"

echo "Creating 56 GitHub issues for feature: 004-benchmark-dataset-comparison"
echo "Repository: $REPO"
echo ""

# ============================================================================
# Phase 1: Setup
# ============================================================================

echo "=== Phase 1: Setup ==="

gh issue create --repo "$REPO" \
  --title "T001: Create directory structure for benchmark comparison feature" \
  --body "## Task
Create directory structure for the benchmark comparison feature.

**Directories to create:**
- \`src/Validator.Domain/Benchmarks/\`
- \`src/Validator.Domain/Comparison/\`
- \`src/Validator.Application/Benchmark/\`
- \`src/Validator.Application/Comparison/\`
- \`src/Validator.Infrastructure/Benchmark/\`
- \`tests/Validator.Domain.Tests/Comparison/\`
- \`tests/Validator.Application.Tests/Benchmark/\`
- \`tests/Validator.Application.Tests/Comparison/\`
- \`tests/Validator.Infrastructure.Tests/Benchmark/\`

**Phase:** 1 (Setup)
**Feature:** 004-benchmark-dataset-comparison" \
  --label "phase:setup" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T002: Create test fixture CSV files" \
  --body "## Task
Create test fixture CSV files in \`tests/Fixtures/\`:

1. \`AUDUSD_D1_reference.csv\` — ~100 candles reference dataset
2. \`AUDUSD_D1_candidate_identical.csv\` — identical to reference
3. \`AUDUSD_D1_candidate_with_differences.csv\` — one material price difference + one tolerated broker difference
4. \`AUDUSD_D1_candidate_coverage_gaps.csv\` — missing and extra candles
5. \`AUDUSD_D1_candidate_no_overlap.csv\` — no overlapping timestamps

**Phase:** 1 (Setup)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]" \
  --label "phase:setup" \
  --label "feature:benchmark-comparison"

# ============================================================================
# Phase 2: Foundational
# ============================================================================

echo "=== Phase 2: Foundational ==="

gh issue create --repo "$REPO" \
  --title "T003: Create OhlcvField enum" \
  --body "## Task
Create \`OhlcvField\` enum in \`src/Validator.Domain/Comparison/OhlcvField.cs\`

**Values:** Open, High, Low, Close, Volume

**Phase:** 2 (Foundational)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]" \
  --label "phase:foundational" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T004: Create ToleranceDecision discriminated union" \
  --body "## Task
Create \`ToleranceDecision\` discriminated union in \`src/Validator.Domain/Comparison/ToleranceDecision.cs\`

**Variants:** AcceptedByAbsolute, AcceptedByRelative, MaterialDifference

**Phase:** 2 (Foundational)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]" \
  --label "phase:foundational" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T005: Create TimestampMode enum" \
  --body "## Task
Create \`TimestampMode\` enum in \`src/Validator.Domain/Comparison/TimestampMode.cs\`

**Value:** Exact

**Phase:** 2 (Foundational)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]" \
  --label "phase:foundational" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T006: Create FieldDiscrepancy record" \
  --body "## Task
Create \`FieldDiscrepancy\` record in \`src/Validator.Domain/Comparison/FieldDiscrepancy.cs\`

**Fields:**
- TimestampUtc (DateTimeOffset)
- Field (OhlcvField)
- BenchmarkValue (decimal)
- CandidateValue (decimal)
- Difference (decimal) — must be non-negative
- DirectionalDifference (decimal)
- ResolvedAbsoluteTolerance (decimal)
- ResolvedRelativeTolerance (decimal)
- ToleranceDecision

**Validation:** Immutable with Difference >= 0

**Phase:** 2 (Foundational)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]" \
  --label "phase:foundational" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T007: Create ComparisonCoverage record" \
  --body "## Task
Create \`ComparisonCoverage\` record in \`src/Validator.Domain/Comparison/ComparisonCoverage.cs\`

**Fields:**
- BenchmarkRecordCount (long)
- CandidateRecordCount (long)
- MatchedCount (long)
- MissingFromCandidateCount (long)
- ExtraInCandidateCount (long)
- OverlappingRange (DateRange?)

**Validation:** Enforce count invariants

**Phase:** 2 (Foundational)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]" \
  --label "phase:foundational" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T008: Create ToleratedDifferenceAggregate record" \
  --body "## Task
Create \`ToleratedDifferenceAggregate\` record in \`src/Validator.Domain/Comparison/ToleratedDifferenceAggregate.cs\`

**Fields:**
- Field (OhlcvField)
- TotalCompared (long)
- AcceptedCount (long)
- AcceptedByAbsoluteCount (long)
- AcceptedByRelativeCount (long)
- MaterialCount (long)

**Phase:** 2 (Foundational)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]" \
  --label "phase:foundational" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T009: Create BenchmarkAgreementScore record" \
  --body "## Task
Create \`BenchmarkAgreementScore\` record in \`src/Validator.Domain/Comparison/BenchmarkAgreementScore.cs\`

**Fields:**
- Score (ScoreValue?) — null iff UnavailableReason is non-null
- Formula (string)
- MatchedPopulation (long)
- MaterialDiscrepancyCount (long)
- UnavailableReason (string?)

**Phase:** 2 (Foundational)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]" \
  --label "phase:foundational" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T010: Create ComparedField record" \
  --body "## Task
Create \`ComparedField\` record in \`src/Validator.Domain/Comparison/ComparedField.cs\`

**Fields:**
- Field (OhlcvField)
- Enabled (bool)
- AbsoluteTolerance (decimal?)
- RelativeTolerance (decimal?)
- ResolvedAbsolute (decimal)
- ResolvedRelative (decimal)

**Phase:** 2 (Foundational)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]" \
  --label "phase:foundational" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T011: Create ComparisonConfiguration record" \
  --body "## Task
Create \`ComparisonConfiguration\` record in \`src/Validator.Domain/Comparison/ComparisonConfiguration.cs\`

**Fields:**
- BenchmarkName (string)
- Fields (IReadOnlyList<ComparedField>)
- TimestampMode (TimestampMode)

**Validation:** No duplicate fields, all tolerances non-negative

**Phase:** 2 (Foundational)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]" \
  --label "phase:foundational" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T012: Create IBenchmarkStore interface" \
  --body "## Task
Create \`IBenchmarkStore\` interface in \`src/Validator.Application/Benchmark/IBenchmarkStore.cs\`

**Methods:**
- SaveAsync(BenchmarkSnapshot)
- LoadAsync(string name)
- DeleteAsync(string name)
- ExistsAsync(string name)
- ListAsync()

**Phase:** 2 (Foundational)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]" \
  --label "phase:foundational" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T013: Create BenchmarkSnapshot record" \
  --body "## Task
Create \`BenchmarkSnapshot\` record in \`src/Validator.Application/Benchmark/BenchmarkSnapshot.cs\`

**Fields:**
- Name (string)
- EstablishedAtUtc (DateTimeOffset)
- Source (SourceIdentity)
- Context (ValidationContextSnapshot)
- Coverage (ScanCoverage)
- Checks (IReadOnlyList<CheckExecution>)
- Metrics (IReadOnlyList<MetricScore>)
- Dataset (DatasetScore)
- Weighting (ScoreWeighting)

**Phase:** 2 (Foundational)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]" \
  --label "phase:foundational" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T014: Create CandidateIdentity record" \
  --body "## Task
Create \`CandidateIdentity\` record in \`src/Validator.Application/Comparison/CandidateIdentity.cs\`

**Fields:**
- Source (SourceIdentity)
- Context (ValidationContextSnapshot)

**Phase:** 2 (Foundational)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]" \
  --label "phase:foundational" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T015: Write unit tests for FieldDiscrepancy validation" \
  --body "## Task
Write unit tests for \`FieldDiscrepancy\` validation in \`tests/Validator.Domain.Tests/Comparison/FieldDiscrepancyTests.cs\`

**Test cases:**
- Non-negative difference
- Correct directional difference
- Tolerance decision variants

**Phase:** 2 (Foundational)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]" \
  --label "phase:foundational" \
  --label "feature:benchmark-comparison" \
  --label "type:tests"

gh issue create --repo "$REPO" \
  --title "T016: Write unit tests for ComparisonCoverage invariant enforcement" \
  --body "## Task
Write unit tests for \`ComparisonCoverage\` invariant enforcement in \`tests/Validator.Domain.Tests/Comparison/ComparisonCoverageTests.cs\`

**Test cases:**
- Count relationships
- Zero-match edge case

**Phase:** 2 (Foundational)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]" \
  --label "phase:foundational" \
  --label "feature:benchmark-comparison" \
  --label "type:tests"

gh issue create --repo "$REPO" \
  --title "T017: Write unit tests for BenchmarkAgreementScore null/unavailable invariant" \
  --body "## Task
Write unit tests for \`BenchmarkAgreementScore\` null/unavailable invariant in \`tests/Validator.Domain.Tests/Comparison/BenchmarkAgreementScoreTests.cs\`

**Test cases:**
- Available vs unavailable states
- Formula correctness

**Phase:** 2 (Foundational)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]" \
  --label "phase:foundational" \
  --label "feature:benchmark-comparison" \
  --label "type:tests"

gh issue create --repo "$REPO" \
  --title "T018: Write unit tests for ComparisonConfiguration validation" \
  --body "## Task
Write unit tests for \`ComparisonConfiguration\` validation in \`tests/Validator.Domain.Tests/Comparison/ComparisonConfigurationTests.cs\`

**Test cases:**
- Duplicate field rejection
- Negative tolerance rejection

**Phase:** 2 (Foundational)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]" \
  --label "phase:foundational" \
  --label "feature:benchmark-comparison" \
  --label "type:tests"

# ============================================================================
# Phase 3: User Story 1 — Establish Benchmark (MVP)
# ============================================================================

echo "=== Phase 3: User Story 1 ==="

gh issue create --repo "$REPO" \
  --title "T019: Write unit tests for EstablishBenchmarkUseCase" \
  --body "## Task
Write unit tests for \`EstablishBenchmarkUseCase\` in \`tests/Validator.Application.Tests/Benchmark/EstablishBenchmarkUseCaseTests.cs\`

**Test cases:**
- Successful establishment
- Name collision rejection
- Invalid validation rejection
- Source identity preservation

**Phase:** 3 (US1 - Establish Benchmark)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]
**Note:** Write these tests FIRST, ensure they FAIL before implementation" \
  --label "phase:us1" \
  --label "feature:benchmark-comparison" \
  --label "type:tests"

gh issue create --repo "$REPO" \
  --title "T020: Write unit tests for FileBenchmarkStore" \
  --body "## Task
Write unit tests for \`FileBenchmarkStore\` in \`tests/Validator.Infrastructure.Tests/Benchmark/FileBenchmarkStoreTests.cs\`

**Test cases:**
- Save/load/delete/list round-trip
- Atomic writes
- SHA-256 verification on load
- Missing file handling
- Corrupted JSON handling

**Phase:** 3 (US1 - Establish Benchmark)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]
**Note:** Write these tests FIRST, ensure they FAIL before implementation" \
  --label "phase:us1" \
  --label "feature:benchmark-comparison" \
  --label "type:tests"

gh issue create --repo "$REPO" \
  --title "T021: Implement BenchmarkSnapshotValidator" \
  --body "## Task
Implement \`BenchmarkSnapshotValidator\` in \`src/Validator.Application/Benchmark/BenchmarkSnapshotValidator.cs\`

Validate that a DetailedValidationReport has all required fields (source identity, context, checks completed, metrics scored) before benchmark creation is allowed (FR-004).

**Phase:** 3 (US1 - Establish Benchmark)
**Feature:** 004-benchmark-dataset-comparison" \
  --label "phase:us1" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T022: Implement EstablishBenchmarkUseCase" \
  --body "## Task
Implement \`EstablishBenchmarkUseCase\` in \`src/Validator.Application/Benchmark/EstablishBenchmarkUseCase.cs\`

**Orchestration:**
1. Validate report completeness
2. Build BenchmarkSnapshot from report
3. Check name collision via IBenchmarkStore
4. Save snapshot + source bytes
5. Reject on collision (FR-003) or incomplete validation (FR-004)

**Phase:** 3 (US1 - Establish Benchmark)
**Feature:** 004-benchmark-dataset-comparison" \
  --label "phase:us1" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T023: Implement BenchmarkName value object" \
  --body "## Task
Implement \`BenchmarkName\` value object in \`src/Validator.Application/Benchmark/BenchmarkName.cs\`

**Logic:** Derive safe directory name from user input:
- Lowercase
- Spaces to hyphens
- Remove non-alphanumeric characters
- No path separators

**Phase:** 3 (US1 - Establish Benchmark)
**Feature:** 004-benchmark-dataset-comparison" \
  --label "phase:us1" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T024: Implement FileBenchmarkStore" \
  --body "## Task
Implement \`FileBenchmarkStore\` in \`src/Validator.Infrastructure/Benchmark/FileBenchmarkStore.cs\`

**File-based IBenchmarkStore:**
- Save benchmark.json + source.csv atomically
- Load with SHA-256 verification
- Delete directory
- List existing benchmarks

**Phase:** 3 (US1 - Establish Benchmark)
**Feature:** 004-benchmark-dataset-comparison" \
  --label "phase:us1" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T025: Implement BenchmarkSnapshotJsonSerializer" \
  --body "## Task
Implement \`BenchmarkSnapshotJsonSerializer\` in \`src/Validator.Infrastructure/Benchmark/BenchmarkSnapshotJsonSerializer.cs\`

**Responsibility:** Serialize/deserialize BenchmarkSnapshot to/from JSON contract v1; handle all nested types (SourceIdentity, ValidationContextSnapshot, MetricScore, etc.)

**Phase:** 3 (US1 - Establish Benchmark)
**Feature:** 004-benchmark-dataset-comparison" \
  --label "phase:us1" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T026: Extend ValidateCommand CLI with benchmark options" \
  --body "## Task
Extend \`ValidateCommand\` CLI in \`src/Validator.Cli/Commands/ValidateCommand.cs\`

**New options:**
- \`--benchmark <name>\` — establish validated dataset as named benchmark
- \`--benchmark-dir <path>\` — override default \`./benchmarks/\` directory
- \`--benchmark-delete <name>\` — delete existing benchmark
- \`--yes\` — skip confirmation for deletion

**Phase:** 3 (US1 - Establish Benchmark)
**Feature:** 004-benchmark-dataset-comparison" \
  --label "phase:us1" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T027: Run and pass all US1 tests" \
  --body "## Task
Run and pass all US1 tests:

\`\`\`bash
dotnet test --filter "Benchmark"
\`\`\`

**Phase:** 3 (US1 - Establish Benchmark)
**Feature:** 004-benchmark-dataset-comparison" \
  --label "phase:us1" \
  --label "feature:benchmark-comparison"

# ============================================================================
# Phase 4: User Story 2 — Compare Against Benchmark (MVP)
# ============================================================================

echo "=== Phase 4: User Story 2 ==="

gh issue create --repo "$REPO" \
  --title "T028: Write unit tests for ToleranceResolver" \
  --body "## Task
Write unit tests for \`ToleranceResolver\` in \`tests/Validator.Application.Tests/Comparison/ToleranceResolverTests.cs\`

**Test cases:**
- Default price tolerance (fractional step inference, 0.01% relative)
- Default volume tolerance (5%)
- Custom override per field
- Field disable
- Invalid tolerance rejection
- Zero-price edge case

**Phase:** 4 (US2 - Compare Against Benchmark)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]
**Note:** Write these tests FIRST, ensure they FAIL before implementation" \
  --label "phase:us2" \
  --label "feature:benchmark-comparison" \
  --label "type:tests"

gh issue create --repo "$REPO" \
  --title "T029: Write unit tests for FieldComparator" \
  --body "## Task
Write unit tests for \`FieldComparator\` in \`tests/Validator.Domain.Tests/Comparison/FieldComparatorTests.cs\`

**Test cases:**
- Accepted-by-absolute
- Accepted-by-relative
- Material difference
- Zero-benchmark-value edge case
- Identical values
- Large difference

**Phase:** 4 (US2 - Compare Against Benchmark)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]
**Note:** Write these tests FIRST, ensure they FAIL before implementation" \
  --label "phase:us2" \
  --label "feature:benchmark-comparison" \
  --label "type:tests"

gh issue create --repo "$REPO" \
  --title "T030: Write unit tests for TimestampMatcher" \
  --body "## Task
Write unit tests for \`TimestampMatcher\` in \`tests/Validator.Domain.Tests/Comparison/TimestampMatcherTests.cs\`

**Test cases:**
- Matched/missing/extra categorization
- Empty datasets
- Single-overlap
- No-overlap
- Full overlap

**Phase:** 4 (US2 - Compare Against Benchmark)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]
**Note:** Write these tests FIRST, ensure they FAIL before implementation" \
  --label "phase:us2" \
  --label "feature:benchmark-comparison" \
  --label "type:tests"

gh issue create --repo "$REPO" \
  --title "T031: Write unit tests for CompareDatasetsUseCase" \
  --body "## Task
Write unit tests for \`CompareDatasetsUseCase\` in \`tests/Validator.Application.Tests/Comparison/CompareDatasetsUseCaseTests.cs\`

**Test cases:**
- Identical data (no discrepancies)
- Material price difference detected
- Tolerated broker difference accepted
- Missing candle reported
- Extra candle reported
- No-overlap returns unavailable
- Timeframe mismatch rejected

**Phase:** 4 (US2 - Compare Against Benchmark)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]
**Note:** Write these tests FIRST, ensure they FAIL before implementation" \
  --label "phase:us2" \
  --label "feature:benchmark-comparison" \
  --label "type:tests"

gh issue create --repo "$REPO" \
  --title "T032: Write integration test for CompareDatasetsUseCase" \
  --body "## Task
Write integration test for \`CompareDatasetsUseCase\` with file-based benchmark in \`tests/Validator.Application.Tests/Comparison/CompareDatasetsIntegrationTests.cs\`

**Test:** End-to-end: load benchmark from FileBenchmarkStore, load candidate from CsvCandleSource, compare, verify ComparisonReport structure

**Phase:** 4 (US2 - Compare Against Benchmark)
**Feature:** 004-benchmark-dataset-comparison
**Note:** Write these tests FIRST, ensure they FAIL before implementation" \
  --label "phase:us2" \
  --label "feature:benchmark-comparison" \
  --label "type:tests"

gh issue create --repo "$REPO" \
  --title "T033: Implement ToleranceResolver" \
  --body "## Task
Implement \`ToleranceResolver\` in \`src/Validator.Application/Comparison/ToleranceResolver.cs\`

**Responsibility:** Resolve per-field tolerances from user overrides and defaults:
- Infer fractional step from benchmark OHLC precision (Q5)
- Apply 0.01% relative for prices
- Apply 5% relative for volume
- OR-logic acceptance (FR-017)
- Reject invalid config before data read (FR-019)

**Phase:** 4 (US2 - Compare Against Benchmark)
**Feature:** 004-benchmark-dataset-comparison" \
  --label "phase:us2" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T034: Implement FieldComparator" \
  --body "## Task
Implement \`FieldComparator\` in \`src/Validator.Domain/Comparison/FieldComparator.cs\`

**Responsibility:** Pure function: compare two decimal values against resolved tolerances, return ToleranceDecision; deterministic and culture-invariant (FR-018)

**Phase:** 4 (US2 - Compare Against Benchmark)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]" \
  --label "phase:us2" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T035: Implement TimestampMatcher" \
  --body "## Task
Implement \`TimestampMatcher\` in \`src/Validator.Domain/Comparison/TimestampMatcher.cs\`

**Responsibility:** Pure function: match sorted timestamp sequences, produce matched/missing/extra sets and ComparisonCoverage; deterministic ordering (FR-031)

**Phase:** 4 (US2 - Compare Against Benchmark)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]" \
  --label "phase:us2" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T036: Implement CompareDatasetsUseCase" \
  --body "## Task
Implement \`CompareDatasetsUseCase\` in \`src/Validator.Application/Comparison/CompareDatasetsUseCase.cs\`

**Orchestration:**
1. Load benchmark from IBenchmarkStore
2. Load candidate from ICandleSource
3. Validate timeframe compatibility (FR-006 hard fail)
4. Resolve tolerances
5. Match timestamps
6. Compare fields
7. Build ComparisonReport with ordered discrepancies
8. Compute BenchmarkAgreementScore
9. Fail safe on any error (FR-030)

**Phase:** 4 (US2 - Compare Against Benchmark)
**Feature:** 004-benchmark-dataset-comparison" \
  --label "phase:us2" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T037: Extend ValidateCommand CLI with compare options" \
  --body "## Task
Extend \`ValidateCommand\` CLI in \`src/Validator.Cli/Commands/ValidateCommand.cs\`

**New options:**
- \`--compare <benchmark-name>\` — compare candidate against benchmark
- \`--tolerances <json>\` — custom tolerance overrides
- Exit 0 on success, exit 2 on fatal (Q6)

**Phase:** 4 (US2 - Compare Against Benchmark)
**Feature:** 004-benchmark-dataset-comparison
**Depends on:** T026 (same file)" \
  --label "phase:us2" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T038: Run and pass all US2 tests" \
  --body "## Task
Run and pass all US2 tests:

\`\`\`bash
dotnet test --filter "Comparison"
\`\`\`

**Phase:** 4 (US2 - Compare Against Benchmark)
**Feature:** 004-benchmark-dataset-comparison" \
  --label "phase:us2" \
  --label "feature:benchmark-comparison"

# ============================================================================
# Phase 5: User Story 3 — Review Scores
# ============================================================================

echo "=== Phase 5: User Story 3 ==="

gh issue create --repo "$REPO" \
  --title "T039: Write unit tests for BenchmarkComparisonReportBuilder" \
  --body "## Task
Write unit tests for \`BenchmarkComparisonReportBuilder\` in \`tests/Validator.Application.Tests/Comparison/BenchmarkComparisonReportBuilderTests.cs\`

**Test cases:**
- Candidate scores separate from benchmark scores
- Agreement score computation
- Tolerated summary aggregation
- Coverage statistics
- No-overlap unavailable state

**Phase:** 5 (US3 - Review Scores)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]
**Note:** Write these tests FIRST, ensure they FAIL before implementation" \
  --label "phase:us3" \
  --label "feature:benchmark-comparison" \
  --label "type:tests"

gh issue create --repo "$REPO" \
  --title "T040: Write unit tests for ComparisonTextReportWriter" \
  --body "## Task
Write unit tests for \`ComparisonTextReportWriter\` in \`tests/Validator.Application.Tests/Comparison/ComparisonTextReportWriterTests.cs\`

**Test cases:**
- Benchmark section
- Coverage section
- Discrepancies section
- Tolerated differences section
- Scores section
- No-overlap message

**Phase:** 5 (US3 - Review Scores)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]
**Note:** Write these tests FIRST, ensure they FAIL before implementation" \
  --label "phase:us3" \
  --label "feature:benchmark-comparison" \
  --label "type:tests"

gh issue create --repo "$REPO" \
  --title "T041: Write unit tests for ComparisonJsonReportWriter" \
  --body "## Task
Write unit tests for \`ComparisonJsonReportWriter\` in \`tests/Validator.Application.Tests/Comparison/ComparisonJsonReportWriterTests.cs\`

**Test cases:**
- JSON contract v1 compliance
- All fields present
- Correct types
- Deterministic ordering
- Null handling for unavailable score

**Phase:** 5 (US3 - Review Scores)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]
**Note:** Write these tests FIRST, ensure they FAIL before implementation" \
  --label "phase:us3" \
  --label "feature:benchmark-comparison" \
  --label "type:tests"

gh issue create --repo "$REPO" \
  --title "T042: Implement BenchmarkComparisonReportBuilder" \
  --body "## Task
Implement \`BenchmarkComparisonReportBuilder\` in \`src/Validator.Application/Comparison/BenchmarkComparisonReportBuilder.cs\`

**Responsibility:** Assemble ComparisonReport from comparison results:
- Attach BenchmarkSnapshot, CandidateIdentity, Configuration, Coverage
- Ordered discrepancies
- Tolerated summary
- Candidate scores
- Agreement score
- Compute per-field tolerated aggregates from raw comparison results

**Phase:** 5 (US3 - Review Scores)
**Feature:** 004-benchmark-dataset-comparison" \
  --label "phase:us3" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T043: Implement ComparisonTextReportWriter" \
  --body "## Task
Implement \`ComparisonTextReportWriter\` in \`src/Validator.Application/Reporting/ComparisonTextReportWriter.cs\`

**Responsibility:** Render ComparisonReport as human-readable text per comparison-report-contract.md text format:
- Benchmark section
- Coverage
- Material discrepancies
- Tolerated differences
- Scores

**Phase:** 5 (US3 - Review Scores)
**Feature:** 004-benchmark-dataset-comparison" \
  --label "phase:us3" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T044: Implement ComparisonJsonReportWriter" \
  --body "## Task
Implement \`ComparisonJsonReportWriter\` in \`src/Validator.Application/Reporting/ComparisonJsonReportWriter.cs\`

**Responsibility:** Render ComparisonReport as JSON per comparison-report-contract.md JSON format; extend existing DetailedReportV2Writer with benchmarkComparison section

**Phase:** 5 (US3 - Review Scores)
**Feature:** 004-benchmark-dataset-comparison" \
  --label "phase:us3" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T045: Integrate report writers into CompareDatasetsUseCase" \
  --body "## Task
Integrate report writers into \`CompareDatasetsUseCase\`

**Responsibility:** Wire ComparisonTextReportWriter and ComparisonJsonReportWriter into the use case output path; conditionally include benchmarkComparison section only when --compare was specified (FR-029)

**Phase:** 5 (US3 - Review Scores)
**Feature:** 004-benchmark-dataset-comparison" \
  --label "phase:us3" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T046: Run and pass all US3 tests" \
  --body "## Task
Run and pass all US3 tests.

**Phase:** 5 (US3 - Review Scores)
**Feature:** 004-benchmark-dataset-comparison" \
  --label "phase:us3" \
  --label "feature:benchmark-comparison"

# ============================================================================
# Phase 6: User Story 4 — Reproduce and Audit
# ============================================================================

echo "=== Phase 6: User Story 4 ==="

gh issue create --repo "$REPO" \
  --title "T047: Write determinism test" \
  --body "## Task
Write determinism test in \`tests/Validator.Application.Tests/Comparison/ComparisonDeterminismTests.cs\`

**Test cases:**
- Run identical comparison twice
- Verify byte-identical JSON output
- Verify identical text output
- Verify discrepancy ordering stability

**Phase:** 6 (US4 - Reproduce and Audit)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]
**Note:** Write these tests FIRST, ensure they FAIL before implementation" \
  --label "phase:us4" \
  --label "feature:benchmark-comparison" \
  --label "type:tests"

gh issue create --repo "$REPO" \
  --title "T048: Write audit trail test" \
  --body "## Task
Write audit trail test in \`tests/Validator.Application.Tests/Comparison/ComparisonAuditTrailTests.cs\`

**Test cases:**
- Every material discrepancy carries timestamp, field, values, tolerances, source references
- Configuration and resolved tolerances are recorded in report

**Phase:** 6 (US4 - Reproduce and Audit)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]
**Note:** Write these tests FIRST, ensure they FAIL before implementation" \
  --label "phase:us4" \
  --label "feature:benchmark-comparison" \
  --label "type:tests"

gh issue create --repo "$REPO" \
  --title "T049: Implement deterministic discrepancy ordering" \
  --body "## Task
Implement deterministic discrepancy ordering in \`src/Validator.Application/Comparison/CompareDatasetsUseCase.cs\`

**Sorting:** Material discrepancies by:
1. Timestamp ascending
2. Field name alphabetically
3. Absolute difference descending

Ensure ordering is purely data-driven with no dependency on insertion order (SC-006)

**Phase:** 6 (US4 - Reproduce and Audit)
**Feature:** 004-benchmark-dataset-comparison" \
  --label "phase:us4" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T050: Add context-difference warnings to ComparisonReport" \
  --body "## Task
Add context-difference warnings to ComparisonReport

**Logic:** When benchmark and candidate differ in calendar, timestamp interpretation, or date range (but not timeframe), add informational warnings to the report per FR-006.

**Phase:** 6 (US4 - Reproduce and Audit)
**Feature:** 004-benchmark-dataset-comparison" \
  --label "phase:us4" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T051: Run and pass all US4 tests" \
  --body "## Task
Run and pass all US4 tests:

\`\`\`bash
dotnet test --filter "Determinism|AuditTrail"
\`\`\`

**Phase:** 6 (US4 - Reproduce and Audit)
**Feature:** 004-benchmark-dataset-comparison" \
  --label "phase:us4" \
  --label "feature:benchmark-comparison"

# ============================================================================
# Phase 7: Polish & Cross-Cutting
# ============================================================================

echo "=== Phase 7: Polish ==="

gh issue create --repo "$REPO" \
  --title "T052: Update README.md with benchmark comparison documentation" \
  --body "## Task
Update \`README.md\` with new CLI options and usage examples per Principle VIII.

**New options to document:**
- \`--benchmark <name>\`
- \`--compare <benchmark-name>\`
- \`--tolerances <json>\`
- \`--benchmark-dir <path>\`
- \`--benchmark-delete <name>\`

**New sections:**
- Benchmark comparison output format
- Usage examples

**Phase:** 7 (Polish)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]" \
  --label "phase:polish" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T053: Add edge-case unit tests" \
  --body "## Task
Add edge-case unit tests in \`tests/Validator.Domain.Tests/Comparison/EdgeCaseTests.cs\`

**Test cases:**
- Zero-price tolerance
- Single-overlap timestamp
- Identical textual-precision values
- Large dataset overflow protection

**Phase:** 7 (Polish)
**Feature:** 004-benchmark-dataset-comparison
**Parallel:** Yes [P]" \
  --label "phase:polish" \
  --label "feature:benchmark-comparison" \
  --label "type:tests"

gh issue create --repo "$REPO" \
  --title "T054: Run quickstart.md validation scenarios" \
  --body "## Task
Run quickstart.md validation scenarios

**Action:** Execute all 8 scenarios from \`specs/004-benchmark-dataset-comparison/quickstart.md\` and verify expected outcomes.

**Phase:** 7 (Polish)
**Feature:** 004-benchmark-dataset-comparison" \
  --label "phase:polish" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T055: Run full test suite and verify 100% coverage" \
  --body "## Task
Run full test suite and verify 100% line/branch coverage on Domain and Application layers.

\`\`\`bash
dotnet test
\`\`\`

**Phase:** 7 (Polish)
**Feature:** 004-benchmark-dataset-comparison" \
  --label "phase:polish" \
  --label "feature:benchmark-comparison"

gh issue create --repo "$REPO" \
  --title "T056: Verify clean build with no warnings" \
  --body "## Task
Run \`dotnet build\` and verify clean compilation with no warnings.

**Phase:** 7 (Polish)
**Feature:** 004-benchmark-dataset-comparison" \
  --label "phase:polish" \
  --label "feature:benchmark-comparison"

echo ""
echo "=== Done ==="
echo "Created 56 GitHub issues for feature: 004-benchmark-dataset-comparison"
echo "Repository: $REPO"
