# Quickstart: Validate Dataset Quality Scoring End to End

This guide validates the implementation produced from this plan through its public
contracts. Implementation sequencing belongs in `tasks.md`; each behaviour below
must first be introduced by a failing test.

## Prerequisites

- .NET SDK 10.0.301 or a later compatible .NET 10 SDK
- Repository root as the working directory
- PowerShell on Windows or an equivalent POSIX shell

Confirm the SDK:

```powershell
dotnet --version
```

Expected: `10.0.x`.

## 1. Restore, Build, and Test

```powershell
dotnet restore FinancialDataCleaner.slnx
dotnet build FinancialDataCleaner.slnx --configuration Release --no-restore
dotnet test FinancialDataCleaner.slnx --configuration Release --no-build
```

Expected: all existing and feature-003 tests pass with zero warnings. Domain and
Application retain inward-only references; no scoring type references a
serializer, console, or file system.

## 2. Enforce Business-Logic Coverage

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

Expected: the scoring code in both business-logic assemblies is fully covered,
holding the established line and branch gate.

## 3. Locate the CLI and Verify Help

```powershell
$validator = "src/Validator.Cli/bin/Release/net10.0/Validator.Cli.dll"
dotnet $validator --help
```

Expected: help documents `--score` and `--score-weights`, states that scoring
requires the v2 JSON contract when emitting JSON, and includes a scored example,
per [`contracts/cli.md`](contracts/cli.md).

## 4. Prove the Unscored Path Is Byte-Identical

This is the primary regression guarantee (SC-006).

```powershell
dotnet $validator tests/Validator.Cli.Tests/Fixtures/known-defects.csv `
  --timeframe H1 > unscored.txt
$unscoredExit = $LASTEXITCODE

dotnet $validator tests/Validator.Cli.Tests/Fixtures/known-defects.csv `
  --timeframe H1 --format json > unscored-v1.json

dotnet $validator tests/Validator.Cli.Tests/Fixtures/known-defects.csv `
  --timeframe H1 --format json --report-version 2 > unscored-v2.json
```

Expected:

- `unscored.txt` is exactly the six established summary lines, with no scoring
  section anywhere.
- `unscored-v1.json` is byte-identical to its pre-feature output and validates
  against the v1 contract.
- `unscored-v2.json` contains **no** `scoring` member and still validates against
  the detailed v2 contract.
- `$unscoredExit` is `1`, unchanged.

## 5. Score the Same Dataset and Recalculate by Hand

```powershell
dotnet $validator tests/Validator.Cli.Tests/Fixtures/known-defects.csv `
  --timeframe H1 --score > scored.txt
$scoredExit = $LASTEXITCODE

Get-Content scored.txt
$scoredExit
```

Expected:

- The first six lines of `scored.txt` are byte-identical to `unscored.txt`.
- A `Quality scores (0-100, higher is better):` section follows, listing all six
  metrics in the established category order.
- Each scored metric states its count, population, population kind, weight, and
  normalised share.
- `Dataset average:` states its value and how many of the six metrics it covers.
- `$scoredExit` is `1` — identical to the unscored run. The score does not change
  the exit code.

Verify SC-002 by hand for any scored metric: with count `c` and population `p`,
the reported score must equal `100 × (p − c) / p` rounded to two decimals, half
away from zero. Then confirm the average equals the weighted mean of the
unrounded metric scores over exactly the covered metrics.

## 6. Confirm the Scored Run Changes Nothing Else

```powershell
dotnet $validator tests/Validator.Cli.Tests/Fixtures/known-defects.csv `
  --timeframe H1 --verbose > verbose-unscored.txt

dotnet $validator tests/Validator.Cli.Tests/Fixtures/known-defects.csv `
  --timeframe H1 --verbose --score > verbose-scored.txt

Compare-Object (Get-Content verbose-unscored.txt) (Get-Content verbose-scored.txt)
```

Expected: the only differences are the added scoring section lines. Every summary
line, every finding, and the finding order are unchanged, confirming FR-002 and
FR-003.

## 7. Validate the JSON v2 Scoring Contract

```powershell
dotnet $validator tests/Validator.Cli.Tests/Fixtures/known-defects.csv `
  --timeframe H1 --score --format json --report-version 2 > scored-v2.json

Get-Content scored-v2.json -Raw | ConvertFrom-Json | Out-Null
```

Expected: exactly one JSON document whose `contractVersion` is still `2`, which
validates against the detailed v2 contract as amended by
[`contracts/detailed-report-v2-amendment.md`](contracts/detailed-report-v2-amendment.md),
and whose `scoring` member validates against
[`contracts/scoring-v2.schema.json`](contracts/scoring-v2.schema.json).

Confirm every value is a separate documented field — score, count, population,
population kind, state, reason, weight, normalised share, average, metric
coverage, excluded metrics — so no consumer parses human-readable text (SC-005).

## 8. Reject Scoring on the v1 Contract

```powershell
dotnet $validator tests/Validator.Cli.Tests/Fixtures/known-defects.csv `
  --timeframe H1 --score --format json
$v1ConflictExit = $LASTEXITCODE

dotnet $validator tests/Validator.Cli.Tests/Fixtures/known-defects.csv `
  --timeframe H1 --score --format json --report-version 1
