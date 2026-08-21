# Financial Data Cleaner

A cross-platform .NET 10 command-line validator for timestamped OHLCV CSV data.
It detects missing candles, duplicate records, invalid OHLC values, records in
closed market periods, time gaps, and malformed rows without changing the input
file. Reports are deterministic and available as text or JSON. It can also score
each quality dimension on a 0-to-100 scale and report one weighted dataset
average, on request, without altering any count or exit code.


## Build

Install the .NET 10 SDK, then run from the repository root:

```powershell
dotnet restore FinancialDataCleaner.slnx
dotnet build FinancialDataCleaner.slnx --configuration Release --no-restore
dotnet test FinancialDataCleaner.slnx --configuration Release --no-build
```

The built CLI is `src/Validator.Cli/bin/Release/net10.0/Validator.Cli.dll`.

## Usage

```powershell
$validator = "src/Validator.Cli/bin/Release/net10.0/Validator.Cli.dll"
dotnet $validator <input-file> [options]
```

Validate a default headerless MT4 export:

```powershell
dotnet $validator tests/Validator.Cli.Tests/Fixtures/clean-forex-h1.csv
```

Emit JSON with an explicit timeframe:

```powershell
dotnet $validator prices.csv --timeframe H1 --format json
```

The unversioned JSON format is the compatible v1 contract. Select the detailed
v2 JSON contract explicitly:

```powershell
dotnet $validator prices.csv --timeframe H1 --format json --report-version 2
```

To include every finding in human-readable text, use verbose mode. The first
six summary lines remain unchanged, followed by source identity, validation
context, scan coverage, check execution, reconciliation, and category-specific
finding details:

```powershell
dotnet $validator prices.csv --timeframe H1 --verbose
```

Score each quality dimension and report one dataset average. Scoring is opt-in
through `--score` and is additive: the six summary lines, the findings, and the
exit code are unchanged. Optionally reweight the average with `--score-weights`
(all six metrics required):

```powershell
dotnet $validator prices.csv --timeframe H1 --score
dotnet $validator prices.csv --timeframe H1 --score `
  --score-weights "missingCandles=2,duplicateRecords=1,invalidOhlc=3,closedMarketRecords=1,timeGaps=2,malformedRows=1"
```

A detailed report can be written atomically to a file:


```powershell
dotnet $validator prices.csv --timeframe H1 --format json `
  --report-version 2 --output report-v2.json
```

Validate a headered combined-timestamp source:

```powershell
dotnet $validator prices.csv --header `
  --timestamp-format "yyyy-MM-dd HH:mm:ss" `
  --timestamp-column Timestamp --tz-offset +00:00
```

Use a custom market calendar and write the report atomically to a file:

```powershell
dotnet $validator custom.csv --market custom --calendar market-hours.json `
  --timeframe H1 --format json --output report.json
