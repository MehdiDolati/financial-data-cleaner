# Quickstart: Validate the OHLCV Validator End to End

This is a runnable validation guide for the implementation produced from this
plan. It proves the feature through public behavior; implementation sequencing
and production code belong in `tasks.md` and the implementation phase.

## Prerequisites

- .NET SDK 10.0.301 or a later compatible .NET 10 SDK
- Git
- PowerShell 7/Windows PowerShell on Windows, or an equivalent POSIX shell
- Repository root as the working directory

Confirm the SDK:

```powershell
dotnet --version
```

Expected: a `10.0.x` version.

## 1. Restore and Build

```powershell
dotnet restore FinancialDataCleaner.slnx
dotnet build FinancialDataCleaner.slnx --configuration Release --no-restore
```

Expected: all four production projects and all four test projects compile with
zero warnings and errors. Domain has no package/project references beyond the
BCL; Application references only Domain.

## 2. Run the Test-First Suites

During implementation, run the narrow failing test first, confirm red, implement
the smallest behavior, then confirm green. At a full checkpoint run:

```powershell
dotnet test FinancialDataCleaner.slnx --configuration Release --no-build
```

Expected: Domain/Application theory tests, Infrastructure fixture tests, and CLI
process-level tests all pass. Required boundary cases include:

- `High == Low`, all-positive flat candles, zero/negative prices, and negative volume
- duplicate group sizes two and three, both exact and conflicting
- Friday 21:59:59/22:00 UTC and Sunday 21:59:59/22:00 UTC forex boundaries
- New York equity sessions on both sides of DST changes
- one missing candle, multiple contiguous missing candles, and separate gaps
- malformed value rows versus fatal short-column rows
- empty, header-only, single-record, unsorted, mixed-interval, and tied-mode files;
  no-override timeframe failures and valid-override empty/single-record paths
- invalid UTF-8, quoted delimiters, extra columns, and reordered case-insensitive headers
- clean/finding/fatal exit codes and JSON stdout purity

## 3. Enforce Business-Logic Coverage

```powershell
dotnet test tests/Validator.Domain.Tests/Validator.Domain.Tests.csproj `
  --configuration Release --no-build `
  /p:CollectCoverage=true /p:Threshold=100 `
  /p:ThresholdType="line%2cbranch" /p:ThresholdStat=Total `
  /p:Include="[Validator.Domain]*"

dotnet test tests/Validator.Application.Tests/Validator.Application.Tests.csproj `
  --configuration Release --no-build `
  /p:CollectCoverage=true /p:Threshold=100 `
  /p:ThresholdType="line%2cbranch" /p:ThresholdStat=Total `
  /p:Include="[Validator.Application]*"
```

Expected: 100% line and 100% branch coverage for `Validator.Domain` and
`Validator.Application`. Infrastructure and CLI are excluded from the percentage
gate and proven by integration/E2E tests instead.

## 4. Locate the CLI

After a Release build:

```powershell
$validator = "src/Validator.Cli/bin/Release/net10.0/Validator.Cli.dll"
dotnet $validator --help
```

Expected: help lists every option in
[`contracts/cli.md`](contracts/cli.md), includes at least one invocation example,
and exits 0 without requiring an input file.

## 5. Clean Fixture Scenario

Use the committed fixture created by implementation tasks:

```powershell
dotnet $validator tests/Validator.Cli.Tests/Fixtures/clean-forex-h1.csv
$LASTEXITCODE
```

Expected stdout:

```text
Missing candles: 0
Duplicate records: 0
Invalid OHLC: 0
Closed-market records: 0
Time gaps: 0
Malformed rows: 0
```

Expected exit code: `0`.

## 6. Known-Defects Fixture Scenario

```powershell
dotnet $validator tests/Validator.Cli.Tests/Fixtures/known-defects.csv --timeframe H1 --verbose
$LASTEXITCODE
```

Expected:

- The first six lines reproduce the exact fixture manifest counts.
- Verbose details identify exact/conflicting duplicates and every invalid rule.
- A duplicate group of three contributes two duplicate records.
- Missing-candle total equals all absent expected timestamps.
- Time-gap total equals maximal contiguous runs, not missing-candle count.
- Closed-market records neither close nor create gaps.
- A malformed row with a parseable timestamp contributes only to malformed rows;
  its timestamp reserves the expected candle slot.
- Exit code is `1`.

The fixture's expected numbers belong in a small adjacent manifest or named E2E
test so they are reviewed with fixture changes rather than duplicated here.

## 7. JSON Contract Scenario

```powershell
dotnet $validator tests/Validator.Cli.Tests/Fixtures/known-defects.csv `
  --timeframe H1 --format json > validation-report.json
