# Feature: OHLCV / Forex CSV Data-Quality Validator

## 1. Overview

Build a command-line tool that inspects a CSV file of timestamped OHLCV (Open,
High, Low, Close, Volume) price data — typically forex, but potentially any
market — and reports on the *quality* of that data without altering it. The
tool answers one question for a trader, quant researcher, or data engineer
about to use a historical price file: "can I trust this data?" It never
repairs or rewrites data; it only detects and reports problems so the caller
can decide what to do next (discard the file, patch it, contact the vendor,
re-download, etc.).

The tool must be usable both as a human-run CLI command and as a library
invoked from other front ends later — for example a CI pipeline step, or a
future web service — without changing the validation logic itself (see
Non-Functional Requirements §4.1).

## 2. Primary Users

- A quant developer or trader vetting a historical price file before backtesting.
- A data engineer validating vendor CSV exports as part of an ingestion pipeline.
- A CI pipeline gating "promote this data file to production" on a clean report.

## 3. Functional Requirements

### 3.1 Input & Parsing

- **FR-001**: The system MUST accept the path to one CSV file as input (positional CLI argument).
- **FR-002**: The system MUST read, at minimum, seven physical columns per record, in the standard MT4/MetaTrader History Center export layout: `Date`, `Time`, `Open`, `High`, `Low`, `Close`, `Volume`.
- **FR-003**: The system MUST, by default, treat the file as headerless with columns in the fixed order from FR-002, comma-delimited — the shape of a raw MT4 History Center export. A `--header` flag MUST opt into header-based column matching (case-insensitive by name) for sources that add one.
- **FR-004**: The system MUST parse `Date` as `yyyy.MM.dd` and `Time` as `HH:mm` or `HH:mm:ss` (auto-detected by colon count), combining them into a single Timestamp. Both patterns MUST be overridable (`--date-format` / `--time-format`) for non-MT4 sources, and a single combined `--timestamp-format` MAY be supplied instead when the source uses one column rather than two.
- **FR-004a**: Source timestamps MUST be interpreted at a fixed UTC+2 offset by default, with no daylight-saving adjustment, overridable via `--tz-offset <±HH:mm>`, and converted to true UTC internally for all processing and reporting. (A fixed, non-DST offset avoids the spring-forward/fall-back artifacts noted in §8. Confirmed with the requester that, specifically at +2, this also makes the weekend boundary land on the calendar Saturday/Sunday — see the derivation under FR-019.)
- **FR-005**: The system MUST parse numeric fields (Open, High, Low, Close, Volume) using the invariant culture (`.` as decimal separator) regardless of the host machine's regional settings.
- **FR-006**: The system MUST auto-detect the field delimiter among comma, semicolon, and tab, with an explicit `--delimiter` override.
- **FR-007**: The system MUST normalize records into chronological order before running any sequence-dependent check (duplicates, gaps, missing candles), regardless of the order rows appear in the file.
- **FR-008**: If the file is structurally invalid (wrong column count for the active layout, file unreadable, not valid CSV), the system MUST stop and report a **fatal ingestion error** — distinct from a data-quality finding — and MUST NOT emit a data-quality report. This maps to its own exit code (see FR-033).
- **FR-009**: If an individual row fails to parse (e.g. non-numeric price field) while the file as a whole is structurally valid, the system MUST skip that row, continue processing the remainder of the file, and count it under a sixth top-line report category, **malformed rows** (line number + reason recorded for verbose mode).

### 3.2 Data-Quality Checks

- **FR-010**: The system MUST detect **duplicate records** — two or more rows sharing an identical timestamp. The reported count MUST equal the sum, over every group of rows sharing a timestamp, of (group size − 1); i.e. a timestamp appearing 3 times contributes 2 to the count, not 3.
- **FR-011** (verbose mode): For each duplicate group, the system MUST indicate whether the OHLCV values are identical across the group ("exact duplicate") or differ ("conflicting duplicate").
- **FR-012**: The system MUST detect **invalid OHLC** rows — any row where at least one of the following holds:
  - High < Open, High < Close, or High < Low
  - Low > Open, Low > Close, or Low > High
  - Open ≤ 0, High ≤ 0, Low ≤ 0, or Close ≤ 0
  - Volume < 0

  Each offending row is counted **once** regardless of how many of the above it violates.
