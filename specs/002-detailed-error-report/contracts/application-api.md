# Application Contract: Detailed Reporting

This contract evolves the reusable boundary from feature 001. Names are
normative at the concept level; exact namespaces may be finalized during
implementation without changing dependency direction or observable behavior.

## Validation Use Case

```csharp
public interface IValidateMarketDataUseCase
{
    ValueTask<DetailedValidationOutcome> ExecuteAsync(
        ValidationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ValidationRequest(
    string SourceLabel,
    ICandleSource CandleSource,
    ValidationOptions Options);

public abstract record DetailedValidationOutcome
{
    public sealed record Succeeded(
        DetailedValidationReport Report) : DetailedValidationOutcome;

    public sealed record Failed(
        FatalDiagnostic Diagnostic) : DetailedValidationOutcome;
}
```

- `SourceLabel` is a safe display name; Application never exposes an absolute
  path.
- Environment failures and expected invalid input return `Failed`; exceptions
  are reserved for programming faults and cancellation.
- The use case does not write to console or a report file. Presentation selects
  a writer and destination after receiving the outcome.
- Existing front ends may adapt `Succeeded.Report` to the v1 writer; new front
  ends may select detailed text or v2 without changing validation rules.

## Prepared Source Contract

```csharp
public interface ICandleSource
{
    ValueTask<PreparedCandleDataResult> PrepareAsync(
        CsvInputOptions options,
        CancellationToken cancellationToken = default);
}

public abstract record PreparedCandleDataResult
{
    public sealed record Succeeded(
        IReplayableCandleData Data,
        SourceIdentity Source,
        ResolvedCsvContext Csv,
        ScanCoverage Coverage) : PreparedCandleDataResult;

    public sealed record Failed(
        FatalDiagnostic Diagnostic) : PreparedCandleDataResult;
}
```

`PrepareAsync` hashes and reads the same stable source bytes. `Coverage` counts
every data record except an optional header and must satisfy
`PhysicalRowsExamined == AcceptedRows + MalformedRows`. The replayable data
retains the original timestamp text and source fields needed for detailed
evidence without retaining all rows in memory.

## Detailed Finding Catalog

The old `List<ValidationFinding>` report property is replaced by an
Application-owned normalized spool contract.

```csharp
public interface IDetailedFindingSink : IAsyncDisposable
{
    ValueTask AppendFindingAsync(
        DetailedFindingHeader finding,
        CancellationToken cancellationToken = default);

    ValueTask AppendLocationLineAsync(
        FindingReference finding,
        long sourceLine,
        CancellationToken cancellationToken = default);

    ValueTask AppendEvidenceAsync(
        FindingEvidenceRecord evidence,
        CancellationToken cancellationToken = default);

    ValueTask AppendRelationshipPairAsync(
        FindingRelationship forward,
        FindingRelationship reverse,
        CancellationToken cancellationToken = default);

    ValueTask<CompletedFindingCatalogResult> CompleteAsync(
        CancellationToken cancellationToken = default);
}

public interface ICompletedFindingCatalog : IAsyncDisposable
{
    FindingCatalogStatistics Statistics { get; }

    IAsyncEnumerable<DetailedFindingCursor> ReadCanonicalAsync(
        CancellationToken cancellationToken = default);
}

public interface IDetailedFindingCursor
{
    DetailedFindingHeader Header { get; }

    IAsyncEnumerable<long> ReadSourceLinesAsync(
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<FindingRelationship> ReadRelationshipsAsync(
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<FindingEvidenceRecord> ReadEvidenceAsync(
        CancellationToken cancellationToken = default);
}
```

Production Infrastructure stores headers, location lines, child evidence, and
relationship edges in separate bounded temporary runs. Readers expose one
canonical finding at a time and stream its children. A cursor's child sequences
must be consumed before advancing the parent enumerator; writers therefore need
only bounded serializer buffers even for one very large duplicate group or gap.

`AppendRelationshipPairAsync` accepts only inverse `PartOfGap` /
`ContainsMissingCandle` edges and persists them atomically. `CompleteAsync`
sorts, checks deterministic reference uniqueness, freezes the catalog, and
returns either a reader or a reconciliation fatal diagnostic.

### Catalog Statistics

```csharp
public sealed record FindingCatalogStatistics(
    CategoryStatistics MissingCandles,
    CategoryStatistics DuplicateRecords,
    CategoryStatistics InvalidOhlc,
    CategoryStatistics ClosedMarketRecords,
    CategoryStatistics TimeGaps,
    CategoryStatistics MalformedRows);

public sealed record CategoryStatistics(
    long EntryCount,
    long ContributionSum);
```

Statistics are maintained with constant-size counters. Every appended finding
has a positive contribution. The completed values are the authoritative input to
report reconciliation.

## Evidence Records

`FindingEvidenceRecord` is a closed discriminated union for v2:

