# Research: Benchmark Dataset Comparison

**Feature**: 004-benchmark-dataset-comparison
**Date**: 2026-08-19

## Research Areas

### 1. Benchmark Storage and Immutability

**Decision**: File-based JSON snapshot stored alongside the original source bytes in a known benchmarks directory.

**Rationale**:
- The existing project has no database dependency and uses file-based I/O throughout (CSV ingestion, JSON report writing, atomic file writes).
- A benchmark must be an immutable reference snapshot: storing the exact source bytes (via SHA-256-verified copy) plus a JSON metadata file ensures reproducibility without requiring a separate storage backend.
- The JSON metadata captures source identity (name, byte size, SHA-256), validation context snapshot, six metric scores, dataset score, and establishment timestamp — all needed to reconstruct the benchmark identity without re-reading the original file.
- This follows the existing `SourceIdentity` pattern (fileName + byteSize + SHA-256) and the atomic write pattern already used in `StageAndCommitWriter`.

**Alternatives considered**:
- Database storage: rejected because it adds an external dependency the project does not have and is not justified for single-instrument benchmarks.
- In-memory only: rejected because benchmarks must survive process restarts and be shareable across runs.
- Storing only the hash and referencing the original file: rejected because the original file may be moved or deleted, breaking the benchmark.

**Benchmark directory structure**:
```
benchmarks/
└── <benchmark-name>/
    ├── benchmark.json      # Metadata, scores, validation context
    └── source.csv          # Exact copy of the original dataset bytes
```

### 2. Tolerance Resolution Strategy

**Decision**: Configurable per-field tolerances with a conservative forex-oriented default profile; tolerances resolved from the benchmark's declared quote precision when available.

**Rationale**:
- Per-field (Open, High, Low, Close, Volume) absolute and relative tolerances give the user full control.
- Default price tolerance: `max(one fractional quote-unit step, 0.01% of benchmark value)` — this filters broker rounding and small feed variations without hiding material changes.
- Default volume tolerance: `5% of benchmark value` — pragmatic baseline acknowledging volume is not directly comparable across providers.
- A difference is accepted when it falls within **either** the absolute **or** the relative tolerance (OR logic, not AND), which is more permissive and aligns with the spec's FR-017.
- Every resolved tolerance is recorded in the comparison report for auditability (Principle VI).

**Alternatives considered**:
- Fixed absolute tolerance only: rejected because it does not scale with price magnitude (1 pip matters differently at 0.6000 vs 1.2000).
- Statistical tolerance (e.g., standard deviation): rejected because it requires historical data and is harder for users to reason about; also conflicts with the spec's preference for explicit tolerances.
- Single global tolerance: rejected because volume and price have fundamentally different acceptable ranges.

### 3. Timestamp Matching and Alignment

**Decision**: Normalize both datasets to UTC, match by exact timestamp, report union of timestamps.

**Rationale**:
- `PriceCandle.Timestamp` already enforces UTC (zero offset) in its constructor, so both benchmark and candidate candles are guaranteed UTC-normalized at construction time.
- Matching by exact `DateTimeOffset` equality (which compares both instant and offset; both are UTC so this is equivalent to instant equality) is deterministic and unambiguous.
- The union of timestamps is computed and three populations reported: matched (present in both), missing from candidate (in benchmark only), extra in candidate (in candidate only).
- This follows the existing timestamp-sorted, deterministic ordering pattern in `ValidateMarketDataUseCase`.

**Alternatives considered**:
- Fuzzy timestamp matching (e.g., within N seconds): rejected because it adds ambiguity and the spec requires exact matching with tolerance on values, not timestamps.
- Frame-based alignment (aligning to nearest expected candle): rejected because it requires knowing the exact timeframe and introduces complexity not justified by the spec.

### 4. Benchmark-Agreement Score Formula

**Decision**: Separate benchmark-agreement score computed from comparison coverage and material discrepancy outcomes; kept separate from the candidate's independent six-metric quality score.

