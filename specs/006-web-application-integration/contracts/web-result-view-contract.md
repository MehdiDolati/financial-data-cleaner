# Contract: Web Result View and Export

**Feature**: 006-web-application-integration | **Plan**: [../plan.md](../plan.md)

Defines what a completed run exposes to the website and to a machine consumer.
This contract answers FR-013 through FR-020 and is the authoritative definition of
the **substantive comparison surface** used to prove CLI↔web parity (SC-001,
SC-002, SC-004, SC-010).

## Two Surfaces, One Truth

| Surface | Consumer | Definition |
|---|---|---|
| `WebResultView` | The website's rendering layer | Typed projection of a terminal run; no markup, no prose-only fields |
| Export artifact | Machine consumers, downloads | Produced by the **existing** report writers at the **existing** versioned contracts |

Both surfaces project the same `DetailedValidationReport` /
`DatasetScoreReport` / `ComparisonReport`. Neither recomputes anything. Where they
differ is arrangement only — never value, never category, never count (FR-020).

## Reused Contracts (unchanged)

No new report schema is introduced. Export reuses, verbatim:

| Representation | Contract |
|---|---|
| Concise text | Existing six summary lines |
| Detailed text | Existing verbose text shape |
| JSON v1 | [`validation-report.schema.json`](../../001-ohlcv-data-quality-validator/contracts/validation-report.schema.json) — frozen |
| JSON v2 | [`detailed-report-v2.schema.json`](../../002-detailed-error-report/contracts/detailed-report-v2.schema.json) |
| Scoring (additive to v2) | [`scoring-v2.schema.json`](../../003-dataset-quality-scoring/contracts/scoring-v2.schema.json) + [amendment](../../003-dataset-quality-scoring/contracts/detailed-report-v2-amendment.md) |
| Fatal diagnostic | [`fatal-diagnostic-v2.schema.json`](../../002-detailed-error-report/contracts/fatal-diagnostic-v2.schema.json) |
| Benchmark / comparison | [benchmark-contract.md](../../004-benchmark-dataset-comparison/contracts/benchmark-contract.md), [comparison-report-contract.md](../../004-benchmark-dataset-comparison/contracts/comparison-report-contract.md) |

`contractVersion` semantics are untouched. A web consumer reading a v2 export gets
byte-equivalent substance to a CLI-produced v2 export for the same input.

## View Structure

```csharp
public sealed record WebResultView(
    WebRunId Id,
    WebRunOperation Operation,
    WebRunStatus Status,
    SourceIdentity Source,
    WebValidationSection? Validation,   // terminal success only
    WebScoringSection? Scoring,         // scoring requested only
    WebBenchmarkSection? Benchmark,     // establish / compare only
    WebComparisonSection? Comparison,   // compare only
    FatalDiagnostic? Diagnostic,        // Failed only
    IReadOnlyList<ReportRepresentation> AvailableExports);
```

**Structural invariants**:

1. `Diagnostic != null` ⇔ `Status == Failed`, and in that case `Validation`,
   `Scoring`, and `Comparison` are all `null` (FR-011, SC-003).
2. `AvailableExports` is empty unless `Status` is a terminal success (FR-014).
3. `Validation != null` ⇔ terminal success.
4. `Scoring != null` only when scoring was requested — an unrequested capability
   is never silently added (FR-005).

### `WebValidationSection`

Carries the established detailed-report fields (FR-013):

| Member | Existing source |
|---|---|
| `Context` | `ValidationContextSnapshot` |
| `Coverage` | `ScanCoverage` |
| `Checks` | `IReadOnlyList<CheckExecution>` — all six, canonical order |
| `Reconciliation` | `ReportReconciliation` |
| `Summary` | `DetailedSummary` — the six categories, never merged |
| `Findings` | `ICompletedFindingCatalog` — streamed, canonical order |

**Rules**:
- The six categories are exposed **separately**, with overlapping findings
  retained in both (spec US1 scenario 2). Categories are never merged and
  overlaps are never hidden.
- Findings stream through the existing catalog cursor. The view MUST NOT
  materialize all findings, so a very large report stays navigable and is never
  silently truncated (spec edge case).
- A finding's source lines, timestamps, and observed values are distinct typed
  members — not prose (FR-019, spec US2 scenario 3).
- Missing-candle ↔ time-gap relationships are preserved in **both** directions,
  so the host can offer navigation between related findings (spec US2 scenario 2).
- A clean result is identified as clean and never implies the dataset was modified
  (spec US1 scenario 3).

### `WebScoringSection`

Projects `DatasetScoreReport` (FR-015):

| Member | Rule |
|---|---|
| Six `MetricScore` entries | Each with state, count, population, population kind, resolved weight, normalized share |
| Average value | With covered-metric count |
| Excluded metrics | With the reason each was excluded |

