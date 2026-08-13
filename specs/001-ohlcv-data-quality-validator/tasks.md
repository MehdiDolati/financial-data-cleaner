---
description: "Task list for OHLCV / Forex CSV Data-Quality Validator"
---

# Tasks: OHLCV / Forex CSV Data-Quality Validator

**Input**: Design documents from `/specs/001-ohlcv-data-quality-validator/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Test tasks ARE included because the specification mandates test-first development (spec.md NFR-010, Principle I. Test-First) and 100% Domain/Application coverage (NFR-011). Every behavior gets a failing test before implementation.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

Four-project Clean Architecture layout from plan.md, rooted at the repository root:

- Production: `src/Validator.Domain/`, `src/Validator.Application/`, `src/Validator.Infrastructure/`, `src/Validator.Cli/`
- Tests: `tests/Validator.Domain.Tests/`, `tests/Validator.Application.Tests/`, `tests/Validator.Infrastructure.Tests/`, `tests/Validator.Cli.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [X] T001 Create `FinancialDataCleaner.sln`, `Directory.Build.props` (net10.0, C# 14, nullable enable, warnings-as-errors, invariant globalization), and empty `Directory.Packages.props` at repository root
- [X] T002 Create the four production projects `src/Validator.Domain/Validator.Domain.csproj`, `src/Validator.Application/Validator.Application.csproj`, `src/Validator.Infrastructure/Validator.Infrastructure.csproj`, `src/Validator.Cli/Validator.Cli.csproj` with inward-only project references (Application→Domain, Infrastructure→Application, Cli→Application) and add them to the solution
- [X] T003 Create the four test projects `tests/Validator.Domain.Tests/`, `tests/Validator.Application.Tests/`, `tests/Validator.Infrastructure.Tests/` (with `Fixtures/`), `tests/Validator.Cli.Tests/` (with `Fixtures/`), each referencing its target project and add them to the solution
- [X] T004 [P] Configure central package management in `Directory.Packages.props` with pinned versions for CsvHelper, System.CommandLine, Microsoft.Extensions.DependencyInjection, NodaTime (pinned TZDB), xUnit, FluentAssertions, Coverlet, and ReportGenerator
- [X] T005 [P] Add `.editorconfig` and analyzer/style configuration at repository root enforcing invariant-culture and treat-warnings-as-errors rules across all projects
- [X] T006 [P] Add CI workflow `.github/workflows/ci.yml` running restore/build/test on Windows, Linux, and macOS with Coverlet 100% line+branch gate scoped to `[Validator.Domain]*` and `[Validator.Application]*`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core types, ports, DTOs, and test doubles that ALL user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T007 Add architecture dependency-direction tests in `tests/Validator.Domain.Tests/Architecture/DependencyRulesTests.cs` asserting Domain has no non-BCL references and Application references only Domain (per NFR-002)
- [X] T008 [P] Create `PriceCandle` immutable record with UTC-offset guard in `src/Validator.Domain/Candles/PriceCandle.cs` and theory tests in `tests/Validator.Domain.Tests/Candles/PriceCandleTests.cs`
- [X] T009 [P] Create `Timeframe` value object (parse/validate canonical `M<n>`/`H<n>`/`D<n>`, reject zero/negative/fractional/overflow) in `src/Validator.Domain/Timeframes/Timeframe.cs` and theory tests in `tests/Validator.Domain.Tests/Timeframes/TimeframeTests.cs`
- [X] T010 [P] Create `FindingCategory` enum with canonical order in `src/Validator.Domain/Findings/FindingCategory.cs` and ordering tests in `tests/Validator.Domain.Tests/Findings/FindingCategoryTests.cs`
- [X] T011 [P] Create `ValidationFinding` (with `CountContribution`, `StableSequence`) and `MalformedRow` records in `src/Validator.Domain/Findings/ValidationFinding.cs` and `src/Validator.Domain/Findings/MalformedRow.cs` with tests in `tests/Validator.Domain.Tests/Findings/ValidationFindingTests.cs`
- [X] T012 [P] Create `MarketProfile`, `WeeklySession` (non-overlap + strict ordering guard), `MarketCalendarDefinition`, and `UtcSession` (`[open, close)`) in `src/Validator.Domain/Calendars/` with tests in `tests/Validator.Domain.Tests/Calendars/MarketCalendarTests.cs`
- [X] T013 Define Application ports in `src/Validator.Application/Abstractions/` — `ICandleSource`, `IReplayableCandleData`, `PreparedCandleData`, `CandleDataStatistics`, `IValidationRule`, `ValidationContext`, `IFindingSink`, `IFindingReader`, `IReportWriter`, `IMarketCalendar`, `IMarketCalendarFactory`, `ITimeZoneScheduleExpander`, and `IValidateMarketDataUseCase` (interfaces only, per contracts/application-api.md)
- [ ] T009 [P] Create `Timeframe` value object (parse/validate canonical `M<n>`/`H<n>`/`D<n>`, reject zero/negative/fractional/overflow) in `src/Validator.Domain/Timeframes/Timeframe.cs` and theory tests in `tests/Validator.Domain.Tests/Timeframes/TimeframeTests.cs`
- [ ] T010 [P] Create `FindingCategory` enum with canonical order in `src/Validator.Domain/Findings/FindingCategory.cs` and ordering tests in `tests/Validator.Domain.Tests/Findings/FindingCategoryTests.cs`
- [ ] T011 [P] Create `ValidationFinding` (with `CountContribution`, `StableSequence`) and `MalformedRow` records in `src/Validator.Domain/Findings/ValidationFinding.cs` and `src/Validator.Domain/Findings/MalformedRow.cs` with tests in `tests/Validator.Domain.Tests/Findings/ValidationFindingTests.cs`
- [ ] T012 [P] Create `MarketProfile`, `WeeklySession` (non-overlap + strict ordering guard), `MarketCalendarDefinition`, and `UtcSession` (`[open, close)`) in `src/Validator.Domain/Calendars/` with tests in `tests/Validator.Domain.Tests/Calendars/MarketCalendarTests.cs`
- [ ] T013 Define Application ports in `src/Validator.Application/Abstractions/` — `ICandleSource`, `IReplayableCandleData`, `PreparedCandleData`, `CandleDataStatistics`, `IValidationRule`, `ValidationContext`, `IFindingSink`, `IFindingReader`, `IReportWriter`, `IMarketCalendar`, `IMarketCalendarFactory`, `ITimeZoneScheduleExpander`, and `IValidateMarketDataUseCase` (interfaces only, per contracts/application-api.md)
- [ ] T014 [P] Create `CsvInputOptions` and `ValidationOptions` records in `src/Validator.Application/Ingestion/CsvInputOptions.cs` and `src/Validator.Application/Validation/ValidationOptions.cs` with default/validation tests in `tests/Validator.Application.Tests/Options/OptionsTests.cs`
- [ ] T015 [P] Create `ValidationSummary`, `DateRange`, and `ValidationReport` (with derived `IsClean`) in `src/Validator.Application/Reporting/` with derivation tests in `tests/Validator.Application.Tests/Reporting/ValidationReportTests.cs`
- [ ] T016 [P] Create `FatalValidationError`, `FatalErrorKind`, `ReportFormat`, `ReportWriteOptions`, `ValidationRequest`, and `ValidationExecution` (Succeeded/Failed) in `src/Validator.Application/Abstractions/` per contracts/application-api.md
- [ ] T017 Create in-memory test doubles (`InMemoryCandleSource`, `InMemoryReplayableCandleData`, `InMemoryFindingSink`/`Reader`) in `tests/Validator.Application.Tests/Doubles/` to drive Application unit tests without Infrastructure

**Checkpoint**: Foundation ready — user story implementation can now begin

---

## Phase 3: User Story 1 - Validate a standard MT4 forex CSV and get a text quality report (Priority: P1) 🎯 MVP

**Goal**: A trader/quant points the CLI at a default headerless comma-delimited MT4 OHLCV file and gets the six-line text summary answering "can I trust this data?", with correct `0`/`1`/`2` exit codes and no source mutation.

**Independent Test**: Run the built CLI against `clean-forex-h1.csv` (expect all-zero counts, exit 0) and `known-defects.csv --timeframe H1 --verbose` (expect the fixture-manifest counts, exit 1), and against `missing-close-column.csv` (expect fatal diagnostic on stderr, no counts, exit 2). Covers AS-01–AS-08, AS-10, AS-11, AS-14.

### Tests for User Story 1 (write FIRST, ensure they FAIL) ⚠️

- [X] T018 [P] [US1] Duplicate-record rule theory tests (group sizes 2 and 3, exact vs conflicting, `n-1` counting) in `tests/Validator.Application.Tests/Rules/DuplicateRecordRuleTests.cs`
- [X] T019 [P] [US1] Invalid-OHLC rule theory tests (High<Low, `High==Low` boundary, zero/negative prices, negative volume, one count per row) in `tests/Validator.Application.Tests/Rules/InvalidOhlcRuleTests.cs`
- [X] T020 [P] [US1] Closed-market-record rule tests (Friday 21:59:59/22:00 and Sunday 21:59:59/22:00 UTC forex boundaries, independence from gaps) in `tests/Validator.Application.Tests/Rules/ClosedMarketRecordRuleTests.cs`
- [X] T021 [P] [US1] Missing-candle + expected-sequence tests (single missing, multiple contiguous, closed periods excluded, malformed-with-timestamp reserves slot) in `tests/Validator.Application.Tests/Rules/MissingCandleRuleTests.cs`
- [X] T022 [P] [US1] Time-gap rule tests (one gap per maximal contiguous run; 12 missing across 2 gaps) in `tests/Validator.Application.Tests/Rules/TimeGapRuleTests.cs`
- [X] T023 [P] [US1] Timeframe-detection tests (modal delta over open-market records, tie/no-delta → fatal, override wins) in `tests/Validator.Application.Tests/Timeframes/TimeframeDetectorTests.cs`
- [X] T024 [P] [US1] Text report writer contract test (exact six `Label: value` lines and order) in `tests/Validator.Infrastructure.Tests/Reporting/TextReportWriterTests.cs`
- [X] T025 [P] [US1] CSV ingestion integration test for default MT4 headerless layout, invariant parsing, and malformed-vs-fatal split in `tests/Validator.Infrastructure.Tests/Csv/CsvIngestionTests.cs`
- [X] T026 [P] [US1] External-sort/replay integration test proving unsorted input yields identical canonical order to pre-sorted input (AS-10) in `tests/Validator.Infrastructure.Tests/Sorting/ExternalSortReplayTests.cs`
- [X] T027 [P] [US1] Use-case integration tests (clean, defects, fatal ingestion, timeframe-inference failure) using in-memory doubles in `tests/Validator.Application.Tests/UseCases/ValidateMarketDataUseCaseTests.cs`
- [X] T028 [P] [US1] CLI end-to-end tests asserting stdout six lines and exit codes 0/1/2 against fixtures in `tests/Validator.Cli.Tests/CoreValidationE2ETests.cs`

### Implementation for User Story 1

- [X] T029 [US1] Implement timeframe-detection service (modal open-market delta; fatal on tie/none) in `src/Validator.Application/Validation/TimeframeDetector.cs`
- [X] T030 [US1] Implement built-in forex calendar (`IMarketCalendar`, Sun 22:00–Fri 22:00 UTC) and expected-session/candle generator in `src/Validator.Application/Validation/ExpectedSequenceGenerator.cs` and `src/Validator.Infrastructure/Calendars/ForexCalendar.cs`
- [X] T031 [P] [US1] Implement `DuplicateRecordRule` in `src/Validator.Application/Validation/Rules/DuplicateRecordRule.cs`
- [X] T032 [P] [US1] Implement `InvalidOhlcRule` in `src/Validator.Application/Validation/Rules/InvalidOhlcRule.cs`
- [X] T033 [P] [US1] Implement `ClosedMarketRecordRule` in `src/Validator.Application/Validation/Rules/ClosedMarketRecordRule.cs`
- [X] T034 [US1] Implement `MissingCandleRule` (consumes expected sequence from T030) in `src/Validator.Application/Validation/Rules/MissingCandleRule.cs`
- [X] T035 [US1] Implement `TimeGapRule` (consumes expected sequence from T030) in `src/Validator.Application/Validation/Rules/TimeGapRule.cs`
- [X] T036 [US1] Implement CSV ingestion adapter for default MT4 headerless comma layout with invariant `decimal`/date parsing and malformed-vs-fatal classification in `src/Validator.Infrastructure/Csv/CsvCandleSource.cs`
- [X] T037 [US1] Implement bounded external merge sort with Application-owned temporary-storage port + adapter in `src/Validator.Infrastructure/Sorting/ExternalMergeSort.cs` and `src/Validator.Infrastructure/Sorting/TempStorage.cs`
- [X] T038 [US1] Implement streaming finding spool adapter (`IFindingSink`/`IFindingReader`, canonical read order) in `src/Validator.Infrastructure/Findings/SpoolingFindingStore.cs`
- [X] T039 [US1] Implement text report writer (six summary lines) in `src/Validator.Infrastructure/Reporting/TextReportWriter.cs`
- [X] T040 [US1] Implement `IValidateMarketDataUseCase` orchestrator (ingest → resolve timeframe → run registered rules → aggregate report) in `src/Validator.Application/Validation/ValidateMarketDataUseCase.cs`
- [X] T041 [US1] Implement CLI positional `<input-file>` + `--timeframe`, argument validation, exit-code mapping, and DI composition root in `src/Validator.Cli/Commands/ValidateCommand.cs` and `src/Validator.Cli/Program.cs`
- [X] T042 [P] [US1] Add fixtures `clean-forex-h1.csv`, `known-defects.csv` (+ adjacent counts manifest), and `missing-close-column.csv` in `tests/Validator.Cli.Tests/Fixtures/`

**Checkpoint**: MVP complete — core forex CSV validation works end-to-end and is independently testable

---

## Phase 4: User Story 2 - Machine-readable JSON and file output for pipelines/CI (Priority: P2)

**Goal**: A data engineer/CI job emits a schema-conformant JSON report (`--format json`), writes it to a file (`--output`), and optionally expands text detail (`--verbose`) so a build step can gate on a clean report.

**Independent Test**: Run `known-defects.csv --timeframe H1 --format json`; stdout is exactly one JSON document validating against `validation-report.schema.json`, exit 1. Run with `--output report.json`; stdout is the one-line completion summary and the fixture is byte-for-byte unchanged. Covers AS-09 and the output-file scenario.

### Tests for User Story 2 (write FIRST, ensure they FAIL) ⚠️

- [X] T043 [P] [US2] JSON report writer contract test validating output against `contracts/validation-report.schema.json` (all six counts, metadata, canonical findings) in `tests/Validator.Infrastructure.Tests/Reporting/JsonReportWriterTests.cs`
- [X] T044 [P] [US2] CLI E2E tests for JSON stdout purity (AS-09), `--output` one-line summary, and `--verbose` text detail in `tests/Validator.Cli.Tests/OutputFormatE2ETests.cs`

### Implementation for User Story 2

- [X] T045 [US2] Implement streaming JSON report writer conforming to `validation-report.schema.json` in `src/Validator.Infrastructure/Reporting/JsonReportWriter.cs`
- [X] T046 [US2] Add verbose finding-detail rendering to the text writer in `src/Validator.Infrastructure/Reporting/TextReportWriter.cs`
- [X] T047 [US2] Add `--format`, `--output` (atomic write + one-line summary), and `--verbose` options with report-writer selection to `src/Validator.Cli/Commands/ValidateCommand.cs`
- [X] T048 [P] [US2] Wire a local (no-network) JSON Schema test dependency and shared schema-assertion helper in `tests/Validator.Cli.Tests/Support/SchemaValidation.cs`

**Checkpoint**: JSON + file + verbose output work; US1 and US2 both independently testable

---

## Phase 5: User Story 3 - Flexible input parsing (headers, delimiters, combined timestamp, offset) (Priority: P3)

**Goal**: Support non-MT4 sources via `--header`, `--delimiter`, `--date-format`, `--time-format`, `--timestamp-format` + `--timestamp-column`, and `--tz-offset`, failing fast on conflicting/ambiguous options.

**Independent Test**: Run `header-semicolon.csv --header --delimiter semicolon` and `combined-timestamp.csv --header --timestamp-format "yyyy-MM-dd HH:mm:ss" --timestamp-column Timestamp --tz-offset +00:00`; both parse to their fixture-manifest counts. Supplying only one combined-timestamp option, or a name selector without `--header`, fails before CSV parsing (AS-13).

### Tests for User Story 3 (write FIRST, ensure they FAIL) ⚠️

- [X] T049 [P] [US3] Delimiter auto-detection tests (comma/semicolon/tab, quoted delimiters, zero/multiple candidates → fatal) in `tests/Validator.Infrastructure.Tests/Csv/DelimiterDetectionTests.cs`
- [X] T050 [P] [US3] Header-mode tests (case-insensitive, reordered columns, extra columns ignored, missing/duplicate → fatal) in `tests/Validator.Infrastructure.Tests/Csv/HeaderLayoutTests.cs`
- [X] T051 [P] [US3] Combined-timestamp and conflicting-option validation tests (index/name selector, missing pair → fatal) in `tests/Validator.Application.Tests/Options/CsvOptionValidationTests.cs`
- [X] T052 [P] [US3] `--tz-offset` conversion tests (fixed offset, ±14:00 bound, correct UTC normalization) in `tests/Validator.Infrastructure.Tests/Csv/TimeZoneOffsetTests.cs`

### Implementation for User Story 3

- [X] T053 [US3] Implement deterministic delimiter detection in `src/Validator.Infrastructure/Csv/DelimiterDetector.cs`
- [X] T054 [US3] Implement header-name layout matching (case-insensitive, order-independent) in `src/Validator.Infrastructure/Csv/HeaderLayoutResolver.cs`
- [X] T055 [US3] Implement combined-timestamp column selection plus `--date-format`/`--time-format`/`--timestamp-format` overrides in `src/Validator.Infrastructure/Csv/CsvCandleSource.cs`
- [X] T056 [US3] Implement `--tz-offset` parsing and UTC normalization in `src/Validator.Infrastructure/Csv/SourceOffsetConverter.cs`
- [X] T057 [US3] Add `--header`, `--delimiter`, `--date-format`, `--time-format`, `--timestamp-format`, `--timestamp-column`, `--tz-offset` options with cross-option conflict validation to `src/Validator.Cli/Commands/ValidateCommand.cs`
- [X] T058 [P] [US3] Add fixtures `header-semicolon.csv` and `combined-timestamp.csv` (+ manifests) in `tests/Validator.Cli.Tests/Fixtures/`

**Checkpoint**: Non-MT4 layouts supported; US1–US3 independently testable

---

## Phase 6: User Story 4 - Alternate market calendars (equities, crypto, custom) (Priority: P4)

**Goal**: Select the market model via `--market equities|crypto|custom` and load a versioned JSON calendar via `--calendar <path>`, with DST-correct equity sessions, always-open crypto, and fail-fast custom-calendar validation.

**Independent Test**: Run `custom-session.csv --market custom --calendar custom-market.json --timeframe H1`; only `[open, close)` timestamps are expected and a boundary record is counted as closed-market. An invalid/omitted custom calendar fails before CSV parsing (AS-12).

### Tests for User Story 4 (write FIRST, ensure they FAIL) ⚠️

- [ ] T059 [P] [US4] Equities session tests on both sides of a NodaTime/TZDB DST change (`America/New_York` 09:30–16:00) in `tests/Validator.Infrastructure.Tests/Calendars/EquitiesCalendarTests.cs`
- [ ] T060 [P] [US4] Crypto always-open tests (FR-018 never fires) in `tests/Validator.Application.Tests/Calendars/CryptoCalendarTests.cs`
- [ ] T061 [P] [US4] Custom-calendar loader tests (schema conformance, unsupported version, overlapping/ambiguous session → fatal config error) in `tests/Validator.Infrastructure.Tests/Calendars/CustomCalendarLoaderTests.cs`
- [ ] T062 [P] [US4] CLI E2E custom-calendar test (valid run + invalid/omitted calendar fails pre-parse) in `tests/Validator.Cli.Tests/CalendarE2ETests.cs`

### Implementation for User Story 4

- [ ] T063 [US4] Implement NodaTime `ITimeZoneScheduleExpander` adapter (pinned TZDB) in `src/Validator.Infrastructure/Calendars/NodaTimeScheduleExpander.cs`
- [ ] T064 [US4] Implement equities, crypto, and custom `IMarketCalendar` resolution via `IMarketCalendarFactory` in `src/Validator.Infrastructure/Calendars/MarketCalendarFactory.cs`
- [ ] T065 [US4] Implement custom-calendar JSON loader with schema + semantic validation against `market-calendar.schema.json` in `src/Validator.Infrastructure/Calendars/CalendarJsonLoader.cs`
- [ ] T066 [US4] Add `--market` and `--calendar` options with profile/calendar compatibility validation to `src/Validator.Cli/Commands/ValidateCommand.cs`
- [ ] T067 [P] [US4] Add fixtures `custom-session.csv` and `custom-market.json` in `tests/Validator.Cli.Tests/Fixtures/`

**Checkpoint**: All four market profiles work; US1–US4 independently testable

---

## Phase 7: User Story 5 - Reusable library and bounded-memory guarantees (Priority: P5)

**Goal**: Prove the Domain+Application assemblies drive an identical validation run from a non-CLI front end (NFR-003) and that memory stays bounded on multi-million-row unsorted inputs with all temporary artifacts cleaned up (NFR-020).

**Independent Test**: A harness referencing only `Validator.Application` + `Validator.Domain` with in-memory ports reproduces the CLI counts with zero source changes; a large-fixture replay run matches the pre-sorted result, keeps peak memory within tolerance, and leaves no temp files after clean/finding/fatal/cancellation paths.

### Tests for User Story 5 (write FIRST, ensure they FAIL) ⚠️

- [ ] T068 [P] [US5] Alternate front-end harness test referencing only Application+Domain and calling `IValidateMarketDataUseCase` in `tests/Validator.Application.Tests/AlternateFrontEndProofTests.cs`
- [ ] T069 [P] [US5] Bounded-memory large-fixture replay test asserting sorted-equivalence, chunk-size invariant, and temp-artifact cleanup in `tests/Validator.Infrastructure.Tests/Sorting/BoundedMemoryTests.cs`

### Implementation for User Story 5

- [ ] T070 [US5] Implement large unsorted M1 fixture generator (writes outside the repo) in `tests/Validator.Infrastructure.Tests/Fixtures/LargeFixtureGenerator.cs`
- [ ] T071 [US5] Ensure deterministic temp sort/finding artifact cleanup across clean, finding, fatal, and cancellation terminal paths in `src/Validator.Infrastructure/Sorting/TempStorage.cs` and `src/Validator.Infrastructure/Findings/SpoolingFindingStore.cs`

**Checkpoint**: Library reusability and bounded-memory guarantees proven

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [ ] T072 [P] Verify and enforce 100% line+branch coverage for `Validator.Domain` and `Validator.Application` in CI per quickstart §3
- [ ] T073 [P] Complete `--help` text with every option and required example invocations in `src/Validator.Cli/Commands/ValidateCommand.cs` (contracts/cli.md §Required Help Examples)
- [ ] T074 Run the full `quickstart.md` scenario walkthrough end-to-end and reconcile any gaps
- [ ] T075 [P] Update `README.md` with usage, options table, and exit-code documentation
- [ ] T076 [P] Confirm cross-platform CI matrix (Windows/Linux/macOS) passes all suites in `.github/workflows/ci.yml`
- [ ] T077 Add determinism regression test asserting reordered-input count equality and canonical line ordering in `tests/Validator.Cli.Tests/DeterminismTests.cs`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3+)**: All depend on Foundational phase completion
  - US1 (P1) is the MVP and should be completed first
  - US2, US3, US4 extend the same use case/CLI and can proceed in parallel after Foundational (they touch different adapters), coordinating only on the shared `ValidateCommand.cs`
  - US5 (P5) validates cross-cutting guarantees; its tests depend on US1 being functional
