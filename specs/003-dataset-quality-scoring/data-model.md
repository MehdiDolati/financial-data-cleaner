# Data Model: Dataset Quality Scoring

## Modeling Conventions

- All scoring values are immutable records or value objects with no framework
  dependency, and live in `Validator.Domain/Scoring` or
  `Validator.Application/Scoring`.
- Counts and populations are non-negative `long`, carried unchanged from the
  established summary and scan coverage.
- Exact intermediate values are `ExactRatio` (rational over `BigInteger`).
  Presented values are `decimal` at exactly two decimal places. `float` and
  `double` appear nowhere.
- Weights are non-negative `decimal`; zero is legal.
- Every collection is a fixed six-element sequence in the established category
  order: MissingCandle, DuplicateRecord, InvalidOhlc, ClosedMarketRecord,
  TimeGap, MalformedRow.
- A value that does not exist is represented by an explicit state plus a reason,
  never by a substituted number, and never by silent omission.
- Scoring is derived data. No scoring type can mutate a summary count, a finding,
  a finding's order, the source dataset, or an exit code.

## Domain Values

### `ExactRatio`

An exact rational used for every score computation.

| Field | Type | Rules |
|---|---|---|
| `Numerator` | `BigInteger` | Carries the sign. |
| `Denominator` | `BigInteger` | Never zero; normalised positive. |

Behaviour:

- Reduced by GCD on construction, so equal values have one representation and
  compare and format identically.
- Supports addition, multiplication, and division by a non-zero ratio; division
  by zero throws rather than producing a sentinel.
- `Compare` against another ratio is exact, with no widening to a floating type.

**Rules**: Construction with a zero denominator is invalid. The type performs no
rounding; rounding exists only at the presentation boundary below.

### `ScoreValue`

The presentation form of a score on the 0-to-100 scale.

| Field | Type | Rules |
|---|---|---|
| `Exact` | `ExactRatio` | The unrounded score; the average is computed from this, never from `Rounded`. |
| `Rounded` | `decimal` | `Exact` rounded to exactly two decimals, half away from zero. |

**Rules**: `Exact` must lie within 0..100 inclusive; a value outside that range
is an internal inconsistency and throws. Formatting is culture-invariant and
always emits two decimal places, including trailing zeros (`100.00`, `0.00`).

## Application Values

### `MetricPopulationKind`

Enumeration naming the denominator a metric is measured against:
`ExpectedCandles`, `AcceptedRows`, `ExaminedRows`.

Fixed mapping, which is the sole authority for FR-007:

| Metric | Population kind |
|---|---|
| MissingCandle | `ExpectedCandles` |
| TimeGap | `ExpectedCandles` |
| DuplicateRecord | `AcceptedRows` |
| InvalidOhlc | `AcceptedRows` |
| ClosedMarketRecord | `AcceptedRows` |
| MalformedRow | `ExaminedRows` |

### `MetricPopulations`

The three population values for one run, resolved once and shared by all six
metrics.

| Field | Type | Rules |
|---|---|---|
| `ExpectedCandles` | `long?` | Count of expected open-market slots in the evaluated range. `null` when the sequence checks did not run. |
| `AcceptedRows` | `long` | From `ScanCoverage.AcceptedRows`; non-negative. |
| `ExaminedRows` | `long` | From `ScanCoverage.PhysicalRowsExamined`; non-negative. |

**Rules**: Values are copied from the established run, never recomputed.
`ExpectedCandles` is counted during the existing expected-sequence walk, so it
cannot disagree with the missing-candle count derived from the same walk.

### `MetricScoreState`

Enumeration with exactly three values:

| Value | Meaning |
|---|---|
| `Scored` | The check ran and the population is positive; a score exists. |
| `NotApplicable` | The underlying check did not run for this configuration. |
| `NotScored` | The check ran but the population is zero, so the rate is undefined. |

`NotApplicable` and `NotScored` are distinct because their causes differ and the
report must distinguish them (FR-012 vs FR-013).

### `MetricScore`

The scored result for one of the six established metrics.

| Field | Type | Rules |
|---|---|---|
| `Category` | `FindingCategory` | One of the six established categories. |
| `State` | `MetricScoreState` | Exactly one state. |
| `Count` | `long` | The established summary count; non-negative. |
| `Population` | `long?` | The denominator used; `null` only when `NotApplicable` and no population exists. |
| `PopulationKind` | `MetricPopulationKind` | Fixed by the table above. |
| `Score` | `ScoreValue?` | Present if and only if `State == Scored`. |
| `Reason` | `string?` | Non-empty if and only if `State != Scored`. |

**Rules**:

- `Score` is non-null exactly when `State == Scored`; `Reason` is non-null
  exactly when `State != Scored`. Both invariants are enforced in the
  constructor, so no instance can be both unscored and valued.
- When `State == Scored`: `Population > 0` and `Count <= Population`. A count
  exceeding its population implies a defect rate above 1 and is an internal
  inconsistency that fails the run — it is never clamped.