$validatorExit = $LASTEXITCODE

Get-Content validation-report.json -Raw | ConvertFrom-Json | Out-Null
$validatorExit
```

Expected:

- stdout redirection contains exactly one valid JSON document.
- The document validates against
  [`contracts/validation-report.schema.json`](contracts/validation-report.schema.json).
- It contains the same six counts as text, safe source metadata, UTC range,
  `isClean: false`, and the complete canonical `findings` array.
- The validator itself exits `1`; JSON parsing succeeds.

Schema validation should also run automatically in the CLI test project using a
locally referenced JSON Schema test dependency (no network fetch at test time).

## 8. Fatal Ingestion Scenario

```powershell
dotnet $validator tests/Validator.Cli.Tests/Fixtures/missing-close-column.csv --header
$LASTEXITCODE
```

Expected: actionable fatal diagnostic on stderr, no six-count data-quality
report on stdout, and exit code `2`.

Repeat through automated tests for unreadable files, invalid UTF-8, malformed CSV
grammar, ambiguous delimiter detection, and tied timeframe mode.

## 9. Header, Delimiter, and Combined-Timestamp Scenarios

```powershell
dotnet $validator tests/Validator.Cli.Tests/Fixtures/header-semicolon.csv `
  --header --delimiter semicolon

dotnet $validator tests/Validator.Cli.Tests/Fixtures/combined-timestamp.csv `
  --header --timestamp-format "yyyy-MM-dd HH:mm:ss" --timestamp-column Timestamp `
  --tz-offset +00:00
```

Expected: both complete normally, parse invariantly under any host culture, and
produce their fixture manifest counts.

## 10. Custom Market Calendar Scenario

Example `custom-market.json`:

```json
{
  "version": 1,
  "name": "Weekday UTC Session",
  "timeZone": "UTC",
  "sessions": [
    { "openDay": "Monday", "openTime": "09:00", "closeDay": "Monday", "closeTime": "17:00" },
    { "openDay": "Tuesday", "openTime": "09:00", "closeDay": "Tuesday", "closeTime": "17:00" },
    { "openDay": "Wednesday", "openTime": "09:00", "closeDay": "Wednesday", "closeTime": "17:00" },
    { "openDay": "Thursday", "openTime": "09:00", "closeDay": "Thursday", "closeTime": "17:00" },
    { "openDay": "Friday", "openTime": "09:00", "closeDay": "Friday", "closeTime": "17:00" }
  ]
}
```

Run:

```powershell
dotnet $validator tests/Validator.Cli.Tests/Fixtures/custom-session.csv `
  --market custom `
  --calendar tests/Validator.Cli.Tests/Fixtures/custom-market.json `
  --timeframe H1 --tz-offset +00:00
```

Expected: only timestamps inside `[09:00, 17:00)` are expected; a 17:00 record is
counted under closed-market records. Missing closed timestamps are ignored.
The config validates against
[`contracts/market-calendar.schema.json`](contracts/market-calendar.schema.json).

## 11. Output File Scenario

```powershell
dotnet $validator tests/Validator.Cli.Tests/Fixtures/clean-forex-h1.csv `
  --format json --output report.json
```

Expected: `report.json` contains the complete valid JSON report and stdout
contains only the one-line completion summary from the CLI contract. The source
fixture remains byte-for-byte unchanged.

## 12. Bounded-Memory and Replay Validation

Run the committed large-fixture generator or test fixture that creates several
million unsorted M1 rows outside the repository, then invoke the CLI. The
automated performance/integration test should sample peak working set or assert
the configured chunk-size invariant rather than enforce a latency SLA.

Expected:

- Results match the equivalent pre-sorted fixture.
- Peak memory remains within a fixed tolerance independent of row count.
- Temporary sort/finding artifacts are removed after clean, finding, fatal, and
  cancellation paths.
- No source data is changed.

## 13. Alternate Front-End Proof

Create a disposable test host (or use a test-project harness) that references
only `Validator.Application` and `Validator.Domain`, supplies in-memory
implementations of Application ports, and calls `IValidateMarketDataUseCase`.

Expected: it reproduces the CLI report counts with no source change to either
assembly. This proves the contract in
[`contracts/application-api.md`](contracts/application-api.md) and NFR-003.

## Completion Criteria

- Every acceptance scenario in `spec.md` has a named automated test.
- All tests pass on Windows, Linux, and macOS CI runners.
- Domain/Application line and branch coverage are exactly 100%.
- JSON output validates against its schema; custom calendars validate against
  their schema.
- Clean, findings, and fatal paths return 0, 1, and 2 respectively.
- Reordered and large inputs preserve deterministic results and bounded memory.
- Domain/Application can be driven without the CLI or Infrastructure assembly.