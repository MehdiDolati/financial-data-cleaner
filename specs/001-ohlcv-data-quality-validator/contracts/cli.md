# Command-Line Contract

## Command Shape

```text
validator <input-file> [options]
```

`<input-file>` is one required readable regular file. Directories, missing paths,
multiple positional paths, and unknown arguments fail before ingestion.

## Options

| Option | Value | Default | Rules |
|---|---|---|---|
| `--timeframe` | `M<n>`, `H<n>`, `D<n>` | auto-detect | Positive integral duration; case-insensitive input, canonical upper-case output. |
| `--market` | `forex`, `equities`, `crypto`, `custom` | `forex` | Case-insensitive. |
| `--calendar` | file path | none | Required for `custom`; optional override for `equities`; rejected with `forex` or `crypto`. File must be version 1 and match `market-calendar.schema.json`. |
| `--date-format` | .NET exact date format | `yyyy.MM.dd` | Explicit use conflicts with `--timestamp-format`. |
| `--time-format` | .NET exact time format | auto `HH:mm`/`HH:mm:ss` | Explicit use conflicts with `--timestamp-format`. |
| `--timestamp-format` | .NET exact timestamp format | none | Enables combined-column mode. |
| `--timestamp-column` | header name or one-based physical index | none | Required with `--timestamp-format`; names are case-insensitive in header mode, indexes work in either layout. A name requires `--header`. |
| `--tz-offset` | `+HH:mm` or `-HH:mm` | `+02:00` | Fixed offset, no DST, range ±14:00. Applies to source timestamps, not market-calendar boundaries. |
| `--delimiter` | `comma`, `semicolon`, `tab`, `,`, `;`, or `\t` | auto-detect | Resolves to one physical character. |
| `--header` | flag | false | Match required names case-insensitively and independently of order. |
| `--format` | `text`, `json` | `text` | Case-insensitive. |
| `--output` | file path | stdout | Parent must be writable; input and output must not resolve to the same file. |
| `--verbose` | flag | false | Adds finding details to text; accepted but has no effect on JSON because JSON always contains findings. |
| `--help` | flag | — | Prints all options and examples; exits 0 without requiring an input file. |

Argument/configuration errors print one actionable diagnostic plus help guidance
to stderr, print no report counts, and exit 2.

## Input Layouts

### Headerless, separate date/time (default)

```text
Date,Time,Open,High,Low,Close,Volume[,ignored...]
```

### Headerless, combined timestamp

Enabled by `--timestamp-format` and `--timestamp-column <one-based-index>`:

```text
Timestamp,Open,High,Low,Close,Volume[,ignored...]
```

The selected timestamp column may be any physical column; the five OHLCV
columns immediately following it must be `Open`, `High`, `Low`, `Close`, and
`Volume` in that order. A one-based physical index works in either header mode
or headerless mode.

### Header mode

- Separate mode requires unique names: `Date`, `Time`, `Open`, `High`, `Low`,
  `Close`, `Volume`.
- Combined mode requires a unique selected timestamp, `Open`, `High`, `Low`,
  `Close`, and `Volume` columns. The timestamp is selected by a case-insensitive
  header name or one-based physical index; it does not have to be named
  `Timestamp`.
- Matching is case-insensitive; order is arbitrary; extra columns are ignored.
- Missing or duplicate required headers are fatal structural errors.

Any data row with fewer columns than its resolved required indexes is fatal.
Individual date/time/decimal conversion failures are malformed rows.

A zero-byte headerless file is a valid empty dataset. With `--header`, a header
record is required; a required-header-only file is valid and has no data rows.

## Standard Output

### Text summary

With no `--output`, successful text mode begins with and, unless verbose, contains
exactly:

```text
Missing candles: <long>
Duplicate records: <long>
Invalid OHLC: <long>
Closed-market records: <long>
Time gaps: <long>
Malformed rows: <long>
```

`--verbose` appends a blank line, `Findings:`, then canonical one-line details.
The first six summary lines and their order never change.

### JSON

With no `--output`, stdout contains one UTF-8 JSON document and nothing else.
It conforms to
[`validation-report.schema.json`](validation-report.schema.json). Informational
messages are suppressed; fatal diagnostics use stderr.

### Output file

With `--output <path>`, the complete selected report is written atomically to
the path. On success stdout receives one line:

```text
Validation complete: findings=<sum-of-six-counts>; clean=<true|false>; report=<path>
```

The path is printed as supplied, not expanded to a machine-specific absolute
path. A write or replace failure exits 2; no successful report is claimed.

## Exit Codes

| Code | Meaning |
|---:|---|
| `0` | Help was requested, or validation completed and all six counts are zero. |
| `1` | Validation completed and one or more summary counts are non-zero. |
| `2` | Invalid arguments/configuration, fatal ingestion, calendar/timeframe ambiguity, or report output failure. No data-quality report is emitted. |

## Required Help Examples

```text
validator EURUSD_H1.csv
validator EURUSD_M15.csv --header --format json
validator prices.csv --timestamp-format "yyyy-MM-dd HH:mm:ss" --timestamp-column 1 --tz-offset +00:00
validator equities.csv --market equities --timeframe M30 --verbose
validator custom.csv --market custom --calendar market-hours.json --output report.json --format json
```

## Determinism

- Parsing/formatting is invariant regardless of host locale.
- A source timestamp is interpreted with only `--tz-offset`, then converted UTC.
- Findings use the canonical order documented in `data-model.md`.
- A tied timeframe mode and ambiguous delimiter are fatal rather than resolved by
  row order or platform behavior.
- Empty/header-only, single-record, and tied-mode inputs without `--timeframe` fail
  with exit 2 and no report because timeframe inference is unavailable or
  ambiguous. With a valid override, empty and single-record inputs produce a
  normal report with sequence checks not applicable.
- Reordering otherwise identical input records does not change counts; where
  source lines are reported, canonical line ordering is ascending.