```

Run `dotnet $validator --help` for the complete examples included with the CLI.

## Options

| Option | Description | Default |
| --- | --- | --- |
| `<input-file>` | CSV file to validate | Required |
| `--timeframe <code>` | Override interval with `M<n>`, `H<n>`, or `D<n>` | Auto-detect |
| `--market <profile>` | `forex`, `equities`, `crypto`, or `custom` | `forex` |
| `--calendar <path>` | Version 1 JSON calendar; required for custom, optional for equities | None |
| `--date-format <fmt>` | Exact format for a separate date column | `yyyy.MM.dd` |
| `--time-format <fmt>` | Exact format for a separate time column | `HH:mm` or `HH:mm:ss` |
| `--timestamp-format <fmt>` | Exact format for a combined timestamp column | None |
| `--timestamp-column <name-or-index>` | Combined timestamp header name or one-based column index | None |
| `--tz-offset <+HH:mm\|-HH:mm>` | Fixed source timestamp offset within +/-14:00 | `+02:00` |
| `--delimiter <value>` | `comma`, `semicolon`, `tab`, `,`, `;`, or `\t` | Auto-detect |
| `--header` | Match required columns by case-insensitive header name | Off |
| `--format <text\|json>` | Select report format | `text` |
| `--report-version <1\|2>` | Select the JSON contract version; v2 enables detailed JSON | `1` |
| `--output <path>` | Write the complete report atomically to a file | Stdout |
| `--verbose` | Append complete canonical finding details to text output | Off |
| `--score` | Report per-metric quality scores and one dataset average | Off |
| `--score-weights <list>` | Reweight the average with six `metric=weight` pairs; requires `--score` | Equal weights |
| `--instrument <identity>` | Explicit instrument identity for benchmark establishment or comparison | None |
| `--benchmark <name>` | Establish a named benchmark from the validated dataset | None |
| `--benchmark-dir <path>` | Benchmark storage directory | `./benchmarks/` |
| `--benchmark-delete <name>` | Delete a stored benchmark | None |
| `--compare <name>` | Compare candidate against a stored benchmark | None |
| `--tolerances <json>` | Custom per-field tolerance overrides (JSON) | Defaults |
| `--yes` | Skip confirmation prompt for benchmark deletion | Off |
| `--help` | Show all options and required examples | None |


The default headerless layout is:

```text
Date,Time,Open,High,Low,Close,Volume
```

Numeric and date/time parsing is culture-invariant. Source timestamps are
converted to UTC before validation. Delimiter and timeframe ambiguity fail with
an actionable diagnostic rather than an inferred guess.

## Output

Default text output always begins with exactly six lines:

```text
Missing candles: 0
Duplicate records: 0
Invalid OHLC: 0
Closed-market records: 0
Time gaps: 0
Malformed rows: 0
```

Unversioned JSON and `--report-version 1` conform to the compatible v1 contract:
[`validation-report.schema.json`](specs/001-ohlcv-data-quality-validator/contracts/validation-report.schema.json).
`--format json --report-version 2` produces a complete detailed report with
source identity, resolved validation context, scan coverage, check status,
reconciled category counts, and typed evidence for every finding. Its contract
is documented in
[`detailed-report-v2.schema.json`](specs/002-detailed-error-report/contracts/detailed-report-v2.schema.json).

Detailed reports are complete and deterministic. They include all findings from
checks that completed, keep related missing-candle and time-gap findings linked,
and never expose an absolute source path by default. With `--output`, the
selected report is staged and committed atomically; stdout contains one
completion-summary line only after a successful commit.

When a v2 run cannot produce a trustworthy complete report, exit code `2` is
returned, stdout remains empty, and stderr contains one structured fatal
diagnostic conforming to
[`fatal-diagnostic-v2.schema.json`](specs/002-detailed-error-report/contracts/fatal-diagnostic-v2.schema.json).

## Scoring

With `--score`, a labelled scoring section is appended to text output
immediately after the six summary lines. Each metric is scored on a 0-to-100
scale as `100 x (1 - defect rate)`, where higher is better, and each scored line
states the count, population, population kind, resolved weight, and normalised
share so the score can be recomputed by hand:

```text
Missing candles: 1
Duplicate records: 1
Invalid OHLC: 1
Closed-market records: 0
Time gaps: 1
Malformed rows: 1

Quality scores (0-100, higher is better):
- Missing candles: 80.00 (count=1; population=5 expected candles; weight=1; share=0.17)
- Duplicate records: 80.00 (count=1; population=5 accepted rows; weight=1; share=0.17)
- Invalid OHLC: 80.00 (count=1; population=5 accepted rows; weight=1; share=0.17)
- Closed-market records: 100.00 (count=0; population=5 accepted rows; weight=1; share=0.17)
- Time gaps: 80.00 (count=1; population=5 expected candles; weight=1; share=0.17)
- Malformed rows: 83.33 (count=1; population=6 examined rows; weight=1; share=0.17)
Dataset average: 83.89 (covers 6 of 6 metrics)
```

The dataset average is the weighted mean of the unrounded per-metric scores over
exactly the metrics that were scored, rounded once to two decimals. A metric
whose check did not run is reported as `not applicable` and a metric with a zero
population as `not scored`, each with its reason; neither is credited as a
perfect score, and the average narrows its coverage and lists the excluded
metrics. When no metric can be scored the average is reported as
`not available` with a reason and never as a substitute number.

Scoring is derived from the values the run already establishes; it adds no check,
never re-reads the source, and never changes a summary count, a finding, the
finding order, or the exit code. `--score-weights` affects only the average.

Under `--format json --report-version 2`, `--score` adds one optional top-level
`scoring` object, documented in
[`scoring-v2.schema.json`](specs/003-dataset-quality-scoring/contracts/scoring-v2.schema.json)
and applied additively to the v2 report per
[`detailed-report-v2-amendment.md`](specs/003-dataset-quality-scoring/contracts/detailed-report-v2-amendment.md);
`contractVersion` stays `2` and the member is absent when scoring is not
requested. Because the version 1 JSON contract is frozen, `--score` with
`--format json` (v1) is rejected with exit code `2` and a message directing you
to `--format json --report-version 2`.

## Benchmarks and Comparison

Establish a validated dataset as a named immutable benchmark snapshot, then
compare a candidate dataset against that benchmark to detect material OHLCV
discrepancies while tolerating acceptable broker-level differences.

### Establish a Benchmark

```powershell
dotnet $validator prices.csv --timeframe D1 --score --report-version 2 --format json \
  --instrument AUDUSD --benchmark my-benchmark
