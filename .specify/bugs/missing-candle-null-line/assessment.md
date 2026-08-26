# Bug Assessment: `line` is always null in findings for AUDCAD5.csv

- **Slug**: missing-candle-null-line
- **Created**: 2026-08-26
- **Source**: pasted text (local run artifacts `mamad.json`, `mamad2.json`)
- **Verdict**: invalid (not a bug — specified behavior)
- **Severity**: low (no defect; recorded as an enhancement opportunity)
- **Resolved as**: `specs/002-detailed-error-report` → User Story 5, "Jump Straight to Where an Absent Record Belongs" (FR-039, FR-040, SC-009, tasks T050–T059)

## Report (verbatim or summarized)

> I ran validate on AUDCAD5.csv and outcomes are in mamad.json and mamad2.json the field line is always null in findings

Two local report artifacts were supplied:

- `mamad.json` — v1 report. 61 findings, every one with `"line": null`.
- `mamad2.json` — v2 report (`"contractVersion": 2`). Same findings, every one with `"location": { "sourceLines": [] }`.

## Symptom

Every finding in both reports carries a null (v1) or empty (v2) source-line
reference. The reporter expected a line number identifying where in the CSV each
finding occurs. Observed behavior is that no line is emitted for any finding in
this particular run.

Expected behavior per contract: `line` is null precisely for
expected-but-absent timestamps, and populated only for findings anchored to a
physical row.

## Reproduction

1. Run the validator against `AUDCAD5.csv` with the M5 timeframe and the forex calendar.
2. Emit both the v1 (`mamad.json`) and v2 (`mamad2.json`) reports.
3. Observe `line: null` / `sourceLines: []` on all 61 findings.

Not a defect — the run is behaving as specified. See below.

## Suspected Code Paths

This is the intended path, not a faulty one:

- `mamad.json` summary — `missingCandles: 31`, `timeGaps: 30`, and
  `duplicateRecords: 0`, `invalidOhlc: 0`, `closedMarketRecords: 0`,
  `malformedRows: 0`. All 61 findings are `MissingCandle` (31) + `TimeGap` (30).
  The four categories that *do* carry a line number all have a count of zero, so
  there is no finding in this run that is entitled to a line.
- `src/Validator.Application/Validation/MissingCandleProcessor.cs:87` — builds the
  location as `new FindingLocation(Array.Empty<long>(), expectedUtc)` under the
  comment *"An expected-but-absent record has no physical line to cite."*
- `src/Validator.Application/Validation/TimeGapProcessor.cs:100` — same, under the
  comment *"A gap spans expected slots only; it cites no physical line."*
- `src/Validator.Application/Validation/ValidateMarketDataUseCase.cs:144-165`
  (`CreateSequenceFindings`) — constructs `MissingCandle` and `TimeGap` findings
  setting only `Timestamp`, deliberately leaving `Line` unset.
- `src/Validator.Infrastructure/Reporting/JsonReportWriter.cs:49` — passes
  `f.Line` straight through; the null originates upstream, not in rendering.

Categories that *do* populate a line, for contrast:
`Rules/InvalidOhlcRule.cs`, `Rules/ClosedMarketRecordRule.cs`,
`Rules/DuplicateRecordRule.cs`, and the malformed-row projection in
`ValidateMarketDataUseCase.cs:55` — each sets `Line` from
`PriceCandle.SourceLine` or `MalformedRow.LineNumber`.

## Root Cause Hypothesis

Not a bug. Confidence: **high**. Null is the contractually required value for
these two categories, stated in four independent places:

- `specs/001-ohlcv-data-quality-validator/contracts/validation-report.schema.json`
  — `line` is `oneOf [integer, null]`, described as *"Primary physical source
  line, or **null for expected-but-absent timestamps**."*
- `specs/001-ohlcv-data-quality-validator/data-model.md` — `SourceLines` is
  *"**Empty for expected-but-absent timestamps**; one line for row findings."*
- `specs/002-detailed-error-report/data-model.md` — *"**No line number is
  invented** for an expected-but-absent candle."*
- `specs/002-detailed-error-report/contracts/cli.md` — *"**no physical line is
  invented for a missing candle**."*

Feature 002's FR-016 makes it a hard rule: fields that do not apply *"MUST be
explicitly absent rather than populated with invented values."* A missing candle
is, by definition, a row that is not in the file — so there is no line number
that could honestly be reported for it.

## Proposed Remediation

**Preferred**: No code fix for the reported symptom. Closed as working-as-designed
and converted into an additive user story on feature 002 (see **Resolved as**
above). The SDD artifacts updated to carry it:

| Artifact | Change |
|---|---|
| `specs/002-detailed-error-report/spec.md` | Added clarification session 2026-08-26 (4 Q&A), User Story 5 (P3) with 5 acceptance scenarios, 3 edge cases, FR-039/FR-040, the `Absence Anchor` entity, SC-009, and an amended assumption |
| `specs/002-detailed-error-report/data-model.md` | Added `PreviousObservedSourceLine`/`NextObservedSourceLine` to `MissingCandleEvidence` and `TimeGapEvidence`; documented the tightest-bracket rule and that these lines never enter `FindingLocation.SourceLines` |
| `specs/002-detailed-error-report/contracts/detailed-report-v2.schema.json` | Added optional `previousObservedSourceLine`/`nextObservedSourceLine` (`positiveInteger`) to `missingCandleEvidence` and `timeGapEvidence` |
| `specs/002-detailed-error-report/contracts/cli.md` | Verbose text labels both bracketing lines; boundary side labelled `not applicable` |
| `specs/002-detailed-error-report/tasks.md` | Added Phase 6a (T050–T059), test-first pairs, dependencies, and updated totals |