$v1ExplicitConflictExit = $LASTEXITCODE
```

Expected for both: exit code `2`, empty stdout, and one stderr message naming the
conflict and the option needed to obtain scores. Scoring is never silently
ignored, and no v1 field is added or altered.

## 9. Apply and Reject Weights

```powershell
dotnet $validator tests/Validator.Cli.Tests/Fixtures/known-defects.csv `
  --timeframe H1 --score `
  --score-weights "missingCandles=3,duplicateRecords=1,invalidOhlc=2,closedMarketRecords=1,timeGaps=1,malformedRows=1"
```

Expected: per-metric scores are identical to the default-weight run — weights
change only the average — and the report echoes the resolved weights and
normalised shares so the new average can be recalculated.

Each of the following must exit `2` before the dataset is read, emit no report,
and state both the problem and the accepted form:

```powershell
# Omits a metric
dotnet $validator prices.csv --score --score-weights "missingCandles=1"
# Unknown metric name
dotnet $validator prices.csv --score --score-weights "missingCandles=1,duplicateRecords=1,invalidOHLC=1,closedMarketRecords=1,timeGaps=1,malformedRows=1"
# Negative weight
dotnet $validator prices.csv --score --score-weights "missingCandles=-1,duplicateRecords=1,invalidOhlc=1,closedMarketRecords=1,timeGaps=1,malformedRows=1"
# Non-numeric weight
dotnet $validator prices.csv --score --score-weights "missingCandles=high,duplicateRecords=1,invalidOhlc=1,closedMarketRecords=1,timeGaps=1,malformedRows=1"
# All weights zero
dotnet $validator prices.csv --score --score-weights "missingCandles=0,duplicateRecords=0,invalidOhlc=0,closedMarketRecords=0,timeGaps=0,malformedRows=0"
# Weights without --score
dotnet $validator prices.csv --score-weights "missingCandles=1,duplicateRecords=1,invalidOhlc=1,closedMarketRecords=1,timeGaps=1,malformedRows=1"
```

## 10. Prove Unscorable Metrics Are Stated, Not Credited

Create a single-row source so no expected sequence can be bounded:

```powershell
Set-Content -Path single-row.csv -Value "2026.01.05,00:00,1.10,1.20,1.05,1.15,10"
dotnet $validator single-row.csv --timeframe H1 --score
```

Expected: the missing-candle and time-gap metrics are reported as `not
applicable` with the reason the check itself gave, **not** as `100.00`. The
average covers only the remaining metrics and states that coverage (SC-003).

To see an unavailable average, score a source in which no metric can be scored:

```powershell
Set-Content -Path empty.csv -Value ""
dotnet $validator empty.csv --timeframe H1 --score
```

Expected: `Dataset average: not available` with a reason, and no `0.00` or
`100.00` substitute.

## 11. Prove Determinism and Source Safety

```powershell
$before = (Get-FileHash tests/Validator.Cli.Tests/Fixtures/known-defects.csv).Hash

dotnet $validator tests/Validator.Cli.Tests/Fixtures/known-defects.csv `
  --timeframe H1 --score > run-a.txt
dotnet $validator tests/Validator.Cli.Tests/Fixtures/known-defects.csv `
  --timeframe H1 --score > run-b.txt

Compare-Object (Get-Content run-a.txt -Raw) (Get-Content run-b.txt -Raw)
$after = (Get-FileHash tests/Validator.Cli.Tests/Fixtures/known-defects.csv).Hash
$before -eq $after
```

Expected: no differences between the two runs, and `True` for the hash
comparison — the scored run left the source dataset untouched (SC-004, FR-034).

## 12. Clean Up

```powershell
Remove-Item unscored.txt, unscored-v1.json, unscored-v2.json, scored.txt, `
  verbose-unscored.txt, verbose-scored.txt, scored-v2.json, run-a.txt, run-b.txt, `
  single-row.csv, empty.csv -ErrorAction SilentlyContinue
```

## Validation Checklist

| Outcome | Proven by |
|---|---|
| Scores are opt-in and additive | Steps 4, 6 |
| Six summary lines unchanged | Steps 4, 5 |
| Every score recalculable by hand | Step 5 |
| Weights affect only the average | Step 9 |
| Invalid weights rejected before reading data | Step 9 |
| Unscorable metrics stated, never credited | Step 10 |
| Unavailable average stated with a reason | Step 10 |
| Machine-readable scores under the v2 contract | Step 7 |
| v1 unchanged and the v1 conflict rejected | Steps 4, 8 |
| Deterministic and source-safe | Step 11 |
| Exit codes unchanged | Steps 4, 5 |
