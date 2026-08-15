# Data Model: OHLCV Data-Quality Validator

## Modeling Conventions

- Domain and Application models are immutable records/value objects.
- All prices and volume use .NET `decimal`; binary floating-point is prohibited.
- Internal timestamps are UTC instants. A `DateTimeOffset` value must have
  `Offset == TimeSpan.Zero` before it enters Domain logic.
- Physical source line numbers are one-based `long` values.
- Counts are non-negative `long` values so multi-million-row inputs cannot
  overflow 32-bit counters.
- Open/closed intervals are half-open: `[open, close)`. A timestamp at open is
  tradable; a timestamp at close is closed.
- Canonical ordering is timestamp UTC, then source line. Finding output adds
  category order before those keys.

## Core Domain Types

### `PriceCandle`

One successfully parsed source record. It deliberately permits economically
invalid OHLCV relationships so validation rules can report them.

| Field | Type | Rules |
|---|---|---|
| `TimestampUtc` | UTC instant | Required; offset must be zero. |
| `Open` | `decimal` | Parsed invariantly; validity checked separately. |
| `High` | `decimal` | Parsed invariantly; validity checked separately. |
| `Low` | `decimal` | Parsed invariantly; validity checked separately. |
| `Close` | `decimal` | Parsed invariantly; validity checked separately. |
| `Volume` | `decimal` | Parsed invariantly; validity checked separately. Fractional volume is accepted because vendor exports are not guaranteed to be integral. |
| `SourceLine` | `long` | Greater than zero; identifies the physical CSV record start line. |

**Identity**: Source line identifies the physical record. Timestamp equality
defines a duplicate group but does not make the records entity-identical.

### `Timeframe`

A positive candle duration and its canonical display code.

| Field | Type | Rules |
|---|---|---|
| `Code` | string | Canonical upper-case `M<n>`, `H<n>`, or `D<n>`. |
| `Duration` | duration | Greater than zero and exactly represented by the code. |

Codes such as `M1`, `M5`, `M15`, `M30`, `H1`, `H4`, and `D1` are normal but
not hard-coded as the only accepted values. Zero, negative, fractional, unknown
units, and values whose duration overflows are rejected.

### `MarketProfile`

Enumeration: `Forex`, `Equities`, `Crypto`, `Custom`.

### `WeeklySession`

A recurring local-time market-open interval.

| Field | Type | Rules |
|---|---|---|
| `OpenDay` | day of week | Required. |
| `OpenTime` | local time | Required, second precision. |
| `CloseDay` | day of week | Required. |
| `CloseTime` | local time | Required, second precision. |

The close boundary must occur strictly after the open boundary when projected
onto a recurring seven-day cycle; equal boundaries are invalid rather than
interpreted as always open. Sessions must not overlap. A custom local boundary
that is skipped or ambiguous under its time zone for a date in the evaluated
range causes a fatal configuration error rather than an inferred resolution.

### `MarketCalendarDefinition`

| Field | Type | Rules |
|---|---|---|
| `Profile` | `MarketProfile` | Required. |
| `Version` | integer | Required; exactly `1` for the v1 JSON calendar contract. |
| `Name` | string | Non-empty stable display identifier. |
| `TimeZoneId` | string? | IANA zone required for local weekly sessions; null for fixed-UTC forex and always-open crypto. |
| `Sessions` | list of `WeeklySession` | Non-empty for equities/custom; empty for crypto; forex uses its built-in UTC closure. |

**Built-ins**:

- Forex: open Sunday 22:00 UTC through Friday 22:00 UTC.
- Equities: Monday-Friday 09:30-16:00 `America/New_York`.
- Crypto: always open.
- Custom: supplied by a file conforming to
  [`contracts/market-calendar.schema.json`](contracts/market-calendar.schema.json).

### `UtcSession`

A concrete `[OpenUtc, CloseUtc)` interval generated for a date range. Both
values are UTC; close is greater than open. Expected candle timestamps are
aligned to each session's opening instant. For always-open crypto, the first
observed open-market candle supplies the phase anchor.

