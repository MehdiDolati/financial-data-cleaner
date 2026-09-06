# Contract: Web Integration Boundary

**Feature**: 006-web-application-integration | **Plan**: [../plan.md](../plan.md)

The single explicit boundary through which a website invokes business use cases
and receives typed outcomes without owning their rules (FR-021). Names are
normative at the concept level; exact namespaces may be finalized during
implementation without changing dependency direction or observable behavior — the
same latitude feature 002's application contract established.

## Prohibited Types (normative)

The façade signature, its request records, and its outcome records MUST NOT
reference any of the following. This is the mechanically checkable form of
Principle II and FR-021:

- HTTP or web-server types (`HttpContext`, `HttpRequest`, `IFormFile`,
  `IActionResult`, `ControllerBase`, route/model-binding attributes)
- Session, cookie, claims, identity, or authorization types
- View, component, template, markup, or DOM types
- `System.IO` filesystem paths as *inputs* (streams are permitted; paths are not)
- `Console`, `Environment`, `DateTime.Now`, `CultureInfo.CurrentCulture`,
  `TimeZoneInfo.Local`

A compile-time or test-time assertion over the `Validator.Application` assembly's
references is the enforcement mechanism.

## Façade

```csharp
public interface IValidationWebService
{
    ValueTask<WebRunSubmission> SubmitAsync(
        WebRunRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<WebRunStatusResult> GetStatusAsync(
        WebRunId id,
        CancellationToken cancellationToken = default);

    ValueTask<WebResultRetrieval> GetResultAsync(
        WebRunId id,
        CancellationToken cancellationToken = default);

    ValueTask<WebExportResult> ExportAsync(
        WebRunId id,
        ReportRepresentation representation,
        Stream destination,
        CancellationToken cancellationToken = default);

    ValueTask<WebRunSubmission> RetryAsync(
        WebRunId id,
        CancellationToken cancellationToken = default);
}
```

- `SubmitAsync` validates options **before** any dataset byte is interpreted
  (FR-007), persists the run as `Pending`, and hands it to `IWebRunQueue`.
- `GetStatusAsync` is the polling surface backing the pending/progress state
  (FR-009). It is cheap and MUST NOT trigger work.
- `GetResultAsync` returns the typed view for a terminal run, or an explicit
  not-ready / unavailable outcome — never an empty success (FR-032).
- `ExportAsync` streams the machine-readable artifact using the **existing**
  report writers (FR-014); no new serializer is introduced.
- `RetryAsync` performs the only permitted `Failed → Pending` transition
  (FR-010).

## Request

```csharp
public sealed record WebRunRequest(
    WebRunOperation Operation,
    string SubmittedFileName,
    Stream Content,
    WebRunOptions Options,
    string? SubmittedBy = null);

public enum WebRunOperation
{
    Validate,
    EstablishBenchmark,
    Compare
}

public sealed record WebRunOptions(
    string? Timeframe,
    MarketProfile Market,
    string? CalendarReference,
    CsvInputOptions Csv,
    int ReportVersion,
    bool Score,
    string? ScoreWeights,
    string? Instrument,
    string? BenchmarkName,
    string? ToleranceOverrides);
```

- `Content` is a readable stream of the uploaded bytes. The façade never receives
  a filesystem path, which is what keeps the boundary transport-neutral and makes
  upload handling a host concern.
- `SubmittedFileName` is untrusted text. It is normalized through the existing
  `SourceIdentity`, which already rejects path components and never exposes an
  absolute path (FR-030).
- `WebRunOptions` MUST expose every option that materially affects a result
  (FR-003). The option set is intentionally the same surface the CLI documents:
  timeframe, market profile/calendar, timestamp interpretation, delimiter, header
  handling, source timestamp format and column, report version, scoring and
  weights, instrument, benchmark name, and tolerance overrides.
- `SubmittedBy` is opaque host correlation only. The boundary never interprets it
  and never uses it for authorization (research R6).

## Option Validation (pre-read)

`WebRunOptionsValidator` reuses the CLI's established rules and codes rather than
restating them. Each rejection is a `Failed` outcome carrying `INVALID_ARGUMENT`
with the specific correction required (FR-007, SC-003):

| Rule | Established source |
|---|---|
| `ReportVersion == 2` requires JSON representation | CLI: `--report-version 2` requires `--format json` |
| Score weights require scoring enabled | CLI: `--score-weights` requires `--score` |
| Scoring is unavailable under the frozen v1 JSON contract | CLI: `--score` + v1 JSON rejected |
| Benchmark establishment and comparison require scoring + v2 | CLI: `--benchmark` / `--compare` require `--score` |
| Benchmark establishment and comparison require an instrument identity | CLI: `--instrument` required |
| Tolerance overrides require a comparison | CLI: `--tolerances` requires `--compare` |
| Timeframe override must be a canonical `M<n>`/`H<n>`/`D<n>` code | `Timeframe.Parse` |
| CSV option combination must be self-consistent | `CsvInputOptions.Validate()` |
| Score weights must cover all six metrics | `ScoreWeightParser.Parse` |

Validation MUST complete before the content stream is interpreted, so a rejected
configuration produces no report and no partial work (Principle V).

