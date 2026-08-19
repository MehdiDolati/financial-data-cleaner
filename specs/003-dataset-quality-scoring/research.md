# Research: Dataset Quality Scoring

The specification arrived fully clarified (six clarification answers recorded in
`spec.md`), so no functional NEEDS CLARIFICATION marker remained. The open
questions were technical: how to compute exactly, where each population comes
from, how to surface scores without breaking two frozen contracts, and how to
fail safely. Each decision below was validated against the existing code base.

## R1. Exact score arithmetic

**Decision**: Represent every intermediate value as an exact rational
`ExactRatio` (numerator/denominator over `BigInteger`, normalised sign, reduced by
GCD) in `Validator.Domain/Scoring`. A metric score is the exact ratio
`100 × (population − count) / population`. The average is the exact ratio
`Σ(scoreᵢ × weightᵢ) / Σ(weightᵢ)` built from unrounded metric ratios. Rounding to
two decimals, half away from zero, happens once at the presentation boundary.

**Rationale**: The constitution bans `float`/`double` for any reported value, and
FR-010/FR-011 require exactness, no accumulated drift, and an average computed
from unrounded metric scores. `decimal` alone is insufficient: `100 × (1 − 1/3)`
is not representable, so a `decimal` average of `decimal` quotients can disagree
with a hand recalculation from the printed counts and weights, breaking SC-002.
Rationals keep every division symbolic until the single final rounding.
`BigInteger` also removes any overflow concern when weights multiply 64-bit
populations, satisfying the large-count edge case. Rounding half away from zero
matches `MidpointRounding.AwayFromZero` and is stated in FR-011; scores are
non-negative, so the "away from zero" direction is always upward at a midpoint.

**Alternatives considered**: `double` — rejected outright by the constitution and
non-reproducible. `decimal` throughout — rejected for the drift and
hand-recalculation mismatch above. Scaled integer arithmetic in fixed
hundredths — rejected because the average of already-truncated hundredths
violates FR-011's "computed from unrounded metric scores".

## R2. Where each population comes from

**Decision**: Accepted rows and examined rows come from the existing
`ScanCoverage` (`AcceptedRows`, `PhysicalRowsExamined`), which the run already
establishes and reconciles. Expected candles are counted inside the existing
sequence walk in `DetailedValidationOrchestrator.RunSequenceChecksAsync`: the loop
`for (var expected = first; expected <= last; expected += timeframe.Duration)`
already visits every candidate slot and already calls `calendar.IsOpen(expected)`,
so the count is one increment on the open-market branch. It is returned alongside
the existing `(Checks, Summary)` tuple and is `null` when the sequence checks did
not run.