- **FR-013** (verbose mode): For each invalid row, the system MUST list which specific rule(s) it violated and the offending values.
- **FR-014**: The system MUST determine a nominal **timeframe** (candle interval — e.g. M1, M5, M15, M30, H1, H4, D1) either from an explicit `--timeframe` override, or by auto-detecting the statistical mode of the time delta between chronologically consecutive records observed during open-market periods.
- **FR-015**: Using the timeframe from FR-014 and the active market calendar (FR-019), the system MUST construct the full sequence of timestamps expected between the first and last record, excluding any period the calendar marks as closed.
- **FR-016**: Every expected timestamp from FR-015 that has no matching record MUST be counted under **missing candles**.
- **FR-017**: A maximal *contiguous* run of one or more consecutive missing expected timestamps MUST be counted as exactly one **time gap**. ("Missing candles" is the sum of gap lengths; "time gaps" is the number of distinct gap runs — a file with a 10-candle hole and a separate 2-candle hole reports 12 missing candles across 2 time gaps.)
- **FR-018**: A record whose UTC-normalized timestamp (FR-004a) falls inside a period the active market calendar marks as closed MUST be counted under **weekend records**. This check is independent of FR-016/017: a weekend record neither closes a gap nor creates one, and a missing weekend timestamp is never counted as a missing candle.
- **FR-019**: The market calendar MUST be selectable via `--market <profile>`. v1 ships with:
  - `forex` (default): open continuously from Sunday 22:00 UTC to Friday 22:00 UTC; closed ("weekend") from Friday 22:00 UTC to Sunday 22:00 UTC. The rule itself stays UTC-based so it remains correct if `--tz-offset` is ever changed. *Confirmed with the requester: at the default fixed +2 offset specifically, 22:00 UTC lands exactly on local midnight at both ends (Fri 22:00 UTC = Sat 00:00 local; Sun 22:00 UTC = Mon 00:00 local), so for this data source the closed window coincides exactly with calendar Saturday + calendar Sunday, with no fractional-day spillover.*
  - `equities`: Mon–Fri 09:30–16:00 America/New_York (illustrative default; exchange hours are configurable).
  - `crypto`: always open (no weekend/closed concept; FR-018 never fires).
  - `custom`: caller supplies trading days/hours explicitly.
- **FR-020**: Market-calendar holiday exclusions are **not** part of v1 (see §9) — a holiday will surface as an ordinary time gap.

### 3.3 Reporting

- **FR-030**: The system MUST produce, by default, a plain-text summary report with exactly the six counts below in `Label: value` form:
  ```
  Missing candles: 12
  Duplicate records: 0
  Invalid OHLC: 3
  Weekend records: 48
  Time gaps: 2
  Malformed rows: 0
  ```
- **FR-031**: The system MUST support `--format json`, emitting a single JSON document with the same summary counts plus source metadata and a `findings` array (category, timestamp, line number, message) per issue, e.g.:
  ```json
  {
    "sourceFile": "EURUSD_H1_2024.csv",
    "detectedTimeframe": "H1",
    "totalRecords": 8760,
    "dateRange": { "from": "2024-01-01T00:00:00Z", "to": "2024-12-31T23:00:00Z" },
    "summary": {
      "missingCandles": 12,
      "duplicateRecords": 0,
      "invalidOhlc": 3,
      "weekendRecords": 48,
      "timeGaps": 2,
      "malformedRows": 0
    },
    "isClean": false,
    "findings": [
      { "category": "InvalidOhlc", "timestamp": "2024-03-05T14:00:00Z", "line": 1523, "message": "High (1.0850) is less than Low (1.0862)" }
    ]
  }
  ```
