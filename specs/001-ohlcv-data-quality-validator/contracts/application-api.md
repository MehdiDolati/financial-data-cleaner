# Application Contract

This contract describes the reusable boundary that the CLI and future front
ends consume. Names are normative at the concept level; exact namespaces may be
finalized during implementation without changing dependency direction.

## Use Case

```csharp
public interface IValidateMarketDataUseCase
{
    ValueTask<ValidationExecution> ExecuteAsync(
        ValidationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ValidationRequest(
    string SourceLabel,
    ICandleSource CandleSource,
    ValidationOptions Options);

public abstract record ValidationExecution
{
    public sealed record Succeeded(ValidationReport Report) : ValidationExecution;
    public sealed record Failed(FatalValidationError Error) : ValidationExecution;
}
```

- `SourceLabel` is safe metadata such as a base file name, not an absolute path.
- The caller supplies an `ICandleSource`; therefore Application never opens a
  path or reads a request stream statically.
- A future API can construct a stream-backed source and invoke this use case
  without changing Domain or Application.
- Expected bad input is represented as `Failed`; exceptions are reserved for
  programming faults and cancellation.

## Input and Replay Ports

```csharp
public interface ICandleSource
{
    ValueTask<PreparedCandleData> PrepareAsync(
        CsvInputOptions options,
        CancellationToken cancellationToken);
}

public interface IReplayableCandleData : IAsyncDisposable
{
    IAsyncEnumerable<PriceCandle> ReadCandlesAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<MalformedRow> ReadMalformedRowsAsync(CancellationToken cancellationToken);
    CandleDataStatistics Statistics { get; }
}

public sealed record PreparedCandleData(IReplayableCandleData Data);
```

`ReadCandlesAsync` may be called multiple times and always yields canonical
`(TimestampUtc, SourceLine)` order. Preparing the source performs strict UTF-8
decoding, CSV/layout validation, row parsing, and bounded external sorting.
Infrastructure owns the file/stream implementation but implements these
Application-defined ports.

## Calendar and Time-Zone Ports

```csharp
public interface IMarketCalendar
{
    bool IsOpen(DateTimeOffset timestampUtc);
    IAsyncEnumerable<UtcSession> GetSessionsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);
}

public interface IMarketCalendarFactory
{
    ValueTask<CalendarResolution> CreateAsync(
        MarketCalendarDefinition definition,
        CancellationToken cancellationToken);
}

public interface ITimeZoneScheduleExpander
{
    ValueTask<ScheduleExpansion> ExpandAsync(
        string ianaTimeZoneId,
        IReadOnlyList<WeeklySession> sessions,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);
}
```

The NodaTime implementation belongs to Infrastructure. Unknown IANA IDs,
overlapping sessions, and ambiguous/skipped local boundaries return typed fatal
configuration errors; Application does not consult host time-zone globals.

## Validation Rule Port

```csharp
public interface IValidationRule
{
    FindingCategory Category { get; }

    ValueTask EvaluateAsync(
        ValidationContext context,
        IFindingSink findings,
        CancellationToken cancellationToken);
}

public sealed record ValidationContext(
    IReplayableCandleData Data,
    IMarketCalendar Calendar,
    Timeframe? Timeframe);
```

Rule implementations are independently registered and tested. A rule emits
findings only for its declared category. Sequence rules are skipped when the
timeframe is null. Rule registration order must not affect report ordering.

## Finding Store Port

```csharp
public interface IFindingSink : IAsyncDisposable
{
    ValueTask AppendAsync(ValidationFinding finding, CancellationToken cancellationToken);
    ValueTask<IFindingReader> CompleteAsync(CancellationToken cancellationToken);
}

public interface IFindingReader : IAsyncDisposable
{
    IAsyncEnumerable<ValidationFinding> ReadCanonicalAsync(
        CancellationToken cancellationToken);
}
```

Production Infrastructure uses bounded temporary storage; tests may use an
in-memory implementation. `CompleteAsync` freezes the set. Canonical read order
is category order from `data-model.md`, timestamp (null last), first source line
(empty last), then stable sequence.

## Reporting Port

```csharp
public interface IReportWriter
{
    ReportFormat Format { get; }

    ValueTask WriteAsync(
        ValidationReport report,
        Stream destination,
        ReportWriteOptions options,
        CancellationToken cancellationToken);
}

public sealed record ReportWriteOptions(bool Verbose);
```

- Text ignores findings unless `Verbose == true` and always starts with exactly
  the six `Label: value` summary lines.
- JSON ignores `Verbose` and always emits the complete finding sequence matching
  [`validation-report.schema.json`](validation-report.schema.json).
- Writers leave the destination open and do not access console or filesystem
  statically.

## Fatal Error Contract

```csharp
public enum FatalErrorKind
{
    InvalidConfiguration,
    SourceUnavailable,
    InvalidEncoding,
    InvalidCsv,
    InvalidStructure,
    AmbiguousDelimiter,
    AmbiguousTimeframe,
    CalendarResolution,
    ReportWriteFailure
}

public sealed record FatalValidationError(
    FatalErrorKind Kind,
    string Message,
    long? SourceLine = null);
```

Messages are actionable, invariant English and safe for stderr. Fatal results
contain no `ValidationReport`; the Presentation layer maps every kind to exit 2.

## Application Invariants

1. All `PriceCandle` values have UTC timestamps before any rule receives them.
2. Row parsing failures are malformed findings; file grammar/layout failures are
   fatal and cannot coexist with a report.
3. Duplicate count is the sum of `group size - 1`, not group count.
4. Closed-period records are evaluated for `WeekendRecord` but excluded from
   timeframe deltas and expected-sequence matching.
5. Report generation is deterministic for equal input bytes and options.
6. Application and Domain never read files, environment variables, console,
   current culture, wall-clock time, or host time-zone state directly.