## Findings

### `FindingCategory`

Canonical order and JSON names:

1. `MissingCandle`
2. `DuplicateRecord`
3. `InvalidOhlc`
4. `ClosedMarketRecord`
5. `TimeGap`
6. `MalformedRow`

The public text summary uses the corresponding labels from FR-030. The
canonical term is `ClosedMarketRecord` because a closed interval may be a
weekend, an overnight equity closure, or a custom session closure.

### `ValidationFinding`

| Field | Type | Rules |
|---|---|---|
| `Category` | `FindingCategory` | Required. |
| `TimestampUtc` | UTC instant? | Null only when the row's timestamp could not be parsed. For a gap, this is its first missing timestamp. |
| `SourceLines` | ordered set of `long` | Empty for expected-but-absent timestamps; one line for row findings; every participating line for duplicate groups. |
| `Message` | string | Non-empty invariant English detail. Includes violated values/rules or duplicate classification as applicable. |
| `CountContribution` | `long` | Positive contribution to the finding's own summary category. |
| `StableSequence` | `long` | Internal spool tie-breaker; not exposed in JSON. |

**Category-specific shape and counting**:

| Category | One finding represents | `CountContribution` |
|---|---|---:|
| `MissingCandle` | One absent expected timestamp | 1 |
| `DuplicateRecord` | One timestamp group of size `n >= 2`; message says exact or conflicting | `n - 1` |
| `InvalidOhlc` | One parsed row violating one or more OHLCV rules | 1 |
| `ClosedMarketRecord` | One parsed row in a closed calendar interval | 1 |
| `TimeGap` | One maximal contiguous run of missing expected timestamps | 1 |
| `MalformedRow` | One structurally present row with value parsing failure | 1 |

A missing run therefore emits one `MissingCandle` finding per absent timestamp
and one `TimeGap` finding for the run. This makes both categories independently
auditable while preserving their different counts.

### `MalformedRow`

An ingestion result that is not a `PriceCandle`.

| Field | Type | Rules |
|---|---|---|
| `SourceLine` | `long` | Greater than zero. |
| `Reason` | string | Non-empty invariant explanation; must not echo unrestricted source content or secrets. |
| `ParsedTimestampUtc` | UTC instant? | Populated only if timestamp parsing succeeded before another field failed. |

Rows with too few active-layout columns are not `MalformedRow`; they make the
whole file structurally invalid and terminate ingestion.

## Report Aggregate

### `ValidationSummary`

| Field | Type | Derivation |
|---|---|---|
| `MissingCandles` | `long` | Sum of `MissingCandle.CountContribution`. |
| `DuplicateRecords` | `long` | Sum of `DuplicateRecord.CountContribution`. |
| `InvalidOhlc` | `long` | Sum of `InvalidOhlc.CountContribution`. |
| `ClosedMarketRecords` | `long` | Sum of `ClosedMarketRecord.CountContribution`. |
| `TimeGaps` | `long` | Sum of `TimeGap.CountContribution`. |
| `MalformedRows` | `long` | Sum of `MalformedRow.CountContribution`. |

All fields are non-negative. `IsClean` is true exactly when all six are zero.

### `DateRange`

`FromUtc` and `ToUtc` are the minimum and maximum timestamps across all
successfully parsed records, including duplicates and closed-period records.
Both are null as one nullable range when there are no parsed records. The range
does not include malformed rows whose timestamp happened to parse.

### `ValidationReport`

| Field | Type | Rules |
|---|---|---|
| `SourceFile` | string | Base file name or caller-provided safe source label; never an absolute machine path. |
| `DetectedTimeframe` | `Timeframe` | Explicit override when supplied; otherwise unique detected mode. Without an override, failure to infer a unique timeframe is fatal and produces no report. Every successful report has a resolved timeframe, including reports for empty or single-record data processed with an explicit override. |
| `TotalRecords` | `long` | Successfully parsed physical records; includes duplicate and closed-period records; excludes malformed rows and a header. |
| `DateRange` | `DateRange?` | Null when `TotalRecords == 0`. |
| `Summary` | `ValidationSummary` | Required. |
| `IsClean` | bool | Derived, never independently assigned. |
| `Findings` | replayable finding sequence | Canonically ordered and streamable; JSON always includes all findings, text includes details only in verbose mode. JSON maps the first internal source line to singular `line`; duplicate messages list the complete group. |