- **Polish (Phase 8)**: Depends on all desired user stories being complete

### User Story Dependencies

- **US1 (P1)**: Depends only on Foundational - No dependencies on other stories
- **US2 (P2)**: Depends on Foundational; reuses the US1 use-case/report pipeline but is independently testable via the JSON/output paths
- **US3 (P3)**: Depends on Foundational; extends the CSV ingestion adapter and CLI options independently of US2/US4
- **US4 (P4)**: Depends on Foundational; adds calendar adapters independently of US2/US3
- **US5 (P5)**: Depends on Foundational and a functional US1 pipeline to exercise reuse and bounded memory

### Within Each User Story

- Tests MUST be written and FAIL before implementation (Test-First / NFR-010)
- Domain/value objects before services
- Services before adapters/endpoints
- Rules and adapters before the CLI wiring that composes them
- Story complete before moving to next priority

### Parallel Opportunities

- All Setup tasks marked [P] (T004–T006) can run in parallel
- All Foundational value-object tasks marked [P] (T008–T012) can run in parallel; T013 (ports) then T014–T016 [P]
- Once Foundational completes, US1 test tasks (T018–T028) can be authored in parallel
- Independent US1 rules (T031–T033) can be implemented in parallel; T034/T035 wait on the expected-sequence generator (T030)
- After Foundational, US2/US3/US4 can be staffed in parallel by different developers (different adapters), synchronizing on `ValidateCommand.cs`