**Rationale**:
- The spec (FR-023, FR-024) explicitly requires the benchmark-agreement score to be separate and non-replacing.
- The score formula: `100 × (matched_timestamps - timestamps_with_material_discrepancies) / matched_timestamps` when matched_timestamps > 0; unavailable otherwise.
- Population for the score is the matched timestamp count — missing and extra timestamps are reported separately but do not directly reduce the agreement score (they affect coverage reporting instead).
- This mirrors the existing `MetricScoreCalculator` pattern (100 × (population - count) / population) using the same `ExactRatio` exact rational arithmetic.

**Alternatives considered**:
- Weighted combination of coverage and value agreement: rejected because it conflates two distinct concerns (coverage completeness vs. value accuracy) and makes the score harder to interpret.
- Including missing/extra timestamps in the discrepancy count: rejected because it would penalize a dataset that covers a different time range even if all shared values agree perfectly.

### 5. CLI Interface Design

**Decision**: Extend the existing `validator` command with `--benchmark` and `--compare` options; add a new `benchmark` subcommand for benchmark establishment.

**Rationale**:
- The existing CLI is a single-command tool (`validator <file> [options]`). Adding subcommands (`benchmark establish`, `benchmark compare`) would change the interface model.
- Instead, extending the existing command with:
  - `--benchmark <name>` : establish the validated dataset as a named benchmark
  - `--compare <benchmark-name>` : compare the validated dataset against a named benchmark
  - `--tolerances <config>` : override default tolerance profile (JSON or preset name)
- This is additive: existing usage with no new options behaves exactly as before (FR-029).
- The `--compare` option implies `--score` for the candidate (FR-021).

**Alternatives considered**:
- Separate `benchmark` subcommand: rejected because it changes the CLI interface model and requires restructuring the argument parser.
- Separate `benchmark-compare` binary: rejected as unnecessarily complex for a single-purpose feature.

### 6. Field Comparison Implementation

**Decision**: Compare by matching timestamps, then for each matched timestamp compare each OHLCV field independently using the resolved tolerance.

**Rationale**:
- Direct field-by-field comparison on `decimal` values is culture-invariant and deterministic.
- The comparison iterates the sorted union of timestamps, finds matching candles by timestamp, then evaluates each field against its resolved tolerance.
- Tolerance evaluation: `|benchmark_value - candidate_value| <= max(absolute_tolerance, relative_tolerance × |benchmark_value|)` — a difference is accepted when it satisfies either the absolute or relative tolerance.
- Edge cases handled:
  - Zero or near-zero benchmark prices: absolute tolerance applies when relative tolerance is unstable (spec edge case).
  - Different textual precision of equivalent values: both are parsed to `decimal` before comparison, so precision differences vanish.
  - No overlapping timestamps: comparison coverage is marked unavailable; no agreement score is presented.

**Alternatives considered**:
- Composite field tolerance (all fields must pass): rejected because a volume difference should not mask a price discrepancy.
- Percentage-only tolerance: rejected because it fails for very small prices (near-zero denominator).

### 7. Deterministic Ordering and Reproducibility

**Decision**: Discrepancies ordered by timestamp, then field name (alphabetical), then absolute difference (descending).

**Rationale**:
- This produces a stable, deterministic ordering that satisfies SC-006 (byte-identical output with identical inputs).
- Using absolute difference as a tiebreaker (descending) surfaces the most material discrepancies first, aiding human review (SC-009).
- The ordering is purely a function of the data and configuration, with no dependency on insertion order, process state, or wall clock.

### 8. README Impact Assessment (Principle VIII)

**Decision**: README.md must be updated.

**Affected sections**:
- Usage / CLI options: new `--benchmark`, `--compare`, and `--tolerances` options
- Output format: new benchmark-agreement section in text and JSON reports
- Examples: benchmark establishment and comparison examples
- Architecture: mention of benchmark and comparison modules

**No-impact rationale**: N/A — this feature has clear README impact.