## Outcomes

```csharp
public abstract record WebRunSubmission
{
    public sealed record Accepted(WebRunId Id, bool JoinedExistingRun)
        : WebRunSubmission;

    public sealed record Rejected(FatalDiagnostic Diagnostic)
        : WebRunSubmission;
}

public abstract record WebRunStatusResult
{
    public sealed record Known(WebRunId Id, WebRunStatus Status)
        : WebRunStatusResult;

    public sealed record Unavailable(WebRunId Id, string Reason)
        : WebRunStatusResult;
}

public abstract record WebResultRetrieval
{
    public sealed record Ready(WebResultView View) : WebResultRetrieval;
    public sealed record NotReady(WebRunStatus Status) : WebResultRetrieval;
    public sealed record Unavailable(string Reason) : WebResultRetrieval;
}

public abstract record WebExportResult
{
    public sealed record Written(ReportRepresentation Representation)
        : WebExportResult;

    public sealed record NotAvailable(string Reason) : WebExportResult;
}
```

- `Accepted.JoinedExistingRun` is how idempotency is observable: a refresh or
  double submission of identical bytes and options returns the same `WebRunId`
  with the flag set, creating no duplicate work and no duplicate benchmark
  (FR-010, spec edge case).
- Expected invalid input and environment failures return a `Rejected` /
  `Unavailable` outcome. Exceptions remain reserved for programming faults and
  cancellation — the same discipline feature 002 established.
- `NotReady` carries the real lifecycle status, so a caller can distinguish
  "still running" from "gone" from "failed" without guessing (FR-008).

## Delegation Rules

The façade composes; it does not decide. Normatively:

| Operation | Delegates to | Façade MUST NOT |
|---|---|---|
| `Validate` | `IDetailedValidationUseCase` | evaluate any check, count any category, or order any finding |
| Scoring | `ScoreRequest` on `ValidationOptions`, computed by the existing scoring pipeline | compute a metric, a population, a weight, or the average |
| `EstablishBenchmark` | `EstablishBenchmarkUseCase` + `IBenchmarkStore` | decide benchmark validity or overwrite an existing name |
| `Compare` | `CompareDatasetsUseCase` | resolve a tolerance, classify a discrepancy, or match timestamps |
| Export | existing `ISuccessReportWriter` / `IFatalDiagnosticWriter` | invent a field, a format, or a version |

Any behavior not listed above is out of the façade's authority. If a web scenario
appears to need a new rule, that is a signal to amend the owning use case under a
new spec — not to add logic at the boundary (FR-024, Principle VII).

## Ports

```csharp
public interface IWebRunStore
{
    ValueTask<WebRunRecord?> FindAsync(WebRunId id, CancellationToken ct = default);

    ValueTask<bool> TryCreateAsync(WebRunRecord record, CancellationToken ct = default);

    ValueTask TransitionAsync(
        WebRunId id,
        WebRunStatus target,
        WebRunTransitionData data,
        CancellationToken ct = default);
}

public interface IUploadedDatasetStore
{
    ValueTask<UploadedDataset> StoreAsync(
        string safeFileName,
        Stream content,
        CancellationToken ct = default);

    ValueTask<IPreparedCandleSource> OpenAsync(
        UploadedDataset dataset,
        CsvInputOptions options,
        CancellationToken ct = default);
}

public interface IWebRunQueue
{
    ValueTask EnqueueAsync(WebRunId id, CancellationToken ct = default);
}
```

- `TryCreateAsync` returns `false` when the deterministic id already exists; that
  is the duplicate-submission guard, not an error.
- `TransitionAsync` MUST reject a transition outside the table in
  [../data-model.md](../data-model.md) rather than coerce the state.
- `StoreAsync` is write-once and content-addressed; `OpenAsync` replays the exact
  stored bytes so validation reads what was hashed (SC-008).
- `EnqueueAsync` is only called after the record is durably `Pending`, so a crash
  between persist and enqueue leaves a recoverable pending run rather than a lost
  one.

## Benchmark Concurrency Amendment

`IBenchmarkStore.SaveAsync` gains an explicit contract, unchanged in signature:

> Establishment is atomic create-if-absent. If the name exists — including a
> concurrent in-flight establishment — the call MUST fail deterministically and
> leave no partial benchmark directory. Silent replacement is forbidden.

This is the deterministic resolution of the spec's concurrent-benchmark edge case
and preserves the immutability guarantee the CLI already relies on.

## Invariants

1. The façade adds no validation, scoring, tolerance, or benchmark rule (FR-024).
2. Every operation is reachable without the command line (FR-001, FR-004).
3. Options are validated before dataset processing (FR-007).
4. An unrequested capability is never silently enabled (FR-005).
5. Uploaded bytes and established benchmarks are never modified (FR-006).
6. A fatal outcome never carries partial counts, scores, or a report reference
   (FR-011).
7. Identical bytes + equivalent resolved options produce an identical `WebRunId`
   and an identical substantive result (SC-004).
8. `Validator.Domain` and `Validator.Cli` require no source change for this
   contract to exist (FR-022, FR-033).
