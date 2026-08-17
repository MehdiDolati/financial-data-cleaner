# Quickstart: Validate Detailed Reporting End to End

This guide validates the implementation produced from this plan through public
contracts. Implementation sequencing belongs in `tasks.md`; each behavior below
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

Expected: all existing and feature-002 tests pass with zero warnings. Domain and
Application retain inward-only references; reporting serialization, hashing,
filesystem publication, and console routing remain outside those assemblies.

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

Expected: exactly 100% line and branch coverage for both business-logic
assemblies. Infrastructure and CLI are proven through integration and
process-level suites.

## 3. Locate the CLI and Verify Help

```powershell
$validator = "src/Validator.Cli/bin/Release/net10.0/Validator.Cli.dll"
dotnet $validator --help
```

Expected: help documents `--report-version <1|2>`, states that unversioned JSON
uses v1, and includes concise, verbose, JSON v1, JSON v2, and v2 output-file
examples from [`contracts/cli.md`](contracts/cli.md).

## 4. Prove Existing Defaults Remain Compatible

```powershell
dotnet $validator tests/Validator.Cli.Tests/Fixtures/clean-forex-h1.csv
$textExit = $LASTEXITCODE

dotnet $validator tests/Validator.Cli.Tests/Fixtures/known-defects.csv `
  --timeframe H1 --format json > report-v1-default.json
$v1DefaultExit = $LASTEXITCODE

dotnet $validator tests/Validator.Cli.Tests/Fixtures/known-defects.csv `
  --timeframe H1 --format json --report-version 1 > report-v1-explicit.json
$v1ExplicitExit = $LASTEXITCODE

Compare-Object `
  (Get-Content report-v1-default.json -Raw) `
  (Get-Content report-v1-explicit.json -Raw)
```

Observed for `known-defects.csv --timeframe H1` (stdout, exit `1`):

```text
Missing candles: 2
Duplicate records: 1
Invalid OHLC: 1
Closed-market records: 0
Time gaps: 2
Malformed rows: 0
```

Expected:

- Concise text remains exactly the six established summary lines.
- Default and explicit v1 JSON are byte-identical and validate against
  `specs/001-ohlcv-data-quality-validator/contracts/validation-report.schema.json`.
- Both finding runs preserve exit code `1`.
- No v2-only field appears in v1.

Retain golden v1 contract tests so later v2 changes cannot alter this result.

## 5. Produce and Validate JSON v2

```powershell
dotnet $validator tests/Validator.Cli.Tests/Fixtures/known-defects.csv `
  --timeframe H1 --format json --report-version 2 > report-v2.json
$v2Exit = $LASTEXITCODE

Get-Content report-v2.json -Raw | ConvertFrom-Json | Out-Null
$v2Exit
```

Observed prefix for the same fixture (single line, abbreviated):

```json
{"contractVersion":2,"status":"FindingsDetected","findingSetComplete":true,
 "source":{"fileName":"known-defects.csv","byteSize":204,"sha256":"e5daf57c…764f"},
 "context":{"timeframe":"H1","calendar":{"profile":"forex","name":"Forex"},
  "timestamp":{"mode":"SeparateDateTime","dateFormat":"yyyy.MM.dd","timeFormat":"HH:mm","sourceOffset":"+02:00"},
  "delimiter":"comma","hasHeader":false,
  "dateRange":{"from":"2025-12-31T22:00:00Z","to":"2026-01-01T02:00:00Z"}},
 "coverage":{"physicalRowsExamined":4,"acceptedRows":4,"malformedRows":0},
 "checks":[{"check":"MissingCandles","status":"Completed"}, …]}
