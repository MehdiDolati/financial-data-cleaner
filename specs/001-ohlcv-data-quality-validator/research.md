# Phase 0 Research: OHLCV Data-Quality Validator

All feature clarifications in the specification are resolved. The decisions
below settle implementation choices and edge behavior needed for an actionable
design; no open clarification markers remain.

## 1. Runtime and Project Boundaries

**Decision**: Target C# 14 / .NET 10 with four projects: Domain, Application,
Infrastructure, and CLI. Domain is BCL-only; Application references Domain;
Infrastructure implements Application ports; CLI hosts commands and composition.

**Rationale**: This directly implements NFR-001 through NFR-004 and allows a
future API to invoke the same Application use case with different adapters.
Project references and architecture tests can enforce the dependency direction.

**Alternatives considered**: A single CLI project was rejected because it would
couple validation to I/O. A fifth generic-host project was rejected because the
CLI already serves as the composition root and the extra assembly adds no v1
behavior.

## 2. CSV Parsing and Encoding

**Decision**: Use CsvHelper for CSV record tokenization, while explicit adapter
code owns delimiter detection, layout validation, exact date/time parsing, and
invariant `decimal` conversion. Accept strict UTF-8 with or without a UTF-8 BOM;
reject UTF-16/UTF-32 BOMs and invalid UTF-8 byte sequences as fatal ingestion
errors.

**Rationale**: A mature parser correctly handles quoted fields and escaped
delimiters, avoiding an ad hoc CSV grammar. Keeping field semantics outside the
library preserves explicit fatal-versus-malformed behavior. Strict decoder
fallback makes the non-UTF-8 requirement observable.

**Alternatives considered**: `string.Split` was rejected because it is not valid
CSV parsing. Automatic encoding fallback was rejected because it makes the same
file parse differently across hosts.

## 3. Delimiter and Layout Detection

**Decision**: Test comma, semicolon, and tab against the first logical records.
Select a delimiter only when exactly one candidate consistently satisfies the
active fixed-width or header-name layout. An explicit `--delimiter` bypasses
detection. Zero or multiple valid candidates are fatal with an instruction to
provide the override, except that a zero-byte headerless file is a valid empty
dataset and needs no delimiter. `--header` still requires a physical header;
a required-header-only file with no data rows is valid.

**Rationale**: Structural evidence is deterministic. Failing on ambiguity follows
the constitution's no-guessing rule. Headerless Date+Time requires at least seven
fields; a combined timestamp requires at least six; extra trailing fields are
ignored. Any data row with fewer active-layout fields is structurally invalid.

**Alternatives considered**: Choosing the delimiter with the highest raw count
was rejected because delimiters may occur inside quoted values. Always defaulting
to comma conflicts with FR-006.

## 4. Bounded-Memory Chronological Normalization

**Decision**: The CSV adapter prepares a replayable dataset using external merge
sort. It parses into bounded chunks, sorts each chunk by `(TimestampUtc,
SourceLine)`, writes binary temporary runs, then performs a bounded-fan-in merge
to one replayable candle spool. Malformed rows are written to a separate spool.
All files are accessed through Application-owned temporary-storage abstractions
and removed on disposal or failure.

**Rationale**: FR-007 requires sorting arbitrary input before sequence checks,
while NFR-020 forbids memory growth proportional to file size. A stable
source-line tie-breaker also makes duplicate detail deterministic. Replay is
needed because timeframe detection precedes missing-candle evaluation.

**Alternatives considered**: In-memory `OrderBy` was rejected for linear memory.
Rejecting unsorted input violates FR-007. A database-backed sort was rejected as
unnecessary persistent infrastructure for a one-file offline tool.

## 5. Timeframe Detection and Expected Sequence

**Decision**: Explicit timeframe codes parse as positive `M<n>`, `H<n>`, or
`D<n>` durations. Auto-detection counts positive deltas between adjacent distinct
open-market records only when the interval does not traverse a closed session;
the unique statistical mode wins. If no delta exists, sequence checks are not
applicable and the reported timeframe is null. If multiple deltas tie for mode,
validation fails with an actionable request for `--timeframe`.

Expected timestamps are generated within calendar open sessions, aligned to the
session opening instant and clipped to the earliest and latest open-market source
records. Always-open crypto uses the earliest open record as its phase anchor.
Closed-period records are excluded from sequence bounds and matching, ensuring
they neither create nor close gaps. Open intervals use `[open, close)` semantics.

