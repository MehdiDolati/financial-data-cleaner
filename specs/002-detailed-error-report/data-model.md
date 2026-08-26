-# Data Model: Detailed Dataset Error Report

## Modeling Conventions

- Domain and Application values are immutable records or value objects.
- Counts, byte sizes, physical line numbers, and count contributions use
  non-negative `long`; physical lines and contributions are positive when set.
- Price and volume evidence uses `decimal`; elapsed time uses integral seconds.
- All normalized timestamps are UTC instants and serialize with a trailing `Z`.
- Public references and codes are invariant ASCII and stable for identical input
  bytes and resolved validation context.
- Source-derived strings are retained as data, never interpreted as report
  markup. Rendering adapters are responsible for structural escaping.
- Findings use the established category order, then UTC timestamp (null last),
  first source line (absent last), and deterministic reference.
- Collections that can grow with source size are replayable sequences, not
  materialized lists.

## Outcome Types

### `ReportStatus`

Enumeration: `Clean`, `FindingsDetected`.

`Fatal` is deliberately not a value on a successful report. It is represented by
the separate `FatalDiagnostic` aggregate, preventing a partial run from carrying
fields that imply complete quality totals.

### `DetailedValidationOutcome`

A discriminated Application result:

```text
DetailedValidationOutcome
  |- Succeeded(DetailedValidationReport)
  `- Failed(FatalDiagnostic)