```

Expected: stdout contains exactly one JSON document, exits `1`, and validates
offline against
[`contracts/detailed-report-v2.schema.json`](contracts/detailed-report-v2.schema.json).
The CLI contract test project must validate the schema with a locally referenced
Draft 2020-12 validator and no network fetch.

Assert these semantic invariants in addition to schema validation:

- `contractVersion == 2`, `status == FindingsDetected`, and
  `findingSetComplete == true`.
- Source name is safe, byte size matches the fixture, and SHA-256 matches an
  independent hash of the exact fixture bytes.
- Resolved timeframe, calendar, timestamp mode/format/offset, delimiter, header
  mode, and UTC date range match invocation and fixture.
- `physicalRowsExamined == acceptedRows + malformedRows`.
- Exactly six checks appear in canonical order; none is `NotCompleted`.
- For each category, `summaryCount == contributionSum` and `entryCount` matches
  the number of detailed entries.
- Findings are complete and in category/timestamp/line/reference order.

## 6. Verify Every Category-Specific Detail

Use a reviewed cross-category fixture containing:

- one multi-candle gap with adjacent observed records;
- one exact duplicate group and one conflicting group of at least three rows;
- one row violating multiple OHLC/volume rules;
- one closed-market row at a boundary;
- one malformed row with a valid timestamp and multiple invalid values;
- one malformed row whose timestamp is not parseable.

Validate verbose text and v2 JSON against the same expected-evidence manifest.
Expected:

- Missing candles have no invented source line, include timeframe and adjacent
  timestamps, and reference their owning gap.
- The gap includes first/last missing timestamps, elapsed seconds, missing count,
  adjacent observations, and references every missing-candle finding.
- Relationship references exist in both directions.
- Duplicate details include every source line and each row's OHLCV values; a
  conflicting group names every differing field; contribution is `rows - 1`.
- Invalid OHLC appears once with all observed values and every violated stable
  rule code.
- Closed-market evidence identifies the selected calendar and concrete boundary
  or recurring closed rule.
- Each malformed row appears once, contains every independently detectable field
  error and original offending value, identifies skipped checks, and states
  whether its expected candle slot was reserved.

Verbose text contains equivalent substantive evidence under human-readable
section labels and keeps the first six summary lines unchanged.

Observed verbose text after the six summary lines
(`known-defects.csv --timeframe H1 --verbose`, abbreviated):

```text
Report status:
- status: FindingsDetected
- validationCompleted: true
- findingSetComplete: true (complete for every check listed as Completed under Check execution)
- contractVersion: 2

Source identity:
- fileName: "known-defects.csv"
- byteSize: 204
- sha256: e5daf57c800fbaf9d38e1fa2746ec53e2f86fff5cbfada4ff3df63f7f8c7764f

Validation context:
- timeframe: H1
- calendarProfile: forex
- calendarName: "Forex"
```

Automated coverage: `tests/Validator.Cli.Tests/ReportCompatibilityTests.cs` and
`tests/Validator.Cli.Tests/DetailedReportV2E2ETests.cs`.

## 7. Clean and Not-Applicable Scenarios

```powershell
dotnet $validator tests/Validator.Cli.Tests/Fixtures/clean-forex-h1.csv `
  --format json --report-version 2 > clean-v2.json
```

Expected: `status == Clean`, all six summary and reconciliation counts are zero,
the finding array is empty, and the exit code is `0`.

Repeat with empty/header-only and single-record fixtures plus a valid
`--timeframe` override. Expected: successful clean reports; checks without an
evaluation domain are `NotApplicable` with a non-empty reason; no check is
`NotCompleted`; completeness remains true.

## 8. Structured Fatal v2 Diagnostics

Run v2 against fixtures for unreadable input, invalid UTF-8, invalid CSV grammar,
too few required columns, ambiguous delimiter, unresolved timeframe, invalid
calendar, forced reconciliation failure, forced renderer failure, and unwritable
destination. Capture streams independently:

```powershell
dotnet $validator tests/Validator.Cli.Tests/Fixtures/missing-close-column.csv `
  --header --format json --report-version 2 `
  1> fatal-stdout.txt 2> fatal-v2.json
$fatalExit = $LASTEXITCODE
```

Expected:

- Exit code is `2`.
- `fatal-stdout.txt` is empty.
- Stderr contains exactly one document and validates against
  [`contracts/fatal-diagnostic-v2.schema.json`](contracts/fatal-diagnostic-v2.schema.json).
- Status is `Fatal`, completeness is false, and stable code/class/stage match the
  fixture.
- Reason, guidance, known safe source/location fields, and all six check statuses
  are present as applicable.
- There is no successful summary, reconciliation, `isClean`, or findings array.

Observed stderr for the command above (exit `2`, stdout `0` bytes, abbreviated):

```json
{"contractVersion":2,"status":"Fatal","findingSetComplete":false,
 "code":"INVALID_STRUCTURE","failureClass":"Dataset","stage":"Ingestion",
 "reason":"The source does not expose the columns the active layout requires.",
 "guidance":"Required header 'close' was not found in the CSV input.",
 "source":{"fileName":"missing-close-column.csv"},
 "checks":[{"check":"MissingCandles","status":"NotCompleted","reason":"Validation did not run."}, …]}
```

Automated coverage: `tests/Validator.Cli.Tests/FatalV2RoutingTests.cs`,
`tests/Validator.Cli.Tests/OperationalFailureTests.cs`, and
`tests/Validator.Cli.Tests/SchemaValidationTests.cs`.

Repeat with `--output existing-report.json` containing sentinel bytes. On fatal
exit the sentinel file remains byte-for-byte unchanged.

## 9. Distinguish Failure Classes

Contract tests cover at least these mappings:

| Scenario | Expected class | Expected stage |
|---|---|---|
| Invalid timestamp-option combination | `Configuration` | `ArgumentValidation` |
| Structurally short row | `Dataset` | `Ingestion` |
| Invalid UTF-8 | `Dataset` | `Ingestion` |
| Missing/unreadable input | `Operational` | `SourceIdentity` |
| Ambiguous timeframe without override | `Configuration` | `TimeframeResolution` |
| Reconciliation mismatch | `Operational` | `Reconciliation` |
| Renderer or destination failure | `Operational` | `ReportRendering` or `ReportCommit` |

