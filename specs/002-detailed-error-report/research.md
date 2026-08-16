# Phase 0 Research: Detailed Dataset Error Report

All specification clarifications are resolved. The decisions below convert the
requirements into an implementation-ready design without open clarification
markers.

## 1. Compatibility and Version Selection

**Decision**: Keep concise text as the default, keep `--format json` on the exact
v1 contract, and select detailed JSON only with
`--format json --report-version 2`. `--report-version` accepts `1` or `2`, is
valid only with JSON, and defaults to `1`. `--verbose` selects detailed text and
does not change either JSON contract.

**Rationale**: Explicit version selection satisfies FR-037 and protects strict
v1 consumers. Separating verbosity from JSON contract version avoids making a
presentation flag silently alter machine data.

**Alternatives considered**: Making v2 the new default was rejected as a breaking
change. Using `--verbose --format json` as v2 opt-in was rejected because verbose
currently has no JSON effect and does not communicate a contract version.

## 2. Complete Detail with Bounded Memory

**Decision**: Replace report-sized collections with a normalized replayable
finding catalog backed by temporary storage. Store finding headers, repeated
evidence rows/errors, and relationship edges separately, sort them by canonical
keys through bounded external merge runs, then expose repeatable sequential
readers. Category aggregates are maintained with constant-size counters.

**Rationale**: A duplicate group or one time gap may contain an unbounded number
of child rows/references. Keeping children in a `List<T>` would violate SC-006
even if top-level findings were spooled. Normalized child streams let writers
perform a sequential merge join and emit every required detail.

**Alternatives considered**: Materializing `ValidationReport.Findings` and a full
report string was rejected for linear memory. One JSONL object per finding was
rejected because a single duplicate/gap object could still grow with input size.
A database was rejected because external-sort spools are sufficient for one
offline run and require no persistent state.

## 3. Finding Identity and Relationships

**Decision**: Assign each finding a deterministic public reference from its
category and canonical identity key, encoded as invariant ASCII. Missing-candle
and time-gap references derive from expected UTC boundaries; duplicate references
use shared UTC timestamp plus lowest source line; row findings use category plus
physical source line. A stable collision ordinal is appended only when the full
canonical key is otherwise equal. Relationship edges are stored in both
directions and replayed with the related finding.

**Rationale**: References remain identical for identical source bytes and
configuration, can be created before rendering, and avoid random GUIDs or
position-dependent IDs. Bidirectional edge records satisfy FR-021 and allow one
gap to reference all missing candles without a large in-memory collection.

**Alternatives considered**: Random IDs violate reproducibility. Global output
ordinals are deterministic but require all canonical positions before rules can
link findings. Embedding child findings inside a gap would remove the established
missing-candle category and break reconciliation.

## 4. Report Status and Check Execution

**Decision**: Model successful reports as `Clean` or `FindingsDetected`; model
`Fatal` as a separate diagnostic aggregate that never contains final category
totals. Track each of the six established checks with `Completed`,
`NotApplicable`, or `NotCompleted`. Successful outcomes contain no
`NotCompleted`; inability to complete an applicable check transitions to fatal.
Sequence checks are `NotApplicable` when the resolved timeframe exists but fewer
than two open-market occupied timestamps bound an expected sequence.

**Rationale**: Separate success and fatal types prevent partial observations from
being serialized accidentally as a complete report. Explicit check status makes
empty/single-row behavior auditable without pretending work occurred.

**Alternatives considered**: A nullable status on the old report was rejected
because invalid state combinations remain constructible. Treating every empty
check as completed was rejected because it obscures when a check had no
applicable sequence.

## 5. Source Identity and Resolved Context

**Decision**: Infrastructure computes SHA-256 over exact source bytes and records
the safe base name and 64-bit byte size. The prepared source also returns the
resolved delimiter, header mode, timestamp mode/formats/column, source offset,
calendar profile/name/time zone/weekly sessions or definition fingerprint,
resolved timeframe, and evaluated UTC range. No absolute path is reported.

**Rationale**: SHA-256 is deterministic, available in the BCL, and strong enough
to identify archived dataset bytes. Recording resolved rather than merely
requested options lets repeated runs be compared meaningfully.

**Alternatives considered**: File name and size alone are collision-prone. File
modification time is platform-dependent and mutable. MD5/SHA-1 were rejected as
weaker fingerprints with no implementation advantage.

## 6. Category-Specific Evidence

**Decision**: Use a discriminated evidence model with one shape per category.
Common finding fields carry reference, category, title, explanation, count
contribution, location, suggested action, and relationships. Evidence carries:

- missing candle: timeframe, owning gap, adjacent observed timestamps;
- time gap: first/last missing timestamps, missing count, elapsed seconds,
  adjacent observations, and streamed missing-candle references;