- **FR-032**: The system MUST support a `--verbose` flag that adds the per-finding detail above to the text report as well.
- **FR-033**: The system MUST return one of three process exit codes: `0` — clean run, zero findings; `1` — successful run with one or more findings; `2` — fatal ingestion error (FR-008). (No existing CI convention to match, per requester — this is the tool's own convention.)
- **FR-034**: The system MUST support `--output <path>` to write the report to a file; when supplied, the system SHOULD still print a one-line human summary to stdout.

### 3.4 CLI

- **FR-040**: The system MUST provide `--help` documenting every option with at least one example invocation.
- **FR-041**: The system MUST validate its own arguments (missing file, unknown market profile, conflicting flags, etc.) and fail fast with an actionable message before attempting to parse the CSV.

**CLI options at a glance**

| Option | Description | Default |
|---|---|---|
| `<input-file>` | Path to the CSV to validate (positional) | required |
| `--timeframe <code>` | Override auto-detected interval (M1, M5, M15, M30, H1, H4, D1, …) | auto-detect |
| `--market <profile>` | `forex` \| `equities` \| `crypto` \| `custom` | `forex` |
| `--date-format <fmt>` | Format of the `Date` column | `yyyy.MM.dd` (MT4) |
| `--time-format <fmt>` | Format of the `Time` column | `HH:mm[:ss]` (MT4) |
| `--timestamp-format <fmt>` | Format for a single combined column, if used instead of Date+Time | — |
| `--tz-offset <±HH:mm>` | Fixed offset the source timestamps are in | `+02:00` |
| `--delimiter <char>` | CSV field delimiter | auto-detect |
| `--header` | Treat the first row as a header and match columns by name | headerless MT4 layout assumed |
| `--format <text\|json>` | Report format | `text` |
| `--output <path>` | Write report to file | stdout only |
| `--verbose` | Include per-record detail | summary only |
| `--help` | Show usage | — |

## 4. Non-Functional Requirements

### 4.1 Architecture

- **NFR-001**: The solution MUST follow Clean Architecture with four layers:
  - **Domain** — entities/value objects and pure validation rules; zero external dependencies (no I/O, no framework, nothing beyond the BCL).
  - **Application** — use-case orchestration and port interfaces (e.g. `ICandleSource`, `IReportWriter`, `IValidationRule`); depends only on Domain.
  - **Infrastructure** — CSV parsing, file system access, report writers; implements the Application ports; depends on Application + external libraries.
  - **Presentation (CLI)** — composition root, argument parsing, console I/O; depends only on Application abstractions via dependency injection.
- **NFR-002**: Dependencies MUST point inward only (Presentation → Application → Domain; Infrastructure → Application's interfaces). Domain and Application projects MUST NOT reference any console/UI-specific package.
- **NFR-003**: The compiled Domain + Application assemblies MUST be sufficient, on their own, to drive the identical validation logic from a different front end (e.g. an ASP.NET Core minimal API endpoint) with no source changes to either assembly — only a new thin Presentation project referencing the same Application interfaces.
- **NFR-004**: All environment-touching concerns (file system, console, system clock if used) MUST be reached only through interfaces defined in Application and supplied via the composition root — never accessed statically from Domain or Application code.

*(Illustrative project layout — to be finalized in `/plan`, not a binding part of this spec):*
```
src/
  Validator.Domain/          entities, validation rules
  Validator.Application/     use cases, ports, DTOs
  Validator.Infrastructure/  CSV reader, report writers
  Validator.Cli/             Program.cs, arg parsing, DI wiring
tests/
  Validator.Domain.Tests/
  Validator.Application.Tests/
  Validator.Infrastructure.Tests/
  Validator.Cli.Tests/        end-to-end, invokes the built executable
```

### 4.2 Testability & Coverage

- **NFR-010**: Development MUST be test-first: for every unit of behavior, a failing test MUST exist before its implementation is written.
- **NFR-011**: Line and branch coverage of the Domain and Application layers MUST be 100%, enforced by CI (the build fails below threshold).
- **NFR-012**: Infrastructure MUST be covered by integration tests against real fixture CSV files, including malformed variants.
- **NFR-013**: The CLI MUST be covered by end-to-end tests that invoke the built executable against fixture files and assert on stdout and exit code.
- **NFR-014**: Every validation rule MUST have theory/table-driven tests enumerating its boundary conditions (e.g. High == Low, a timestamp exactly on the weekend boundary, a duplicate group of size 3).
- **NFR-011a**: Confirmed with the requester: the 100% gate in NFR-011 is scoped to business logic — Domain and Application — only. The CLI composition root (`Program.cs`) is DI-wiring, excluded from the line-coverage gate, and is covered instead by the end-to-end tests in NFR-013; the same applies to Infrastructure, which is covered by the integration tests in NFR-012 rather than the 100% gate.
- *(Suggested toolchain, to confirm in `/plan`): xUnit + FluentAssertions for tests; Coverlet + ReportGenerator for coverage, wired into CI.)*

### 4.3 Performance & Scalability

- **NFR-020**: The system MUST process the input file as a stream; memory use MUST NOT grow linearly with file size. Soft target only (not a priority metric, per requester): comfortably handle a multi-year, single-timeframe M1 history — on the order of a few million rows — without a noticeable memory-footprint increase; no specific latency requirement.

### 4.4 Portability

- **NFR-030**: The solution MUST target .NET 10 and run unmodified on Windows, Linux, and macOS.

### 4.5 Extensibility

- **NFR-040**: Each data-quality check MUST be an independently testable rule implementation so new checks can be added without modifying existing ones (Open/Closed Principle).
- **NFR-041**: Report output MUST go through a pluggable writer abstraction so new formats can be added without touching validation logic.

### 4.6 Configuration & Correctness

- **NFR-050**: All numeric and date/time parsing MUST be culture-invariant.
- **NFR-051**: Timeframe, market calendar, and format assumptions MUST be externally configurable, never hard-coded.

## 5. Key Entities

| Entity | Purpose | Key fields |
|---|---|---|
| `PriceCandle` | One parsed OHLCV record | Timestamp (UTC), Open, High, Low, Close, Volume, SourceLine |
| `ValidationFinding` | One reported issue | Category, Timestamp(s), SourceLine(s), Message |
| `ValidationReport` | Aggregate result of a run | SourceFile, DetectedTimeframe, DateRange, the six summary counts, Findings, IsClean |
| `MarketCalendar` | Defines open/closed periods | Profile name, trading days/hours, closed-period rule |
| `ValidationOptions` | Run configuration | InputPath, TimeframeOverride, MarketProfile, TimestampFormat, Delimiter, OutputFormat, Verbose |

## 6. Success Criteria

- Running the validator against a hand-built fixture file with known, deliberately injected defects (N duplicates, N invalid-OHLC rows, N missing candles across N gaps, N weekend rows, N malformed rows) reproduces those exact counts.
- Running the validator against a verified-clean fixture file reports all-zero counts and exits with the "clean" code.
- A throwaway minimal ASP.NET Core Web API project can reference the Domain + Application assemblies and reproduce a validation run with zero source changes to either assembly (proves NFR-003).
- CI fails the build whenever Domain or Application coverage drops below 100%.

## 7. Acceptance Scenarios

- **AS-01 Clean file**: Given gapless, weekday-only H1 candles with valid OHLC and no duplicates, when validated, then all five counts are 0 and the process exits with the "clean" code.
- **AS-02 Duplicate**: Given one timestamp appearing in two rows, when validated, then Duplicate records = 1, and verbose output lists both line numbers.
- **AS-03 Invalid OHLC**: Given a row with High < Low, when validated, then Invalid OHLC = 1 and verbose output names the violated rule.
- **AS-04 Single missing candle**: Given H1 data that jumps from 09:00 straight to 11:00 on a weekday, when validated, then Missing candles = 1 and Time gaps = 1.
- **AS-05 Multiple gaps**: Given one weekday run missing 13:00–15:00 (3 candles) and a separate weekday missing 09:00 (1 candle), when validated, then Missing candles = 4 and Time gaps = 2.
- **AS-06 Weekend data present**: Given a file that contains 48 hourly rows across a Saturday and Sunday, when validated, then Weekend records = 48, and those rows do not additionally affect Missing candles or Time gaps.
- **AS-07 Fatal ingestion error**: Given a CSV missing the Close column, when validated, then the system reports a fatal ingestion error, produces no data-quality counts, and exits with the fatal-error code.
- **AS-08 Timeframe auto-detection**: Given an M15 file with no `--timeframe` override, when validated, then the system infers a 15-minute interval from the modal gap between consecutive weekday timestamps.
- **AS-09 JSON output**: Given `--format json`, when validated, then stdout contains exactly one valid JSON document shaped as in FR-031, and nothing else.
- **AS-10 Unsorted input**: Given a file whose rows are not chronologically ordered, when validated, then the result is identical to running the equivalent pre-sorted file.
- **AS-11 Malformed row**: Given a file where one row has a non-numeric value in the Close column, when validated, then Malformed rows = 1, that row is excluded from all other checks, and the run completes normally over the remaining rows.

## 8. Edge Cases

- Empty file or header-only file — no data rows to evaluate.
- Single-row file — timeframe cannot be inferred; sequence-based checks (missing candles/time gaps) are not applicable.
- Mixed/irregular intervals within one file (e.g. M1 rows spliced into an H1 file) — auto-detected timeframe follows the modal spacing; gap counts against a minority interval are a known limitation.
- A timestamp sitting exactly on the configured weekend boundary — behavior must be deterministic and covered by an explicit boundary test.
- Local, DST-observing timestamps rather than UTC — spring-forward/fall-back can fabricate an apparent missing or duplicate hour; the default UTC assumption exists specifically to avoid this class of bug.
- Extra, unrecognized columns present in the file (e.g. spread, adjusted close) — ignored, not an error.
- Header present but columns in a different order than the default — matched by name, not position.
- Non-UTF-8 file encoding — treated as a fatal ingestion error.

## 9. Out of Scope (v1)

- Repairing, correcting, or rewriting the input data — detection and reporting only.
- Holiday-calendar awareness — only recurring weekly closures are modeled; a holiday shows up as an ordinary time gap.
- Batch processing of multiple files or a directory in one invocation.
- Statistical outlier / price-spike detection beyond the logical OHLC-consistency rules in FR-012.
- Live or streaming data sources — CSV batch files only.
- Any graphical or web front end — the Application/Domain layers are designed to support one later (NFR-003), but building it is not part of this feature.
- Non-English report text.

## 10. Clarifications

### Session 2026-08-05

- **Q (input shape)** → **A**: Standard MT4/MetaTrader History Center export — headerless, comma-delimited, separate `Date` (`yyyy.MM.dd`) and `Time` (`HH:mm[:ss]`) columns rather than one Timestamp column. → FR-002–FR-004.
- **Q (source timezone)** → **A**: Fixed UTC+2, no DST. → FR-004a. This also resolves the weekend boundary: at +2 specifically, it coincides exactly with calendar Saturday/Sunday. → FR-018, FR-019.
- **Q (malformed rows)** → **A**: Promoted to a sixth top-line report category rather than staying verbose-only. → FR-009, FR-030, FR-031.
- **Q (exit codes)** → **A**: No existing CI convention; adopted `0` / `1` / `2` as the tool's own. → FR-033.
- **Q (coverage scope)** → **A**: 100% applies to Domain + Application (business logic) only; `Program.cs` is DI-wiring, excluded, covered by end-to-end tests instead. → NFR-011a.
- **Q (performance target)** → **A**: Not a priority; soft target of a few million rows without unbounded memory growth, no hard latency requirement. → NFR-020.

All items from the original Open Questions list are resolved; none remain outstanding for this feature.
