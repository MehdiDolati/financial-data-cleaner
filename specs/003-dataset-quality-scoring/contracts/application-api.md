# Application API Contract: Dataset Quality Scoring

The scoring capability is usable by any front end, not only the CLI. This
contract states what the Application layer exposes, what it requires, and what it
guarantees, without naming a serializer, console, or file system.

## Scope

Scoring is a pure derivation over one completed validation run. It performs no
I/O, opens no file, reads no environment variable, and starts no clock, so it
needs no new port.

## Inputs

Scoring requires only values a completed run already establishes:

| Input | Existing source | Use |
|---|---|---|
| Six category counts | `DetailedSummary.For(category)` | Numerator of each defect rate. |
| Accepted rows | `ScanCoverage.AcceptedRows` | Population for record-level metrics. |
| Examined rows | `ScanCoverage.PhysicalRowsExamined` | Population for the malformed-row metric. |
| Expected open-market candles | Counted in the existing expected-sequence walk | Population for time-based metrics. |
| Per-check status and reason | `CheckExecution` | Applicability and its explanation. |
| Weighting | Caller-supplied or default | Average only. |

The caller supplies the weighting; everything else comes from the run. No input
is re-derived from the dataset, and the dataset is not reopened.

## Requested Scoring

A caller opts in by supplying a scoring request alongside the existing validation
request. The request carries:

- whether scoring is requested at all, and
- the weighting to use: the default equal weighting, or a caller-supplied
  weighting covering all six metrics.

Weight validation is a pure function of the request and is therefore performed
before any dataset work begins. A rejected weighting yields a fatal
`INVALID_ARGUMENT` outcome and no report.

## Output

On a successful run with scoring requested, the existing report aggregate carries
one additional optional value, the complete `DatasetScoreReport` described in
[`../data-model.md`](../data-model.md). It is absent when scoring is not
requested.

The score section is complete when it is exposed: it contains all six metrics,
each with exactly one state, plus the resolved weighting and the single dataset
average with its coverage. A caller never has to compute, infer, or complete any
part of it.

## Guarantees

1. **Additive.** Scoring never alters a summary count, a finding, a finding's
   evidence, the finding order, the report's status, or the exit mapping.
2. **Read-only.** The source dataset is never modified, and no temporary artifact
   is created.
3. **No new pass.** No additional traversal of the dataset occurs; the only added
   work inside the scan is one increment per expected open-market slot in a walk
   that already happens.
4. **Exact.** Every score derives from integer counts and populations through
   exact rational arithmetic. `float` and `double` are absent. Rounding to two
   decimals happens once, at presentation.
5. **Deterministic.** Identical source bytes, identical validation configuration,
   and identical weighting produce an identical score section, with no
   wall-clock, locale, or random input.
6. **Recalculable.** The section exposes each metric's count, population,
   population kind, weight, and normalised share, so every score and the average
   can be recomputed from the output alone.
7. **Explicit absence.** A metric that cannot be scored carries a state and a
   reason, never a substituted value. An unavailable average carries a reason,
   never `0.00` or `100.00`.
8. **Fail safe.** A count exceeding its population is an internal inconsistency
   that fails the run as `REPORT_RECONCILIATION_FAILED`; a rate is never clamped.
9. **No score without a report.** A fatal outcome carries no score, so an
   incomplete run cannot present quality numbers.

## Layer Boundaries

| Concern | Owner |
|---|---|
| Exact rational arithmetic; two-decimal invariant formatting | Domain |
| Population resolution, applicability, weighting, average, section assembly | Application |
| Rendering the section as text or JSON | Infrastructure |
| Parsing `--score` / `--score-weights` and routing streams | CLI |

Application returns the score section as data. It never formats a report line,
never chooses a JSON member name, and never writes to a stream — which is what
allows a different front end to consume the same result and present it its own
way.

## Alternate Front End

Any host that can build the existing validation request can request scoring and
read the resulting section, because:

- the request carries only a flag and six numbers,
- the result is plain immutable data with no rendering attached, and
- every failure is a returned diagnostic rather than console output.

This is the test that scoring did not leak into the CLI: a library caller obtains
identical scores without the CLI present.
