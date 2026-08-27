# Data Model: Benchmark Dataset Comparison

**Feature**: 004-benchmark-dataset-comparison
**Date**: 2026-08-19

## Entities

### 1. BenchmarkSnapshot

An immutable reference snapshot of a validated dataset, persisted as a JSON file alongside the source bytes.

| Field | Type | Description |
|-------|------|-------------|
| `Name` | `string` | User-assigned unique name (non-empty, no path separators) |
| `EstablishedAtUtc` | `DateTimeOffset` | UTC timestamp of benchmark creation |
| `Source` | `SourceIdentity` | Exact identity of the source bytes (fileName, byteSize, SHA-256) |
| `Context` | `ValidationContextSnapshot` | Validation configuration: timeframe, calendar, timestamp interpretation, delimiter |
| `Coverage` | `ScanCoverage` | Scan coverage: total records, date range, examined rows |
| `Checks` | `IReadOnlyList<CheckExecution>` | Six check execution results (canonical order) |
| `Metrics` | `IReadOnlyList<MetricScore>` | Six metric scores (canonical order) |
| `Dataset` | `DatasetScore` | Dataset average score with coverage and excluded metrics |
| `Weighting` | `ScoreWeighting` | The resolved weighting used for the dataset average |

**Constraints**:
- `Name` must be unique across all benchmarks; creation is rejected on collision (FR-003).
- The benchmark is immutable after creation; any change to source or scoring context creates a distinct identity (assumption).
- If validation does not produce a trustworthy complete report, benchmark creation is rejected (FR-004).

**State transitions**:
- `Creating` → `Established` (on successful validation and save)
- `Creating` → `Rejected` (on validation failure or name collision)

---

### 2. ComparisonConfiguration

The explicitly resolved rules for a comparison run, built from user-supplied options and defaults.

| Field | Type | Description |
|-------|------|-------------|
| `BenchmarkName` | `string` | Name of the benchmark to compare against |
| `Fields` | `IReadOnlyList<ComparedField>` | Which OHLCV fields to compare and their tolerances |
| `TimestampMode` | `TimestampMode` | How timestamps are matched (exact only for now) |

#### ComparedField

| Field | Type | Description |
|-------|------|-------------|
| `Field` | `OhlcvField` | The OHLCV field (Open, High, Low, Close, Volume) |
| `Enabled` | `bool` | Whether this field is included in comparison |
| `AbsoluteTolerance` | `decimal?` | Absolute tolerance (null = not configured) |
| `RelativeTolerance` | `decimal?` | Relative tolerance as a fraction (null = not configured) |
| `ResolvedAbsolute` | `decimal` | Final resolved absolute tolerance after default application |
| `ResolvedRelative` | `decimal` | Final resolved relative tolerance after default application |

#### OhlcvField (enum)

| Value | Description |
|-------|-------------|
| `Open` | Opening price |
| `High` | Highest price |
| `Low` | Lowest price |
| `Close` | Closing price |
| `Volume` | Trading volume |

#### TimestampMode (enum)

| Value | Description |
|-------|-------------|
| `Exact` | Match by exact timestamp equality |

**Constraints**:
- Configuration is validated and fully resolved **before** any dataset is read (FR-019).
- Invalid, negative, or contradictory tolerances are rejected with an actionable diagnostic.
- A field can be explicitly disabled (FR-016).

---

### 3. ComparisonCoverage

Describes the overlap between benchmark and candidate datasets.

| Field | Type | Description |
|-------|------|-------------|
| `BenchmarkRecordCount` | `long` | Number of candles in the benchmark |
| `CandidateRecordCount` | `long` | Number of candles in the candidate |
| `MatchedCount` | `long` | Timestamps present in both datasets |
| `MissingFromCandidateCount` | `long` | Timestamps in benchmark but not in candidate |
| `ExtraInCandidateCount` | `long` | Timestamps in candidate but not in benchmark |
| `OverlappingRange` | `DateRange?` | Time range of matched timestamps (null if no overlap) |

**Constraints**:
- `BenchmarkRecordCount` = `MatchedCount` + `MissingFromCandidateCount`
- `CandidateRecordCount` = `MatchedCount` + `ExtraInCandidateCount`
- If `MatchedCount` is 0, comparison is marked unavailable.

---

### 4. FieldDiscrepancy

A single material difference between a benchmark and candidate value at a shared timestamp.

| Field | Type | Description |
|-------|------|-------------|
| `TimestampUtc` | `DateTimeOffset` | The shared timestamp (UTC) |
| `Field` | `OhlcvField` | Which OHLCV field differs |
| `BenchmarkValue` | `decimal` | Value from the benchmark dataset |
| `CandidateValue` | `decimal` | Value from the candidate dataset |
| `Difference` | `decimal` | Absolute difference: `|benchmark - candidate|` |
| `DirectionalDifference` | `decimal` | Signed difference: `candidate - benchmark` |
| `ResolvedAbsoluteTolerance` | `decimal` | The absolute tolerance that was applied |
| `ResolvedRelativeTolerance` | `decimal` | The relative tolerance that was applied |
| `ToleranceDecision` | `ToleranceDecision` | How the difference was classified |