```

Only `Succeeded` may be passed to a successful report writer. CLI exit mapping is
`Clean -> 0`, `FindingsDetected -> 1`, and `Failed -> 2`.

## Source and Run Context

### `SourceIdentity`

| Field | Type | Rules |
|---|---|---|
| `FileName` | string | Non-empty safe base name; no absolute path. |
| `ByteSize` | `long` | Non-negative exact source byte length. |
| `Sha256` | string | Exactly 64 lower-case hexadecimal characters over the exact input bytes. |

The fingerprint and byte size are captured from the same readable source handle
used to prepare validation data, avoiding identity drift if a file changes
between independent opens. A detected mid-read source change is fatal.

### `TimestampInterpretation`

| Field | Type | Rules |
|---|---|---|
| `Mode` | enum | `SeparateDateTime` or `CombinedTimestamp`. |
| `DateFormat` | string? | Resolved exact format in separate mode. |
| `TimeFormat` | string? | Resolved exact format in separate mode. |
| `TimestampFormat` | string? | Resolved exact format in combined mode. |
| `TimestampColumn` | string? | Resolved header name or one-based index in combined mode. |
| `SourceOffset` | string | Canonical fixed `+HH:mm` or `-HH:mm`. |

Exactly the fields relevant to `Mode` are populated.

### `CalendarContext`

| Field | Type | Rules |
|---|---|---|
| `Profile` | string | `forex`, `equities`, `crypto`, or `custom`. |
| `Name` | string | Stable resolved calendar name. |
| `TimeZone` | string? | IANA identifier when local sessions are used. |
| `DefinitionSha256` | string? | Present when a calendar file supplied the definition. |
| `Sessions` | replayable sequence | Resolved weekly session definitions; may be empty for built-in forex/crypto. |

### `ValidationContextSnapshot`

| Field | Type | Rules |
|---|---|---|
| `Timeframe` | string | Resolved canonical `M<n>`, `H<n>`, or `D<n>`. |
| `Calendar` | `CalendarContext` | Required. |
| `Timestamp` | `TimestampInterpretation` | Required. |
| `Delimiter` | string | Resolved `comma`, `semicolon`, or `tab`. |
| `HasHeader` | bool | Resolved input mode. |
| `DateRange` | UTC range? | Null when no timestamped record was accepted. |

No field depends on host locale, absolute path, current time, or environment.

## Scan Coverage and Checks

### `ScanCoverage`

| Field | Type | Invariant |
|---|---|---|
| `PhysicalRowsExamined` | `long` | Every physical data record, excluding an optional header. |
| `AcceptedRows` | `long` | Rows normalized to `PriceCandle`. |
| `MalformedRows` | `long` | Structurally readable rows excluded due to field conversion errors. |

`PhysicalRowsExamined == AcceptedRows + MalformedRows` is mandatory. A failure to
establish this equality is fatal and no successful report may be rendered.

### `CheckName`

Stable values in canonical order:

1. `MissingCandles`
2. `DuplicateRecords`
3. `InvalidOhlc`
4. `ClosedMarketRecords`
5. `TimeGaps`
6. `MalformedRows`

### `CheckStatus`

Enumeration: `Completed`, `NotApplicable`, `NotCompleted`.

### `CheckExecution`

| Field | Type | Rules |
|---|---|---|
| `Check` | `CheckName` | Exactly one entry for every established check. |
| `Status` | `CheckStatus` | Required. |
| `Reason` | string? | Required for `NotApplicable` and `NotCompleted`; absent for `Completed`. |

A successful report has no `NotCompleted` entry. `NotApplicable` is valid only
when the resolved input does not provide a meaningful evaluation domain, such as
missing-candle and time-gap checks with fewer than two bounding open-market
timestamps. A fatal diagnostic identifies every check not completed.

## Summary and Reconciliation

### `CategoryReconciliation`

| Field | Type | Rules |
|---|---|---|
| `Category` | established category | Required. |
| `SummaryCount` | `long` | Non-negative established category count. |
| `EntryCount` | `long` | Number of detailed entries in this category. |
| `ContributionSum` | `long` | Sum of every entry's positive `CountContribution`. |

`SummaryCount == ContributionSum` for every category. `EntryCount` may differ
from `SummaryCount`: a duplicate group can contribute `n - 1`, and each time gap
contributes one regardless of its missing-candle count.

### `ReportReconciliation`

Contains six `CategoryReconciliation` entries and the `ScanCoverage` equality.
It exposes no value named `totalErrors` or `uniqueProblems`. A representation may
show `CategoryCountSum`, but must label it as the arithmetic sum of overlapping
category counts.

## Finding Model

### `FindingReference`

A non-empty stable ASCII string unique within one report. Canonical prefixes:

| Category | Identity input | Example shape |
|---|---|---|
| Missing candle | expected UTC timestamp | `missing-candle:20240801T1000000000000Z` |
| Duplicate record | shared UTC timestamp + lowest line | `duplicate-record:...:line-42` |
| Invalid OHLC | physical line | `invalid-ohlc:line-73` |
| Closed-market record | physical line | `closed-market-record:line-91` |
| Time gap | first and last missing UTC timestamps | `time-gap:...:...` |
| Malformed row | physical line | `malformed-row:line-108` |

When full canonical identity inputs collide, append a one-based collision ordinal
derived from canonical source order. Public references never use random values.

### `FindingLocation`

| Field | Type | Rules |
|---|---|---|
| `SourceLines` | replayable ordered sequence of `long` | Empty for absent expected records; otherwise all applicable physical lines. |
| `TimestampUtc` | UTC instant? | Normalized observed or expected timestamp when known. |
| `OriginalTimestampText` | string? | Present only when recovered from a source row. |

No line number is invented for an expected-but-absent candle. Source lines may
exceed 32-bit range. An absence is instead located through the bracketing
observed source lines carried in `MissingCandleEvidence` and `TimeGapEvidence`
(FR-039); those lines belong to real neighbouring rows and never enter this
`SourceLines` sequence, which continues to describe only the finding's own
physical rows.

### `FindingRelationship`

| Field | Type | Rules |
|---|---|---|
| `Kind` | string | v1 value `PartOfGap` or `ContainsMissingCandle`. |
| `TargetReference` | `FindingReference` | Existing related finding. |

For every missing-candle/time-gap link, both directional edges are produced in
the same append transaction. Relationships are streamed from a normalized spool.

### `DetailedFinding`

| Field | Type | Rules |
|---|---|---|
| `Reference` | `FindingReference` | Unique and deterministic. |
| `Category` | established `FindingCategory` | Required. |
| `Title` | string | Non-empty concise invariant English. |
| `Explanation` | string | Non-empty plain-language reason. |
| `CountContribution` | `long` | Positive. |
| `Location` | `FindingLocation` | Required. |
| `EvidenceKind` | enum | Must correspond to `Category`. |
| `SuggestedAction` | string | Non-empty advisory action; never automatic repair. |
| `Relationships` | replayable sequence | Empty when none apply. |

Evidence is stored separately and joined by `Reference` while reading. This keeps
one duplicate group or gap from creating an unbounded object in memory.

## Category Evidence

### `MissingCandleEvidence`

| Field | Type |
|---|---|
| `ExpectedTimestampUtc` | UTC instant |
| `ExpectedTimeframe` | timeframe code |
| `TimeGapReference` | `FindingReference` |
| `PreviousObservedTimestampUtc` | UTC instant? |
| `NextObservedTimestampUtc` | UTC instant? |
| `PreviousObservedSourceLine` | positive `long`? |
| `NextObservedSourceLine` | positive `long`? |

Each bracketing source line is present exactly when its paired observed
timestamp is present, and is the physical line of that observed record. Every
missing candle in one gap carries the same pair as its owning gap. A boundary gap
leaves the unavailable side absent rather than zero or negative (FR-040).

### `TimeGapEvidence`

| Field | Type |
|---|---|
| `FirstMissingTimestampUtc` | UTC instant |
| `LastMissingTimestampUtc` | UTC instant |
| `ExpectedTimeframe` | timeframe code |
| `MissingCandleCount` | positive `long` |
| `ElapsedSeconds` | positive `long` |
| `PreviousObservedTimestampUtc` | UTC instant? |
| `NextObservedTimestampUtc` | UTC instant? |
| `PreviousObservedSourceLine` | positive `long`? |
| `NextObservedSourceLine` | positive `long`? |
| `MissingCandleReferences` | replayable ordered sequence |

`MissingCandleCount` equals the number of child references and the number of
related `MissingCandle` entries, without changing the gap's own contribution of
one.

When a bracketing timestamp occurs on several physical rows, the line resolves to
the tightest bracket: the highest line sharing the preceding timestamp and the
lowest line sharing the following timestamp. Because unsorted input is accepted,
the two lines are not required to be consecutive or ascending — they identify the
temporal neighbours, not the physically adjacent rows.

### `DuplicateRecordEvidence`

| Field | Type |
|---|---|
| `SharedTimestampUtc` | UTC instant |
| `Classification` | `Exact` or `Conflicting` |
| `DifferingFields` | replayable ordered sequence of OHLCV field names |
| `Rows` | replayable ordered sequence of `DuplicateRowEvidence` |

`DuplicateRowEvidence` contains `SourceLine`, original timestamp text when
available, and decimal `Open`, `High`, `Low`, `Close`, and `Volume`. Every group
row is preserved. `CountContribution == row count - 1`.

### `InvalidOhlcEvidence`

Contains physical source line, observed decimal Open/High/Low/Close/Volume, and
a non-empty replayable sequence of every violated stable rule code. Stable rule
codes include `HIGH_BELOW_OPEN`, `HIGH_BELOW_CLOSE`, `HIGH_BELOW_LOW`,
`LOW_ABOVE_OPEN`, `LOW_ABOVE_CLOSE`, `LOW_ABOVE_HIGH`, `NON_POSITIVE_OPEN`,
`NON_POSITIVE_HIGH`, `NON_POSITIVE_LOW`, `NON_POSITIVE_CLOSE`, and
`NEGATIVE_VOLUME`. The finding contribution is one regardless of violation count.

### `ClosedMarketRecordEvidence`

Contains source line, observed UTC timestamp, selected profile/calendar name,
calendar time zone when relevant, and a non-empty closed-rule description or the
concrete UTC `[close, next-open)` boundary that classified the row.

### `MalformedRowEvidence`

| Field | Type |
|---|---|
| `SourceLine` | positive `long` |
| `ParsedTimestampUtc` | UTC instant? |
| `OriginalTimestampText` | string? |
| `ExpectedSlotReserved` | bool |
| `FieldErrors` | replayable non-empty sequence |
| `ChecksNotApplied` | replayable non-empty sequence of `CheckName` |

Each `MalformedFieldError` carries the field name or physical column, original
offending value, and stable reason code plus explanation. All independently
detectable field errors are recorded; the row still contributes exactly one.

## Successful Report Aggregate

### `DetailedValidationReport`

| Field | Type | Rules |
|---|---|---|
| `ContractVersion` | integer | Exactly `2` in JSON v2. |
| `Status` | `ReportStatus` | Derived from six summary counts. |
| `FindingSetComplete` | bool | Always `true`. |
| `Source` | `SourceIdentity` | Required. |
| `Context` | `ValidationContextSnapshot` | Required. |
| `Coverage` | `ScanCoverage` | Reconciled. |
| `Checks` | six `CheckExecution` entries | None `NotCompleted`. |
| `Summary` | existing six counts | Meanings unchanged from feature 001. |
| `Reconciliation` | `ReportReconciliation` | Required and valid. |
| `Findings` | replayable canonical sequence | Complete and untruncated. |

`Status == Clean` exactly when all six counts are zero; otherwise it is
`FindingsDetected`. A report cannot exist until the finding catalog is completed
and all invariants pass.

## Fatal Diagnostic Aggregate

### `FailureClass`

Enumeration: `Dataset`, `Configuration`, `Operational`.

### `FailureStage`

Stable values: `ArgumentValidation`, `SourceIdentity`, `Ingestion`,
`TimeframeResolution`, `Validation`, `Reconciliation`, `ReportRendering`, and
`ReportCommit`.

### `FatalDiagnostic`

| Field | Type | Rules |
|---|---|---|
| `ContractVersion` | integer | Exactly `2` in structured v2 output. |
| `Status` | string | Exactly `Fatal`. |
| `FindingSetComplete` | bool | Always `false`. |
| `Code` | string | Stable upper-snake-case failure code. |
| `FailureClass` | `FailureClass` | Required. |
| `Stage` | `FailureStage` | Required. |
| `Reason` | string | Actionable invariant English; no exception type/path leakage. |
| `Guidance` | string | Non-empty corrective next action. |
| `Source` | partial `SourceIdentity`? | Only trustworthy fields already established. |
| `Location` | line/timestamp/field? | Present only when known. |
| `Checks` | six `CheckExecution` entries | Unfinished checks are `NotCompleted`. |

Fatal diagnostics contain no final summary, reconciliation, `isClean`, or
complete findings array. Optional pre-failure observations, if later added, must
be explicitly labeled non-exhaustive and are outside the v2 base schema.

## Replay and Lifecycle

```text
Created
  -> CapturingSourceIdentity
  -> Ingesting
  -> ResolvingContext
  -> ValidatingToSpools
  -> CompletingCatalog
       |- invariant failure -> FatalDiagnostic
       `- reconciled -> ReportReady
  -> RenderingToStage
       |- render failure -> FatalDiagnostic
       `- complete -> Committing
             |- commit failure -> FatalDiagnostic
             `- published -> exit 0 or 1
```

All spool readers are replayable and canonically ordered. All temporary storage
is disposed after publication or fatal handling; cleanup failure is diagnostic
but never replaces the original failure.