The reporter's underlying need — *locate the reference quickly, programmatically
and manually* — is legitimate and only partially served today. Handle it as an
additive enhancement rather than a fix. Recommended shape: carry the **source
lines of the two bracketing observed records** alongside the bracketing
timestamps that the evidence already exposes.

`mamad2.json` already includes, for every missing candle and gap:

```json
"previousObservedTimestampUtc": "2026-07-09T21:55:00Z",
"nextObservedTimestampUtc":     "2026-07-09T22:05:00Z"
```

Those two neighbors are real rows that *do* have a `PriceCandle.SourceLine`. The
line numbers are available where the evidence is constructed and are simply not
propagated. Adding `previousObservedSourceLine` / `nextObservedSourceLine` beside
the existing timestamp pair gives an exact, verifiable anchor without inventing
anything: *"the gap sits between real line X and real line Y."*

**Alternatives**:

- *Populate `line` with the row the record "should have" occupied* — rejected. It
  overloads a documented contract field with a second, incompatible meaning. For
  every other category `line` means *"this row is the defect"*; here it would mean
  *"the defect is adjacent to this row."* Any existing consumer that jumps to
  `line` and highlights it would flag an innocent row. It also directly violates
  FR-016 and the four contract statements above, and would break v1 golden/
  byte-identical determinism tests.
- *Derive the position client-side by counting rows up to the timestamp* —
  rejected as the primary answer. It forces every consumer to re-implement
  ordering, duplicate, and out-of-order handling that the validator already did.
- *A dedicated `insertionAnchor` object* — viable, but redundant: the temporal
  half of the anchor already lives in the evidence. Extending the existing
  neighbor fields is tighter and keeps related data in one place.

**Files likely to change** (if the enhancement is pursued):

- `src/Validator.Domain/Findings/Evidence/MissingCandleEvidence.cs`
- `src/Validator.Domain/Findings/Evidence/TimeGapEvidence.cs`
- `src/Validator.Application/Validation/MissingCandleProcessor.cs`
- `src/Validator.Application/Validation/TimeGapProcessor.cs`
- `src/Validator.Application/Validation/DetailedValidationOrchestrator.cs`
- `src/Validator.Infrastructure/Reporting/DetailedReportV2Writer.cs`
- `src/Validator.Infrastructure/Reporting/VerboseReportWriter.cs`
- `specs/002-detailed-error-report/contracts/detailed-report-v2.schema.json`

**Tests to add or update**:

- Missing candle and gap evidence expose both neighbor source lines.
- A gap at the very start or end of the dataset omits the unavailable side
  explicitly rather than emitting `0` or a negative value.
- A temporally out-of-order source file still reports the true neighbor lines,
  even when `previous > next` numerically.
- A duplicated neighbor timestamp resolves to a documented, deterministic line
  (tightest bracket).
- Source lines beyond `Int32.MaxValue` survive as 64-bit values.
- v1 output stays byte-identical; summary counts, finding order, and exit codes
  are unchanged.

## Risks & Considerations

- **Do not route this through v1.** `ValidationFinding.Line` is `int?` and is
  populated via `checked((int)…)` casts. Feature 002 already fixes source lines
  as 64-bit `long`. The enhancement belongs in the v2 contract only; v1 is
  immutability-tested.
- **Unsorted input is the real design constraint.** Spec 001 FR-007 requires
  unsorted input to be accepted, and ordering is
  `OrderBy(Timestamp).ThenBy(SourceLine)` with an `ExternalMergeSort` behind it.
  For a temporally unsorted file there is no single "line where it should have
  appeared" — the physically adjacent row and the temporally adjacent row differ.
  Reporting two real neighbor lines degrades gracefully here; a single invented
  line number becomes meaningless.
- **Determinism and scoring are unaffected** as long as the change is purely
  additive: no count contributions change, so scores and exit codes hold.
- **Coverage gate.** Feature 005 just raised the merged Domain+Application gate to
  a true 100% line / 100% branch. New Domain/Application arms must be covered or
  explicitly justified-excluded, or the gate fails.
- Adding neighbor lines to gaps means bounded-memory streaming must be preserved;
  the anchor is two scalars per finding, so this is not a concern in practice.

## Open Questions

All three were resolved during the 2026-08-26 clarification session recorded in
`specs/002-detailed-error-report/spec.md`:

- **Anchor on gaps only, or on every missing candle?** → **Both.** A gap gives one
  anchor per contiguous run for manual inspection; each missing candle carries the
  same pair so per-candle programmatic backfill needs no second lookup. This run
  makes the case: 31 missing candles across 30 gaps.
- **Which line wins when a neighbour timestamp is duplicated?** → The **tightest
  bracket** — the highest line among rows sharing the preceding timestamp and the
  lowest line among rows sharing the following timestamp (FR-040).
- **Should verbose text render the anchor?** → **Yes**, labelled alongside the
  existing observed timestamps, with an unavailable side labelled `not applicable`
  (`contracts/cli.md`).

No open questions remain. Implementation begins at task T050.