`ValidationReport` exists only after structurally successful ingestion and
completed validation. Fatal errors never produce this aggregate.

A zero-byte headerless file and a valid required-header-only file are
structurally successful, but without `--timeframe` they fail timeframe
resolution and produce no report. With a valid timeframe override, they produce
`TotalRecords == 0`, a null date range, and a clean summary; a single-record
file behaves analogously with no sequence findings.

## Configuration Models

### `CsvInputOptions`

| Field | Type | Default / validation |
|---|---|---|
| `HasHeader` | bool | `false`. |
| `DateFormat` | string | `yyyy.MM.dd`; incompatible with combined timestamp mode when explicitly set. |
| `TimeFormat` | string? | Auto-select exact `HH:mm` or `HH:mm:ss` by colon count; explicit override allowed. |
| `TimestampFormat` | string? | Enables one-column timestamp mode; requires `TimestampColumn` and is incompatible with explicit date/time format options. |
| `TimestampColumn` | string? | Header name or one-based physical index; names require header mode. |
| `SourceOffset` | fixed offset | `+02:00`; exact `+HH:mm`/`-HH:mm`, within ±14:00. |
| `Delimiter` | character? | Comma, semicolon, or tab; null means deterministic auto-detection. |

In header mode, required names are case-insensitive and unique. Separate mode
requires `Date`, `Time`, `Open`, `High`, `Low`, `Close`, `Volume`; combined mode
requires the selected timestamp column plus `Open`, `High`, `Low`, `Close`, and
`Volume`. Extra columns are ignored.

### `ValidationOptions`

| Field | Type | Rules |
|---|---|---|
| `TimeframeOverride` | `Timeframe?` | Null means detect; a valid override is required for empty or single-record data. |
| `MarketCalendar` | `MarketCalendarDefinition` | Required; default resolved forex definition. |
| `Csv` | `CsvInputOptions` | Required. |

Output format, verbosity, and output destination are presentation/reporting
options rather than validation semantics and do not affect report content.

## Relationships

```text
ValidationOptions ──selects──> MarketCalendarDefinition ──expands──> UtcSession
        │
        └──configures──> CSV source ──prepares──> PriceCandle (0..*)
                                      └────────> MalformedRow (0..*)

PriceCandle + UtcSession + Timeframe
        └──evaluated by independent rules──> ValidationFinding (0..*)

ValidationFinding ──contributes to──> ValidationSummary
ValidationSummary + source metadata + finding sequence ──form──> ValidationReport
```

## Validation Session State Transitions

```text
Created
  ├─ invalid arguments/config ───────────────> Fatal (exit 2, no report)
  └─ valid ─> Ingesting
                 ├─ I/O/encoding/CSV/shape error ─> Fatal (exit 2, no report)
                 └─ prepared ─> TimeframeResolution
                                      ├─ ambiguous/no safe mode* ─> Fatal (exit 2)
                                      └─ resolved/not-applicable ─> Validating
                                                                       ├─ adapter failure ─> Fatal (exit 2)
                                                                       └─ complete ─> ReportReady
                                                                                         ├─ clean ─> exit 0
                                                                                         └─ findings ─> exit 1
```

`*` Without an explicit override, no safe mode includes empty, single-applicable-
record, and tied-mode data; these paths are fatal. An explicit valid override
resolves the timeframe and allows empty/single-record validation with sequence
checks not applicable.

Temporary sort and finding artifacts exist only between `Ingesting` and final
disposal. Every terminal path attempts cleanup; cleanup failure is diagnostic
and must not replace the original fatal reason.