- When `State == Scored`: `Score.Exact == 100 × (Population − Count) /
  Population`, which is 100 exactly when `Count == 0` and 0 exactly when
  `Count == Population`.
- A `NotApplicable` metric reuses the originating `CheckExecution.Reason`, so the
  report gives one explanation of the fact rather than two.
- `NotScored` states the zero population as its reason.
- No state is ever credited as a perfect score.

### `MetricWeight`

The resolved weight of one metric.

| Field | Type | Rules |
|---|---|---|
| `Category` | `FindingCategory` | One of the six established categories. |
| `Weight` | `decimal` | Non-negative; zero allowed. |
| `NormalisedShare` | `decimal?` | The metric's share of the weights actually used for the average, to two decimals; `null` when the metric is excluded from the average. |

**Rules**: `NormalisedShare` is non-null exactly for metrics with
`State == Scored` when an average is available. The unrounded shares sum to
exactly 1; each share is rounded independently for presentation, so the printed
shares need not sum to `1.00` (six equal shares print as `0.17` and sum to
`1.02`). A weight of zero is retained and reported; it contributes nothing to the
average but never suppresses the metric's own score.

### `ScoreWeighting`

The complete resolved weighting for a run.

| Field | Type | Rules |
|---|---|---|
| `Source` | enum | `Default` or `CallerSupplied`. |
| `Weights` | six `MetricWeight` | One per category, in established order. |

**Rules**:

- `Default` assigns every metric an equal weight, so the default average is a
  plain mean and is deliberately neutral.
- `CallerSupplied` requires all six metrics explicitly; an omission, unknown
  name, duplicate name, negative value, non-numeric value, unparseable input, or
  an all-zero set is rejected before the dataset is read and produces no report.
- Weights affect only the average. They never change a per-metric score, count,
  population, applicability state, or finding.

### `DatasetScore`

The dataset's single average score and its coverage.

| Field | Type | Rules |
|---|---|---|
| `Average` | `ScoreValue?` | Present if and only if an average is available. |
| `MetricsCovered` | `int` | Number of metrics the average covers, 0..6. |
| `CoveredCategories` | sequence | The categories included, in established order. |
| `ExcludedCategories` | sequence | The categories excluded, each with its state and reason. |
| `UnavailableReason` | `string?` | Non-empty if and only if `Average` is null. |

**Rules**:

- `Average.Exact == Σ(scoreᵢ.Exact × weightᵢ) / Σ(weightᵢ)` over scored metrics
  only, computed from unrounded metric scores and rounded once for presentation.
- `MetricsCovered` equals the number of scored metrics, and
  `CoveredCategories.Count + ExcludedCategories.Count == 6` always.
- The average is unavailable exactly when no metric is scored, or when the
  weights of all scored metrics sum to zero. It is then reported with its reason
  and never as `0.00`, `100.00`, or any substitute.
- An average of `100.00` is reported only when every covered metric scored
  `100.00`.
- The average is never presented as a count of problems or as a number of unique
  root causes.

### `DatasetScoreReport`

The complete scoring result attached to one successful validation run.

| Field | Type | Rules |
|---|---|---|
| `Scale` | fixed descriptor | States the 0-to-100 range and that higher is better, so the direction is unambiguous. |
| `Metrics` | six `MetricScore` | All six, in established category order. |
| `Weighting` | `ScoreWeighting` | The resolved weights and normalised shares. |
| `Dataset` | `DatasetScore` | The single average and its coverage. |

**Rules**:

- Exists only on a successful, reconciled run. A fatal outcome carries no score,
  so an incomplete run can never present one.
- Every one of the six metrics appears exactly once with exactly one state.
- Contains enough information — count, population, population kind, weight, and
  normalised share — to recalculate every score and the average from the report
  alone.

## Relationship to Existing Types

| Existing type | Use | Change |
|---|---|---|
| `DetailedSummary` | Supplies each metric's count via `For(category)`. | None. |
| `ScanCoverage` | Supplies accepted and examined populations. | None. |
| `CheckExecution` | Supplies applicability and the reason for `NotApplicable`. | None. |
| `DetailedValidationReport` | Gains one optional `Score` property. | Additive; `null` when scoring is not requested, so every existing consumer is unaffected. |
| `ValidationSummary` (v1) | Not used for scoring. | None; the v1 contract is untouched. |

## State Transitions

Scoring has no lifecycle of its own; it is computed once from a completed,
reconciled run and is thereafter immutable. The only decision per metric is the
one-time state assignment:

```text
check did not run ........................ NotApplicable (reason from the check)
check ran, population == 0 ............... NotScored     (zero-population reason)
check ran, population > 0, count > pop ... fatal         (internal inconsistency)
check ran, population > 0, count <= pop .. Scored        (score = 100 × (1 − count/pop))
```

The average is then available when at least one metric is `Scored` and the scored
weights sum above zero; otherwise it is unavailable with a reason.