**Rationale**: Two passes over the replayable spool resolve timeframe before gap
analysis. Unique-mode failure is deterministic and safer than a hidden tie-break.
Session alignment handles equity DST transitions and the forex weekend boundary.
Clipping prevents inventing missing candles before the observed dataset begins.

**Alternatives considered**: Selecting the smallest tied delta was rejected as
guessing. Advancing a single UTC anchor forever was rejected because New York
session opens move in UTC across DST. Including weekend records as bounds would
contradict FR-018.

## 6. Market Calendars and Time Zones

**Decision**: Implement `forex`, `equities`, `crypto`, and `custom` behind an
Application calendar contract. Forex is the fixed Sunday 22:00 UTC through
Friday 22:00 UTC session. Crypto is always open. Equities and custom schedules
use local weekly sessions plus a pinned NodaTime/TZDB adapter; equities defaults
to `America/New_York` Monday-Friday 09:30-16:00. Add
`--calendar-config <path>` for the custom profile and optional equities-hours
override. Holidays remain unsupported.

**Rationale**: A bundled, pinned TZDB gives consistent IANA-zone behavior across
Windows, Linux, and macOS. The extra CLI option is the concrete input mechanism
required by FR-019's configurable/custom sessions.

**Alternatives considered**: Host `TimeZoneInfo` data was rejected because OS
zone identifiers and update levels differ. Treating equities as a fixed offset
would be wrong across DST. Embedding exchange holidays is explicitly out of
scope.

## 7. Rule Execution and Finding Storage

**Decision**: Implement each check as an independently testable rule. Per-record,
duplicate, and sequence rules consume the replayable sorted dataset and emit
findings to an Application-owned finding-store port. Use an in-memory test
adapter and a temporary streaming Infrastructure adapter. Summary counters use
`long`. Writers replay findings in a canonical category/order sequence.

**Rationale**: This satisfies the Open/Closed Principle and keeps output memory
bounded even when every input row is defective. Summary counts need not equal the
number of finding objects: a duplicate-group finding contributes `group size -
1`, while one time-gap finding can describe many missing-candle findings.

**Alternatives considered**: Accumulating `List<ValidationFinding>` was rejected
because JSON detail can grow linearly with defects. Combining all checks in one
orchestrator was rejected because it weakens independent tests and extension.

## 8. Reporting and Determinism

**Decision**: Define Application-owned report-writer contracts with text and JSON
Infrastructure implementations. JSON always includes all findings; `--verbose`
controls text detail only. Findings are ordered by the six summary-category
order, then timestamp, source line, and stable insertion key. Numeric and time
formatting is invariant; timestamps use UTC ISO 8601. `sourceFile` is the base
file name, avoiding machine-specific absolute paths.

**Rationale**: Streaming writers support large reports, canonical order enables
reproducible tests, and the JSON schema becomes a stable contract for CI and
future modules.

**Alternatives considered**: Serializing a fully materialized object graph was
rejected for memory use. Emitting findings in rule completion order was rejected
because concurrency or registration order could alter output.

## 9. CLI and Exit Semantics

**Decision**: Use System.CommandLine for binding, help, examples, and conflicts.
Exit `0` after a successful all-zero report, `1` after a successful report with
findings, and `2` for invalid arguments/configuration, I/O/report failures,
ambiguous timeframe, or fatal ingestion. When `--output` is used, write the full
report there and print one invariant one-line summary to stdout; otherwise stdout
contains only the selected report, preserving JSON purity.

**Rationale**: This keeps the three-code contract exhaustive and CI-friendly.
Argument validation happens before CSV preparation as required by FR-041.

**Alternatives considered**: Additional exit codes were rejected because FR-033
defines exactly three. Printing diagnostics alongside stdout JSON was rejected
because it breaks AS-09; fatal diagnostics go to stderr.

## 10. Testing and Coverage

**Decision**: Use xUnit theories and FluentAssertions for boundaries, CsvHelper
fixture integration tests, and process-level CLI tests. Coverlet collects line
and branch coverage; CI fails below 100% for Domain and Application while
excluding generated code. Infrastructure and CLI are gated by their respective
integration/E2E suites rather than percentage thresholds.

**Rationale**: This matches NFR-010 through NFR-014 and NFR-011a. Theory data will
name exact boundaries: equal OHLC values, duplicate groups of three, Friday
22:00/Sunday 22:00 UTC, session/DST transitions, malformed fields, and mode ties.

**Alternatives considered**: Snapshot-only tests were rejected because they do
not isolate rule boundaries. Applying 100% to adapter/composition code was
rejected because the specification explicitly scopes that gate to business
logic.