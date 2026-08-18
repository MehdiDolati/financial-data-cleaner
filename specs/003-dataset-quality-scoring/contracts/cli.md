# Command-Line Contract: Dataset Quality Scoring

This contract extends the feature-001 and feature-002 commands without changing
any existing default, count, finding, or exit code.

## Command Shape

```text
validator <input-file> [existing-options] [--score] [--score-weights <list>]
```

All existing input, market, calendar, timeframe, format, version, output, and
exit-code rules continue to apply unchanged.

## Options

| Option | Description | Default |
|---|---|---|
| `--score` | Report per-metric scores and one dataset average | Off |
| `--score-weights <list>` | Override the average's weighting; requires `--score` | Equal weights |

`--score` is opt-in. Without it, output is byte-identical to a run of the same
command before this feature existed.

## Weight Syntax

```text
--score-weights missingCandles=2,duplicateRecords=1,invalidOhlc=3,closedMarketRecords=1,timeGaps=2,malformedRows=1
```

- One comma-separated list of `metric=weight` pairs.
- Metric names are exactly the six names used in the v2 JSON summary:
  `missingCandles`, `duplicateRecords`, `invalidOhlc`, `closedMarketRecords`,
  `timeGaps`, `malformedRows`.
- All six metrics MUST be present. A missing metric is not defaulted.
- Weights are non-negative decimal numbers in invariant form. `0` is accepted.
- Surrounding whitespace around a name or value is tolerated; the value itself
  must not use exponent notation, thousands separators, or a leading `+`.

### Rejected Weight Input

Each of the following fails before the dataset is read, produces no report, exits
`2`, and states both the specific problem and the accepted form:

| Input problem | Example |
|---|---|
| Omits a metric | five pairs supplied |
| Unknown metric name | `invalidOHLC=1` |
| Duplicate metric name | `timeGaps=1,timeGaps=2` |
| Negative weight | `timeGaps=-1` |
| Non-numeric weight | `timeGaps=high` |
| Unparseable input | `timeGaps`, `=1`, `timeGaps=1;malformedRows=1` |
| All weights zero | every pair `=0` |
| Used without `--score` | `--score-weights ...` alone |

## Representation Selection

| Options | Scoring output |
|---|---|
| `--score` | Scoring section appended to human-readable text. |
| `--score --verbose` | Scoring section appended to detailed text. |
| `--score --format json --report-version 2` | `scoring` object in the v2 document. |
| `--score --format json` | Rejected: v1 conflict. |
| `--score --format json --report-version 1` | Rejected: v1 conflict. |

### V1 Conflict

Requesting scores with the frozen v1 JSON contract fails fast as a configuration
conflict, before the dataset is read, with exit code `2` and a message naming the
option needed to obtain scores:

```text
Option '--score' is not available with the version 1 JSON contract. Use '--format json --report-version 2' to obtain scores.
```

Scoring is never silently ignored.

## Text Contract

A scored text run begins with the six established summary lines, unchanged in
content, order, and format, then emits one labelled scoring section:

```text
Missing candles: 2
Duplicate records: 1
Invalid OHLC: 1
Closed-market records: 0
Time gaps: 2
Malformed rows: 0

Quality scores (0-100, higher is better):
- Missing candles: 97.62 (count=2; population=84 expected candles; weight=1; share=0.25)
- Duplicate records: 98.00 (count=1; population=50 accepted rows; weight=1; share=0.25)
- Invalid OHLC: 98.00 (count=1; population=50 accepted rows; weight=1; share=0.25)
- Closed-market records: 100.00 (count=0; population=50 accepted rows; weight=1; share=0.25)
- Time gaps: not scored (population=0 expected candles; reason: ...)
- Malformed rows: not applicable (reason: ...)
Dataset average: 98.41 (covers 4 of 6 metrics; excluded: Time gaps, Malformed rows)
```

- Every metric appears in the established category order with exactly one state:
  a score, `not applicable`, or `not scored`, each carrying its reason when it
  carries no value.
- Each scored line states its count, population, population kind, resolved
  weight, and normalised share, so the score can be checked by hand.
- The average states how many metrics it covers and which were excluded.
- When no average is available, the line states it explicitly with the reason and
  shows no number:

  ```text
  Dataset average: not available (reason: no metric could be scored)
  ```

- Scores are never labelled as a count of problems or as unique root causes.

## JSON Contracts

### V1

Unchanged. `--score` is rejected with v1 rather than adding, removing, or
altering any v1 field.

### V2

`--score --format json --report-version 2` adds one optional top-level `scoring`
object conforming to [`scoring-v2.schema.json`](scoring-v2.schema.json). The
property is absent when scoring is not requested, and `contractVersion` remains
`2`. The delta is recorded in
[`detailed-report-v2-amendment.md`](detailed-report-v2-amendment.md).

Every score, count, population, population kind, state, reason, resolved weight,
normalised weight, the average, its metric coverage, its excluded metrics, and
its unavailability reason are separate documented fields, so no consumer parses
human-readable text.

## Fatal Behaviour

- A fatal run emits no score. The diagnostic makes clear that scoring did not
  occur, and no partial scoring output appears on any stream.
- Invalid weights and the v1 conflict are `INVALID_ARGUMENT`
  (Configuration/ArgumentValidation) and are raised before the source is opened.
- A count exceeding its population is an internal inconsistency reported as
  `REPORT_RECONCILIATION_FAILED` (Operational/Reconciliation). The rate is never
  clamped into range.
- No new fatal code or stage is introduced, so the v2 fatal contract's closed
  enumerations are unchanged.

## Exit Codes

| Code | Meaning |
|---:|---|
| `0` | Help requested, or successful `Clean` report. |
| `1` | Successful `FindingsDetected` report. |
| `2` | Fatal failure, including invalid weights and the v1 scoring conflict. |

A score never changes the exit code. A dataset with a low average and no findings
still exits `0`; a dataset with a high average and findings still exits `1`.

## Determinism and Safety

- Repeated runs over identical source bytes with an identical validation and
  weighting configuration produce byte-identical scoring output, including
  formatting.
- Scores are rendered to exactly two decimal places, culture-invariant, with
  half-away-from-zero rounding, and contain no wall-clock or locale-dependent
  value.
- The source dataset is never modified, repaired, reordered, or overwritten.
- The six summary counts, the findings, the finding order, and the exit code are
  identical to the same run without `--score`.