---

## Parallel Example: User Story 1

```bash
# Author all US1 rule/writer/ingestion tests together (they live in different files):
Task: "Duplicate-record rule tests in tests/Validator.Application.Tests/Rules/DuplicateRecordRuleTests.cs"
Task: "Invalid-OHLC rule tests in tests/Validator.Application.Tests/Rules/InvalidOhlcRuleTests.cs"
Task: "Closed-market-record rule tests in tests/Validator.Application.Tests/Rules/ClosedMarketRecordRuleTests.cs"
Task: "Timeframe-detection tests in tests/Validator.Application.Tests/Timeframes/TimeframeDetectorTests.cs"
Task: "Text report writer contract test in tests/Validator.Infrastructure.Tests/Reporting/TextReportWriterTests.cs"

# Then implement the independent rules in parallel:
Task: "Implement DuplicateRecordRule in src/Validator.Application/Validation/Rules/DuplicateRecordRule.cs"
Task: "Implement InvalidOhlcRule in src/Validator.Application/Validation/Rules/InvalidOhlcRule.cs"
Task: "Implement ClosedMarketRecordRule in src/Validator.Application/Validation/Rules/ClosedMarketRecordRule.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL - blocks all stories)
3. Complete Phase 3: User Story 1 (default MT4 forex validation + text report + exit codes)
4. **STOP and VALIDATE**: Run the clean, defects, and fatal fixtures; confirm counts and exit codes
5. Demo/deploy the MVP

### Incremental Delivery

1. Setup + Foundational → foundation ready
2. Add US1 → validate → deploy (MVP: text report answering "can I trust this data?")
3. Add US2 → JSON + file output for CI/pipelines → validate → deploy
4. Add US3 → non-MT4 input formats → validate → deploy
5. Add US4 → alternate market calendars → validate → deploy
6. Add US5 → library reuse + bounded-memory proof → validate
7. Each story adds value without breaking previous stories

### Parallel Team Strategy

With multiple developers, after Foundational completes:

1. Developer A: US1 (MVP core) — highest priority, others build on its pipeline
2. Once US1's report pipeline is stable:
   - Developer B: US2 (reporting adapters)
   - Developer C: US3 (CSV ingestion options)
   - Developer D: US4 (calendar adapters)
3. Stories integrate at the shared `ValidateCommand.cs` and complete independently

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to a specific user story for traceability
- Test-First is mandatory (NFR-010): verify each test fails before implementing
- Domain and Application carry the 100% line/branch coverage gate (NFR-011/NFR-011a); Infrastructure and CLI are proven by integration/E2E tests
- No source mutation: fixtures must remain byte-for-byte unchanged after any run
- Commit after each task or logical group; stop at any checkpoint to validate a story independently