```text
FindingEvidenceRecord
  |- MissingCandle
  |- TimeGapHeader
  |- TimeGapMissingReference
  |- DuplicateHeader
  |- DuplicateDifferingField
  |- DuplicateRow
  |- InvalidOhlcValues
  |- InvalidOhlcViolation
  |- ClosedMarket
  |- MalformedHeader
  |- MalformedFieldError
  `- MalformedSkippedCheck
```

Splitting repeated children into records is mandatory. No evidence record may
contain a collection whose size grows with source rows or findings. Every record
has its owning `FindingReference` and a deterministic child ordering key.

Stable OHLC violation, malformed reason, relationship, check, failure, and stage
codes are value objects validated in Domain/Application, not serializer strings
invented by writers.

## Report Assembly and Reconciliation

```csharp
public interface IReportReconciler
{
    ReportReconciliationResult Reconcile(
        ValidationSummary summary,
        ScanCoverage coverage,
        IReadOnlyList<CheckExecution> checks,
        FindingCatalogStatistics catalog);
}
```

Successful reconciliation requires:

1. `PhysicalRowsExamined == AcceptedRows + MalformedRows`.
2. Exactly one execution entry for each established check.
3. No successful check has status `NotCompleted`.
4. Every category summary equals its catalog contribution sum.
5. Malformed summary, coverage malformed rows, and malformed catalog
   contribution sum are equal.
6. `Clean` is selected exactly when every category count is zero.

Failure returns `REPORT_RECONCILIATION_FAILED` at stage `Reconciliation` and no
`DetailedValidationReport`.

## Report Writers

```csharp
public enum ReportRepresentation
{
    ConciseText,
    DetailedText,
    JsonV1,
    JsonV2
}

public interface ISuccessReportWriter
{
    ReportRepresentation Representation { get; }

    ValueTask WriteAsync(
        DetailedValidationReport report,
        Stream destination,
        CancellationToken cancellationToken = default);
}

public interface IFatalDiagnosticWriter
{
    FatalRepresentation Representation { get; }

    ValueTask WriteAsync(
        FatalDiagnostic diagnostic,
        Stream standardError,
        CancellationToken cancellationToken = default);
}
```

- `ConciseText` preserves exactly the existing six summary lines.
- `DetailedText` carries the same substantive fields as JSON v2, grouped for
  human scanning, and escapes source strings with invariant JSON-style quoting.
- `JsonV1` preserves the feature-001 schema and field shape unchanged.
- `JsonV2` matches `detailed-report-v2.schema.json` and always includes complete
  details.
- Writers stream UTF-8 without a BOM, leave the destination open, do not access
  filesystem/console statically, and never build the whole report as a string.

## Atomic Publication Port

```csharp
public interface IReportPublisher
{
    ValueTask<ReportPublicationResult> PublishAsync(
        ISuccessReportWriter writer,
        DetailedValidationReport report,
        ReportDestination destination,
        CancellationToken cancellationToken = default);
}
```

The Infrastructure implementation renders to an Application-owned temporary
artifact first. Only a fully rendered and flushed artifact may be copied to
stdout or atomically replace a file destination. Input/output aliases are
rejected before source preparation. A report-render or commit failure returns an
operational fatal diagnostic and leaves a selected file destination uncreated or
unchanged.

## Fatal Diagnostic Registry

Stable v2 codes include:

| Code | Class | Stage |
|---|---|---|
| `INVALID_ARGUMENT` | Configuration | ArgumentValidation |
| `SOURCE_UNAVAILABLE` | Operational | SourceIdentity |
| `SOURCE_CHANGED` | Operational | Ingestion |
| `INVALID_ENCODING` | Dataset | Ingestion |
| `INVALID_CSV` | Dataset | Ingestion |
| `INVALID_STRUCTURE` | Dataset | Ingestion |
| `AMBIGUOUS_DELIMITER` | Configuration | Ingestion |
| `AMBIGUOUS_TIMEFRAME` | Configuration | TimeframeResolution |
| `INVALID_CALENDAR` | Configuration | ArgumentValidation |
| `VALIDATION_INCOMPLETE` | Operational | Validation |
| `REPORT_RECONCILIATION_FAILED` | Operational | Reconciliation |
| `REPORT_RENDER_FAILED` | Operational | ReportRendering |
| `REPORT_COMMIT_FAILED` | Operational | ReportCommit |

Diagnostics expose safe, actionable text and trustworthy source/location fields
only. They never serialize exception type names, stack traces, absolute paths,
partial summary counts, `isClean`, or a complete-findings claim.

## Application Invariants

1. Identical source bytes and resolved options produce identical public fields
   and finding order; there is no wall-clock field or random public ID.
2. Every successful report has complete findings for all `Completed` checks and
   no `NotCompleted` check.
3. Every detailed category reconciles its contribution sum to the established
   summary meaning.
4. Related missing-candle and time-gap findings retain both categories and both
   relationship directions.
5. Source values remain typed data until a writer escapes them.
6. Application and Domain never access files, console, current culture, host time
   zones, environment variables, or system time directly.
7. Temporary readers and report objects are disposed on all terminal paths.