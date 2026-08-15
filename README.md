# Financial Data Cleaner

A cross-platform .NET 10 command-line validator for timestamped OHLCV CSV data.
It detects missing candles, duplicate records, invalid OHLC values, records in
closed market periods, time gaps, and malformed rows without changing the input
file. Reports are deterministic and available as text or JSON.

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
| `--output <path>` | Write the complete report atomically to a file | Stdout |
| `--verbose` | Append canonical finding details to text output | Off |
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

JSON output conforms to
[`validation-report.schema.json`](specs/001-ohlcv-data-quality-validator/contracts/validation-report.schema.json).
With `--output`, the selected report is written to the requested path and stdout
contains one completion-summary line.

## Exit Codes

| Code | Meaning |
| ---: | --- |
| `0` | Help requested, or validation completed with no findings |
| `1` | Validation completed with one or more findings |
| `2` | Usage, configuration, ingestion, timeframe, calendar, or report-write failure |

Fatal failures write diagnostics to stderr and do not emit a data-quality report.

## Architecture

The solution uses four inward-facing projects:

- `Validator.Domain`: immutable financial and calendar concepts
- `Validator.Application`: validation rules, orchestration, and ports
- `Validator.Infrastructure`: CSV, sorting, calendar, finding, and report adapters
- `Validator.Cli`: argument handling and composition

Domain and Application are held to 100% line and branch coverage. Infrastructure
uses real-file integration tests, and the CLI uses end-to-end tests. The detailed
feature contract and runnable walkthrough are in
[`specs/001-ohlcv-data-quality-validator/`](specs/001-ohlcv-data-quality-validator/).