**Rationale**: FR-004 forbids a new check or a re-scan, and FR-007 fixes each
denominator. `ScanCoverage` already carries the row populations with the exact
established meanings the spec's assumptions rely on (`examined = accepted +
malformed`). The expected-candle count is the only value not already retained, and
counting it in the existing walk is both free and guaranteed consistent with the
missing-candle count derived from the same loop — a separately recomputed sequence
could disagree with it, which is exactly the internal inconsistency FR-009 treats
as fatal.

**Alternatives considered**: Recomputing the expected sequence in a scoring
service — rejected as a second source of truth that can drift from the check that
produced the counts. Deriving expected candles arithmetically from the date range
and timeframe — rejected because it ignores the market calendar and would
overstate the population for any closed period. Approximating the population with
accepted rows — rejected because it contradicts the recorded clarification that
time-based metrics are measured against expected candles.

## R3. Applicability and zero-population states

**Decision**: Model three mutually exclusive states — `Scored`,
`NotApplicable`, `NotScored` — where the latter two always carry a reason.
`NotApplicable` is driven by the existing `CheckExecution.Status ==
CheckStatus.NotApplicable` and reuses that check's existing `Reason` string.
`NotScored` is assigned when the check completed but the population is zero.

**Rationale**: FR-012 and FR-013 are different causes that must not be conflated,
and FR-014/FR-015 require the state and its reason to be visible rather than
inferred from a missing value. The orchestrator already produces exactly the
signal needed: sequence checks are marked `NotApplicable` with the reason
"Fewer than two open-market timestamps bound an expected sequence." The
crypto/always-open case in the spec's edge cases falls out of the same status
field, so no new applicability concept is invented. Keeping the reason string
sourced from the check avoids two divergent explanations of the same fact.

**Alternatives considered**: A nullable score with no state — rejected because it
forces the reader to infer exclusion, which FR-014 explicitly prohibits. Treating
a zero population as a perfect score — rejected by FR-013 and SC-003. A single
"unscored" state covering both causes — rejected because the reasons and the
underlying conditions differ and the report must distinguish them.

## R4. Reporting surface for human-readable text

**Decision**: Emit the scoring section after the six summary lines on both text
paths. Because populations and check statuses only exist on the detailed pipeline,
a scored text run is routed through `DetailedValidationOrchestrator` — the same
routing rule already used for `--verbose` — and the six leading lines continue to
be produced from one shared label list so the concise and verbose renderings
cannot drift.

**Rationale**: FR-028 fixes the position after the six established lines and
requires those lines to remain unchanged in content, order, and format. The CLI
already routes `--verbose` text through the detailed pipeline
(`parsed.ReportVersion == 2 || (parsed.Verbose && parsed.Format ==
ReportFormat.Text)`), so extending that condition with `parsed.Score` reuses a
proven path instead of teaching the v1 use case about populations it does not
have. `VerboseReportWriter.AppendSummaryLines` and `TextReportWriter` currently
duplicate the same six labels; centralising them is what makes the "byte-identical
first six lines" guarantee in SC-006 testable rather than aspirational.

**Alternatives considered**: Computing scores in the v1 `ValidateMarketDataUseCase`
— rejected because `ValidationSummary` exposes no expected-candle population or
per-check applicability, so metrics would have to be silently credited or
recomputed. A separate scoring command or file — rejected as out of scope and
contrary to FR-028. Placing scores before the summary lines — rejected by FR-028.

## R5. Weight override input format

**Decision**: `--score-weights` takes one comma-separated list of
`metric=weight` pairs using the six lower-camel metric names already used in the
v2 JSON summary (`missingCandles`, `duplicateRecords`, `invalidOhlc`,
`closedMarketRecords`, `timeGaps`, `malformedRows`). Values are parsed with
`decimal.TryParse` under `NumberStyles.AllowDecimalPoint` and
`CultureInfo.InvariantCulture`. Parsing and full validation run during argument
parsing, before the source is opened.

**Rationale**: FR-022 and FR-024 require all six metrics explicitly and demand
rejection of unknown names, duplicates, omissions, negatives, non-numerics,
unparseable input, and all-zero weights before any dataset content is read.
Reusing the existing v2 field names means the option, the report echo, and the
JSON contract all name metrics identically, which is what lets a user
recalculate the average from the report alone (FR-025). Invariant parsing
satisfies the constitution's culture-invariance rule; rejecting a leading `+`,
exponent notation, and thousands separators keeps the accepted form narrow and
its diagnostic precise.

**Alternatives considered**: Six separate options such as `--weight-invalid-ohlc`
— rejected as six new options for one concept, and it makes "omits a metric"
harder to diagnose as a single actionable message. A JSON weights file — rejected
as new file I/O and a new failure mode for six numbers. Positional weights —
rejected because a silent order mistake would misweight the average with no
diagnostic.

## R6. Failure routing for scoring problems

**Decision**: Reuse existing fatal codes rather than adding any. Invalid weights
and the scoring-with-v1 conflict are `INVALID_ARGUMENT`
(Configuration/ArgumentValidation), raised during argument parsing. An impossible
defect rate (count > population, or a negative input) is
`REPORT_RECONCILIATION_FAILED` (Operational/Reconciliation).

**Rationale**: The v2 fatal contract's `code` and `stage` enumerations are closed
(`additionalProperties: false`, fixed `enum` lists) and `FatalCodeRegistry` fixes
each code's class and stage. Adding a code would amend a frozen contract for no
new consumer benefit, violating Principle VII. The mapping is also semantically
right: a bad weight or an incompatible option combination is a configuration
error found at argument validation, while a count exceeding its population is
precisely a reconciliation failure — the same class of internal inconsistency
`ReconciliationValidator` already reports when a category count disagrees with its
contribution sum. FR-009 demands failure rather than clamping, and this route
already guarantees no report is committed. FR-005 is satisfied for free: a fatal
outcome never constructs a report, so no score can exist on one.

**Alternatives considered**: New `INVALID_SCORE_WEIGHTS` and `SCORE_*` codes —
rejected for amending a frozen closed enum unnecessarily. Clamping an
out-of-range rate into 0..1 — rejected explicitly by FR-009 and Principle V.
Warning and continuing without an average — rejected because an internal
inconsistency must stop the run, not degrade quietly.

## R7. Machine-readable contract shape

**Decision**: Add one optional top-level `scoring` object to the existing v2
report schema, published as `contracts/scoring-v2.schema.json` with the delta
recorded in `contracts/detailed-report-v2-amendment.md`. The property is absent
when scoring is not requested and `contractVersion` stays `2`. JSON v1 is
untouched, and `--score` with v1 is a configuration conflict.

**Rationale**: FR-029 requires the scores under the versioned v2 contract, FR-030
freezes v1, and FR-031 makes the v1 combination fail fast. The existing v2 schema
sets `additionalProperties: false`, so `scoring` must be declared to be legal —
adding it as optional is backward compatible because every existing document
stays valid and every existing consumer ignores a field it does not read. Keeping
`contractVersion` at `2` is correct precisely because the addition is optional and
additive; bumping to 3 would strand the existing consumers the amendment is
designed not to disturb. Publishing the delta separately keeps the diff auditable
against feature 002's schema.

**Alternatives considered**: A `contractVersion` bump to 3 — rejected as a
breaking signal for an additive optional field. A sibling JSON document or
separate `--score-output` file — rejected as a second contract to version and
commit atomically. Nesting scores inside `summary` — rejected because `summary`
is the frozen set of six counts and FR-003 forbids altering it.

## R8. Proving the unscored path is unchanged

**Decision**: Pin SC-006 with two explicit process-level tests: an unscored run's
stdout is byte-identical to the recorded golden output, and a scored run's first
six lines, findings, finding order, and exit code are byte-identical to the same
run without `--score`. Determinism is proven by repeating a scored run and
comparing bytes, extending the existing repeatability suite.

**Rationale**: FR-002, FR-003, and SC-006 are regression guarantees, and the
repository already holds the tooling to assert them — `ReportCompatibilityTests`
for v1 immutability, `DeterminismTests`/`RepeatabilityTests` for byte equality.
Diffing a scored run against its unscored twin is what makes "additive" a
verified property rather than a claim, and it is the only way to catch an
accidental format change to the six shared summary lines introduced by the
centralisation in R4.

**Alternatives considered**: Asserting only the six counts numerically — rejected
because it would miss a whitespace or ordering change that still breaks a
downstream text parser. Trusting the shared label list without a test — rejected
because SC-006 demands byte-level evidence.