```

This creates `./benchmarks/my-benchmark/` containing `benchmark.json` and
`source.csv`. The benchmark is immutable — establishing a second benchmark with
the same name fails with exit code 2.

### List and Delete Benchmarks

```powershell
# Delete a benchmark (prompts for confirmation)
dotnet $validator --benchmark-delete my-benchmark

# Skip confirmation
dotnet $validator --benchmark-delete my-benchmark --yes
```

### Compare Against a Benchmark

```powershell
dotnet $validator candidate.csv --timeframe D1 --score --report-version 2 --format json \
  --instrument AUDUSD --compare my-benchmark
```

The comparison reports:
- **Material discrepancies**: OHLCV values exceeding both absolute and relative
  tolerances
- **Tolerated differences**: Values within acceptable tolerance bands
- **Coverage**: Matched, missing, and extra timestamps
- **Benchmark-agreement score**: Percentage of matched timestamps with no material
  discrepancies

### Custom Tolerances

Override default tolerances per field with a JSON object:

```powershell
dotnet $validator candidate.csv --timeframe D1 --score --report-version 2 --format json \
  --compare my-benchmark \
  --tolerances '{"Open": {"absolute": 0.00005}, "Volume": {"relative": 0.02}}'
```

Default tolerances: price fields use the greater of the inferred fractional
quote-unit step and 0.01% relative; volume uses 5% relative. The inferred and
resolved tolerances are included in comparison output for auditability.

## Exit Codes


| Code | Meaning |
| ---: | --- |
| `0` | Help requested, validation completed with no findings, or an advisory comparison completed regardless of discrepancies/findings |
| `1` | Validation completed with one or more findings |
| `2` | Fatal dataset, configuration, ingestion, benchmark/comparison, timeframe, calendar, reconciliation, or report-write failure |

Fatal failures write diagnostics to stderr and do not emit a data-quality report.

## Architecture

The solution uses four inward-facing projects:

- `Validator.Domain`: immutable financial and calendar concepts
- `Validator.Application`: validation rules, orchestration, and ports
- `Validator.Infrastructure`: CSV, sorting, calendar, finding, and report adapters
- `Validator.Cli`: argument handling and composition

Domain is held to 100% line and branch coverage. The merged Domain/Application
coverage workflow currently measures 99.28% line and 97.97% branch coverage and
enforces 99.2% and 97.9% ratchet thresholds; the remaining paths are defensive
arms that cannot be reached through valid public compositions and are documented
in `.github/workflows/coverage.yml`. Infrastructure uses real-file integration
tests, and the CLI uses end-to-end tests. The feature contracts and runnable walkthroughs are in
[`specs/001-ohlcv-data-quality-validator/`](specs/001-ohlcv-data-quality-validator/)
and
[`specs/002-detailed-error-report/`](specs/002-detailed-error-report/),
[`specs/003-dataset-quality-scoring/`](specs/003-dataset-quality-scoring/), and
[`specs/004-benchmark-dataset-comparison/`](specs/004-benchmark-dataset-comparison/).