Expected: dataset defects are never mislabeled as operational/configuration
problems, and no fatal path claims a complete scan.

## 10. Source-Value Escaping and 64-Bit Lines

Use a generated fixture whose malformed values contain quotes, commas, tabs,
CR/LF, backslashes, Unicode, report-like headings, and other control characters.
Validate both verbose text and v2 JSON.

Expected:

- JSON remains one valid document and preserves the exact logical string values.
- Text shows each value as one quoted escaped datum; source content cannot create
  a new heading or finding.
- No field is silently truncated.
- Application/writer tests cover a physical source line greater than
  `Int32.MaxValue` and preserve it as a JSON integer and text decimal value.

## 11. Determinism

Run verbose text and v2 JSON twice with identical source bytes/options and compare
the outputs byte-for-byte. Repeat after changing host culture and time zone.

Expected: outputs remain identical. Public documents contain no generated time,
random identifier, absolute path, host-specific line ordering, or localized
number/date. Changing one source byte changes SHA-256 and only substantively
affected report fields.

## 12. Atomic Destination and Source Protection

```powershell
Copy-Item tests/Validator.Cli.Tests/Fixtures/known-defects.csv source-copy.csv
$before = (Get-FileHash source-copy.csv -Algorithm SHA256).Hash

dotnet $validator source-copy.csv --timeframe H1 `
  --format json --report-version 2 --output report-v2.json

$after = (Get-FileHash source-copy.csv -Algorithm SHA256).Hash
$before -eq $after
```

Expected: true, and `report-v2.json` validates against the v2 schema.

Then request the same normalized path for input and output. Expected: failure
before validation, exit `2`, structured v2 stderr, and unchanged source bytes.
Inject report rendering and atomic-move failures in Infrastructure tests; no
partial destination is presented as complete and all temporary artifacts are
removed.

## 13. Bounded-Memory Completeness

This scenario is automated in
`tests/Validator.Cli.Tests/LargeReportMemoryTests.cs`, which generates its
sources at run time rather than committing large fixtures:

```powershell
dotnet test tests/Validator.Cli.Tests/Validator.Cli.Tests.csproj `
  --configuration Release --no-build `
  --filter "FullyQualifiedName~LargeReportMemoryTests"
```

Expected: all cases pass. The suite covers a 100,000-finding gap, a
20,000-row duplicate group, hostile source text at scale, temporary-artifact
cleanup, cancelled writes, and interrupted writes, and it asserts two separate
memory guarantees: growth per finding is limited to the catalog's compact index
record, and growing one finding's children a hundredfold costs only the
configured buffers.

To reproduce by hand, use deterministic generators outside the repository to
create:

- at least 100,000 top-level findings;
- one duplicate group with enough rows to exceed the configured in-memory chunk;
- one gap with enough missing candles/references to exceed that chunk;
- malformed rows with multiple child field errors.

Run JSON v2 to a file and verbose text to a file. Integration tests instrument
the configured spool/sort buffer and may sample peak working set with a fixed
tolerance; they must not enforce a latency SLA.

Expected:

- Every generated finding appears; no truncation, sampling, or first-N behavior.
- Every duplicate row, gap child reference, violation, malformed field error, and
  relationship appears.
- Category contributions reconcile exactly.
- Peak memory remains bounded independently of total input rows and finding/child
  counts, with growth limited to configured buffers and serializer state.
- Output is valid and temporary files are removed on clean, findings, fatal, and
  cancellation paths.

## 14. Alternate Front-End Proof

Use an Application test host that references only `Validator.Application` and
`Validator.Domain`, provides in-memory/replayable test adapters, invokes
`IValidateMarketDataUseCase`, and streams the resulting catalog through a test
consumer.

Expected: it reproduces source/context/coverage/check/summary/reconciliation and
all detailed evidence without referencing CLI or Infrastructure and without
source changes to Domain/Application. This proves
[`contracts/application-api.md`](contracts/application-api.md) and the Clean
Architecture constitution gate.

## Completion Criteria

- Every acceptance scenario in `spec.md` has a named automated test.
- Existing concise text and JSON v1 contract tests remain unchanged and green.
- Detailed text and JSON v2 contain equivalent substantive data for all six
  categories.
- Both v2 schemas validate offline in contract tests.
- Every successful category and scan-coverage value reconciles before rendering.
- Every v2 fatal path emits one stderr document, empty stdout/destination, and
  exit `2`.
- Repeated reports are deterministic; hostile source values cannot alter report
  structure; source bytes are always unchanged.
- At least 100,000 findings and unbounded child groups are complete with bounded
  memory.
- Domain/Application line and branch coverage remain exactly 100%.