**Rules**:
- `not applicable`, `not scored`, and `not available` are carried as explicit
  states with reasons. They MUST NOT be rendered or serialized as `0`, as `100`,
  or as an inferred value (FR-018, spec US3 scenario 1).
- Weights, coverage, and the documented average calculation are exposed so the
  average can be recomputed by hand (spec US3 scenario 2).
- Scoring never alters a validation count, a finding, the finding order, or the
  run status (FR-005, spec US3 scenario 4).

### `WebBenchmarkSection` and `WebComparisonSection`

| Member | Rule |
|---|---|
| Benchmark identity | Name, source identity, recorded context |
| Benchmark's recorded scores | **Separate** from the candidate's scores (FR-016) |
| Candidate's own quality score | Separate member |
| Benchmark-agreement score | Separate member; never conflated with the quality score (FR-016) |
| Matched / missing / extra records | Reported separately, never merged (FR-017) |
| Material discrepancies | Timestamp, field, both values, difference, resolved tolerance (FR-017) |
| Tolerated differences | Aggregate auditable evidence; **not** presented as material (spec US4 scenario 4) |
| Coverage / applicability | Explicit unavailable / incompatible / insufficient-coverage state (FR-018) |

**Rules**:
- Three score concepts stay three members: candidate quality, benchmark-agreement,
  and the benchmark's recorded scores. Collapsing any two is a contract violation.
- No-overlap or incompatible-context comparisons are marked unavailable or
  incompatible — never a perfect or misleading agreement score (FR-018, spec US4
  scenario 5).
- An established benchmark remains identifiable later by its exact source content
  and the validation context used at establishment (spec US4 scenario 6).
- A benchmark name already in use produces an explicit conflict, never a silent
  replacement (spec US4 scenario 2).

## Safe Rendering

- The view carries **typed data only**. It never contains markup, HTML entities,
  or pre-escaped strings; the host escapes at render time. This is feature 002's
  Application Invariant 5 extended to the web surface (FR-030).
- File names come from `SourceIdentity`, which already rejects path components and
  never exposes an absolute path.
- Data-derived text — file names, source values, finding messages — cannot alter
  page structure, report structure, or neighboring content, because it is never
  concatenated into a rendered document by Application code (FR-030, SC-008's
  sibling safety property).
- Numeric and date/time values are culture-invariant and UTC-normalized. Display
  localization and user-time-zone rendering are host presentation concerns and MUST
  NOT change any computed value (FR-025).

## Substantive Comparison Surface (normative)

This is the exact set compared by `Validator.Parity.Tests`. A CLI run and a web
run over identical bytes with equivalent resolved options MUST agree on **all** of:

**Validation**
- Report status (clean vs. findings-detected)
- All six category counts
- Scan coverage figures
- All six check statuses, in canonical order
- Reconciliation outcome
- The complete finding sequence in canonical order, including every finding's
  category, location, source lines, timestamps, observed values, evidence records,
  and both relationship directions

**Scoring** (when requested)
- Each metric's state, count, population, population kind, score, resolved weight,
  normalized share
- The average value, its covered-metric count, and the excluded-metric list

**Benchmark / comparison** (when requested)
- Benchmark identity and recorded scores
- Matched / missing / extra counts and their record sets
- Every material discrepancy with timestamp, field, both values, difference, and
  resolved tolerance
- Tolerated-difference aggregates
- Benchmark-agreement score and comparison coverage/applicability state

**Failure**
- Diagnostic code, failure class, stage, and the absence of any partial count,
  score, or report reference

### Explicitly Outside the Surface

These may differ freely between front ends and MUST NOT fail a parity test:

- Human-readable labels, wording, ordering of visual sections, and layout
- Progress and pending-state reporting
- Audit timestamps (`SubmittedAtUtc`, `TerminalAtUtc`)
- `WebRunId` itself (the CLI has no run id)
- Exit codes vs. lifecycle states (they encode the same outcome differently)
- Display-locale and user-time-zone formatting

The spec's own assumption authorizes this split: web labels may be arranged
differently, but the underlying meanings, values, categories, and machine-readable
fields remain compatible.

## Export Rules

1. Export uses the existing `ISuccessReportWriter` / `IFatalDiagnosticWriter`
   implementations. No new serializer, field, or version is introduced (FR-014).
2. The export contains the same substantive information as the displayed view
   (FR-014, spec US2 scenario 4).
3. Export streams UTF-8 without a BOM and never materializes the whole report as a
   string, so a very large result is never silently truncated.
4. Export is available only for a terminal success. An incomplete or fatal run is
   never offered as a complete report (spec US2 scenario 5).
5. Exported source-derived values are escaped by the writer, exactly as they are
   for CLI output — the same code path, therefore the same guarantees.
6. Reading an export requires no page-text parsing to recover any required field
   (FR-019, SC-002).