- duplicate: exact/conflicting classification, every row's line/OHLCV values,
  and every differing field;
- invalid OHLC: all OHLCV values and all violated rule codes;
- closed market: calendar identity and applicable closed-boundary/rule detail;
- malformed row: every independent field error, original offending value,
  parsed timestamp when available, slot reservation, and skipped checks.

**Rationale**: Consumers can inspect facts without parsing prose, while common
fields keep rendering consistent. Rule codes and field names are stable contract
values; explanations and actions remain human-readable English.

**Alternatives considered**: A free-form dictionary weakens schema validation.
Message-only evidence violates FR-018. Echoing the full raw row was rejected
because it is unnecessary, may expose unrelated content, and is harder to render
safely.

## 7. Safe Source-Value Representation

**Decision**: JSON uses `Utf8JsonWriter`, which structurally escapes strings.
Detailed text renders source-derived strings with JSON-style quoting and escapes
CR, LF, tab, other controls, quotes, and backslashes. Values are not interpreted
as markup and no source text is emitted as an unprefixed report line.

**Rationale**: A source value cannot close a JSON field or masquerade as a text
heading/finding. Full offending values remain attributable without silent
truncation.

**Alternatives considered**: Raw interpolation was rejected as report injection.
Replacing controls with spaces loses evidence. Truncation was rejected because
the feature promises complete actionable detail.

## 8. Reconciliation Before Rendering

**Decision**: The completed finding catalog exposes per-category entry counts and
count-contribution sums. Application compares these values with the six summary
counts, validates scan coverage (`examined = accepted + malformed`), check-state
rules, positive contributions, deterministic IDs, and locally generated
bidirectional relationships before it creates a report-ready outcome. Failure
produces `REPORT_RECONCILIATION_FAILED` and no successful report.

**Rationale**: Constant-size category aggregates prove FR-014 without reading all
findings into memory. IDs and paired edge creation are enforced when appending,
so invalid relationships cannot enter a completed catalog.

**Alternatives considered**: Trusting writer output was rejected because a
contradictory report could be emitted. A global in-memory ID set was rejected for
linear memory; deterministic key construction and spool-level uniqueness checks
provide the invariant with bounded sorting.

## 9. Rendering and Atomic Completion

**Decision**: Writers stream to a caller-supplied destination and leave it open.
Infrastructure first renders a complete report to a temporary staged artifact.
After successful rendering and flush, it atomically replaces a file destination
or sequentially copies the staged artifact to stdout. Input/output alias checks
use normalized filesystem identity before source parsing. Temporary artifacts
are deleted on every terminal path.

**Rationale**: Staging prevents serializer/reconciliation failures from leaving a
file labeled as complete and keeps memory bounded. It also guarantees stdout is
empty when validation fails before report commit, as required for v2 fatal runs.

**Alternatives considered**: Rendering to `string` was rejected for memory.
Writing directly to the final file was rejected because failures leave partial
artifacts. Buffering the whole report in memory was rejected for SC-006.

## 10. Fatal Diagnostics and Stream Routing

**Decision**: Define stable fatal codes, classes (`Dataset`, `Configuration`,
`Operational`), stages, source location, corrective guidance, and six check
statuses in Application. Human/v1 runs receive escaped structured text on
stderr. A selected v2 run emits one document matching
`fatal-diagnostic-v2.schema.json` on stderr; stdout and any requested destination
remain untouched. Exit code remains `2`.

**Rationale**: One typed model supports both representations and distinguishes a
bad dataset from bad configuration or an environment/report failure. Separate
stdout/stderr contracts preserve automation and prevent fatal output from being
mistaken for a validation report.

**Alternatives considered**: Serializing exceptions exposes unstable types and
paths. Returning partial counts conflicts with fail-safe semantics. Reusing the
successful v2 schema for fatal output permits invalid clean/complete states.

## 11. Testing Strategy

**Decision**: Add table-driven tests for every evidence shape and fatal code;
contract-test both v2 schemas from local files; retain v1 golden/schema tests;
compare verbose text and v2 substantive fixtures; generate hostile-string,
large-gap, large-duplicate, and 100,000-finding fixtures outside the repository;
measure bounded buffers/working-set tolerance; and process-test stdout, stderr,
destination, exit code, cleanup, determinism, and source bytes.

**Rationale**: The highest risks are compatibility, completeness, state
misclassification, injection, and hidden materialization. Tests at Domain,
Application, Infrastructure, and CLI boundaries isolate each risk while meeting
the constitution's coverage requirements.

**Alternatives considered**: Snapshot-only testing was rejected because it does
not prove reconciliation or bounded memory. Hand-maintained giant fixtures were
rejected in favor of deterministic generators and small reviewed manifests.