#### ToleranceDecision (discriminated union)

| Variant | Description |
|---------|-------------|
| `AcceptedByAbsolute` | Difference within the absolute tolerance |
| `AcceptedByRelative` | Difference within the relative tolerance |
| `MaterialDifference` | Difference exceeds both tolerances |

**Constraints**:
- `Difference` is always non-negative.
- `BenchmarkValue` and `CandidateValue` are always the parsed `decimal` values.
- Missing and extra timestamps produce **no** FieldDiscrepancy — they are reported at the coverage level.

---

### 5. ToleratedDifferenceAggregate

Aggregate count of differences that were within tolerance (not material).

| Field | Type | Description |
|-------|------|-------------|
| `Field` | `OhlcvField` | Which OHLCV field |
| `TotalCompared` | `long` | Timestamps where both values existed |
| `AcceptedCount` | `long` | Differences within tolerance |
| `AcceptedByAbsoluteCount` | `long` | Accepted by absolute tolerance specifically |
| `AcceptedByRelativeCount` | `long` | Accepted by relative tolerance specifically |
| `MaterialCount` | `long` | Differences exceeding both tolerances |

---

### 6. BenchmarkAgreementScore

The benchmark-relative agreement result, kept separate from the candidate's independent quality score.

| Field | Type | Description |
|-------|------|-------------|
| `Score` | `ScoreValue?` | The agreement score (null if unavailable) |
| `Formula` | `string` | Human-readable description of how the score was calculated |
| `MatchedPopulation` | `long` | Timestamps used as the denominator |
| `MaterialDiscrepancyCount` | `long` | Timestamps with at least one material field discrepancy |
| `UnavailableReason` | `string?` | Why the score cannot be computed (null if available) |

**Formula**: `100 × (matchedPopulation - materialDiscrepancyCount) / matchedPopulation` when matchedPopulation > 0.

**Unavailable when**:
- `MatchedCount` is 0 (no overlapping timestamps)
- Either dataset could not be fully loaded

---

### 7. ComparisonReport

The complete result of one comparison run, combining both datasets' independent scores with the comparison results.

| Field | Type | Description |
|-------|------|-------------|
| `Benchmark` | `BenchmarkSnapshot` | The benchmark used (with scores) |
| `Candidate` | `CandidateIdentity` | Identity and validation context of the candidate |
| `Configuration` | `ComparisonConfiguration` | The resolved comparison rules |
| `Coverage` | `ComparisonCoverage` | Timestamp matching statistics |
| `MaterialDiscrepancies` | `IReadOnlyList<FieldDiscrepancy>` | Ordered list of material differences |
| `ToleratedSummary` | `IReadOnlyList<ToleratedDifferenceAggregate>` | Per-field accepted difference counts |
| `CandidateScore` | `DatasetScoreReport` | Candidate's independent six-metric quality scores |
| `AgreementScore` | `BenchmarkAgreementScore` | Benchmark-agreement result |
| `ResolutionTimestamp` | `DateTimeOffset` | When this report was generated |

#### CandidateIdentity

| Field | Type | Description |
|-------|------|-------------|
| `Source` | `SourceIdentity` | Candidate source bytes identity |
| `Context` | `ValidationContextSnapshot` | Candidate validation configuration |

---

## Relationships

```
BenchmarkSnapshot 1 ────── 1 ComparisonReport
                              │
CandidateIdentity 1 ─────── 1 ComparisonReport
                              │
ComparisonConfiguration 1 ── 1 ComparisonReport
                              │
ComparisonCoverage 1 ──────── 1 ComparisonReport
                              │
FieldDiscrepancy * ────────── 0..* ComparisonReport
                              │
ToleratedDifferenceAggregate * ── ComparisonReport
                              │
BenchmarkAgreementScore 1 ── 1 ComparisonReport
```

## Validation Rules

- A `FieldDiscrepancy` is only produced when the absolute difference exceeds both the resolved absolute and relative tolerances (FR-012, FR-017).
- `ComparisonCoverage` must satisfy the count invariants before any discrepancy is reported.
- `BenchmarkAgreementScore.Score` must be null with a non-null `UnavailableReason` when `MatchedCount` is 0 (FR-025).
- Tolerance resolution must happen before data is read (FR-019); the resolved values are immutable during comparison.
- No discrepancy, score, or report may be produced if a fatal validation or comparison failure occurs (FR-030).
