# Tasks: Web Application Integration

**Input**: Design documents from `/specs/006-web-application-integration/`

**Prerequisites**: plan.md, spec.md, data-model.md, contracts/ (web-integration-contract.md, web-run-lifecycle.md, web-result-view-contract.md), research.md, quickstart.md

**Tests**: Included — Constitution Principle I (test-first) is non-negotiable and FR-027 requires failing tests to precede implementation for every new behavior.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

**Repositories**: This feature spans two repositories as one deliverable (spec Clarifications, 2026-09-02):

- **Validator repository** (this repo, `d:\financial-data-cleaner`): the transport-neutral integration boundary (`src/Validator.Application/Web/`), the storage ports and file adapters (`src/Validator.Infrastructure/Web/`), the CLI↔web parity suite (`tests/Validator.Parity.Tests/`), and the versioned local/private NuGet packages.
- **Certus repository** (`D:\Certus`): the Blazor Web App presentation pages (MudBlazor, .NET 10) that consume the packages in-process. Research R1 is therefore **RESOLVED** and presentation-layer tasks are schedulable.

Research R4/R5 interim defaults apply: content-addressed file stores under a configurable root, and retain-until-explicitly-deleted retention with explicit `Unavailable` outcomes. `Validator.Domain` and `Validator.Cli` MUST remain untouched (FR-022, FR-033).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Validator repository**: `src/`, `tests/` at repository root (relative paths below)
- **Certus repository**: absolute paths under `D:\Certus` (website pages, configuration, and website test suites)

---

## Phase 1: Setup

**Purpose**: Create the directory structure, the parity test project, and the web-specific fixtures.

- [ ] T001 Create directory structure for the web boundary: `src/Validator.Application/Web/`, `src/Validator.Infrastructure/Web/`, `tests/Validator.Application.Tests/Web/`, `tests/Validator.Infrastructure.Tests/Web/`
- [ ] T002 [P] Create the parity test project `tests/Validator.Parity.Tests/Validator.Parity.Tests.csproj` (xunit + FluentAssertions, project references to `Validator.Application`, `Validator.Infrastructure`, and `Validator.Cli` for driving both front ends) and register it in `FinancialDataCleaner.slnx`
- [ ] T003 [P] Create web-run test fixtures in `tests/Fixtures/web/`: an empty file, a header-only file, a file with an unsupported encoding (e.g. UTF-16), a structurally unparsable file, a file whose name and values contain markup/quotes/control characters, and a fixture producing a very large finding count (per quickstart scenarios 3, 4, 8)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The run envelope — entities, ports, outcome records, option validation, file adapters, and packaging — that ALL user stories depend on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Tests for Foundational Components

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation** (Constitution Principle I)

- [ ] T004 [P] Write unit tests for the `WebRunStatus` lifecycle in `tests/Validator.Application.Tests/Web/WebRunStatusTests.cs` — allowed transitions (Pending → Running|Failed; Running → CompletedClean|CompletedWithFindings|Failed; Failed → Pending via explicit retry only), forbidden transitions (Pending → CompletedClean, Failed → Completed*, Completed* → anything, Running → Pending), and that CompletedClean is reachable only with a clean reconciled report
- [ ] T005 [P] Write unit tests for `WebRunId` in `tests/Validator.Application.Tests/Web/WebRunIdTests.cs` — exactly 64 lower-case hex characters; identical bytes + equivalent resolved options produce an equal id; one changed material option produces a different id; wall-clock, sequence numbers, randomness, upload name, and progress never contribute (SC-004)
- [ ] T006 [P] Write unit tests for `WebRunRecord` in `tests/Validator.Application.Tests/Web/WebRunRecordTests.cs` — Diagnostic non-null exactly when Status == Failed; ResultReference non-null only for terminal success; BenchmarkName required for EstablishBenchmark/Compare; timestamps from `IApplicationClock` and audit-only; completed states immutable
- [ ] T007 [P] Write unit tests for `WebRunOptionsValidator` in `tests/Validator.Application.Tests/Web/WebRunOptionsValidatorTests.cs` — every rule in the pre-read table of `contracts/web-integration-contract.md` (score weights require scoring; scoring unavailable under frozen v1 JSON; benchmark/compare require scoring + v2 + instrument; tolerances require comparison; canonical timeframe codes; `CsvInputOptions.Validate()`; `ScoreWeightParser.Parse` covering all six metrics), each rejection carrying `INVALID_ARGUMENT` with the specific correction
- [ ] T008 [P] Write the boundary architecture test in `tests/Validator.Application.Tests/Web/WebBoundaryArchitectureTests.cs` — assert the `Validator.Application` assembly references none of the prohibited types in `contracts/web-integration-contract.md` (HTTP/server, session/identity, view/markup, filesystem-path inputs, `Console`, `Environment`, `DateTime.Now`, `CultureInfo.CurrentCulture`, `TimeZoneInfo.Local`) via reflection over exported types and assembly references
- [ ] T009 [P] Write unit tests for `FileWebRunStore` in `tests/Validator.Infrastructure.Tests/Web/FileWebRunStoreTests.cs` — FindAsync/TryCreateAsync (returns false when the deterministic id already exists), TransitionAsync rejects invalid transitions instead of coercing, record and result-artifact persistence under a configurable root, atomic writes, and always-valid observed status during concurrent transitions
- [ ] T010 [P] Write unit tests for `FileUploadedDatasetStore` in `tests/Validator.Infrastructure.Tests/Web/FileUploadedDatasetStoreTests.cs` — write-once content-addressed storage by SHA-256, OpenAsync replays byte-identical bytes (hash before/after), duplicate storage of the same content reuses the same reference, and safe base names with no path components
- [ ] T011 [P] Write unit tests for `InlineWebRunQueue` in `tests/Validator.Infrastructure.Tests/Web/InlineWebRunQueueTests.cs` — executes an accepted run synchronously through the injected run executor, terminal state is persisted, and a crash between durable Pending and enqueue leaves a recoverable Pending run rather than a lost one

### Implementation for Foundational Components

- [ ] T012 [P] Create `WebRunStatus` in `src/Validator.Application/Web/WebRunStatus.cs` — enum (Pending, Running, CompletedClean, CompletedWithFindings, Failed) plus the transition guard implementing the exact table in `contracts/web-run-lifecycle.md`; rejections surface as failures, never coerced states
- [ ] T013 [P] Create `WebRunId` in `src/Validator.Application/Web/WebRunId.cs` — 64-char lower-case hex value object with the normative derivation SHA-256(SourceIdentity.Sha256 ‖ 0x1F ‖ CanonicalOptionsString), including the culture-invariant, field-ordered CanonicalOptionsString serialization of every resolved option that materially affects a result (per `data-model.md`)
- [ ] T014 [P] Create `WebRunRequest` in `src/Validator.Application/Web/WebRunRequest.cs` — the request record (Operation, SubmittedFileName, Content stream, WebRunOptions, optional SubmittedBy), the `WebRunOperation` enum (Validate, EstablishBenchmark, Compare), and the `WebRunOptions` record exposing every material option (FR-003)
- [ ] T015 [P] Create `WebRunRecord` in `src/Validator.Application/Web/WebRunRecord.cs` — the audit aggregate per `data-model.md` (Id, Operation, Status, Source, ResolvedOptions, BenchmarkName?, ResultReference?, Diagnostic?, SubmittedAtUtc, TerminalAtUtc?, SubmittedBy?) with the record-level invariants enforced at construction and transition
- [ ] T016 [P] Create `WebResultView` in `src/Validator.Application/Web/WebResultView.cs` — the typed, presentation-free view record plus its section records (`WebValidationSection`, `WebScoringSection`, `WebBenchmarkSection`, `WebComparisonSection`) with the structural invariants from `contracts/web-result-view-contract.md` (Diagnostic ⇔ Failed with all sections null; AvailableExports empty unless terminal success; typed values only, no markup)
- [ ] T017 [P] Create `IValidationWebService` in `src/Validator.Application/Web/IValidationWebService.cs` — the façade interface (SubmitAsync, GetStatusAsync, GetResultAsync, ExportAsync, RetryAsync) plus the outcome records (WebRunSubmission.Accepted/Rejected, WebRunStatusResult.Known/Unavailable, WebResultRetrieval.Ready/NotReady/Unavailable, WebExportResult.Written/NotAvailable) exactly per `contracts/web-integration-contract.md`
- [ ] T018 [P] Create `IWebRunStore` in `src/Validator.Application/Abstractions/IWebRunStore.cs` — FindAsync, TryCreateAsync, TransitionAsync ports plus the `WebRunTransitionData` record carrying the guarded terminal payload (result reference or FatalDiagnostic)
- [ ] T019 [P] Create `IUploadedDatasetStore` in `src/Validator.Application/Abstractions/IUploadedDatasetStore.cs` — StoreAsync(safeFileName, content) → UploadedDataset and OpenAsync(dataset, options) → IPreparedCandleSource ports, plus the `UploadedDataset` record (Identity, ContentReference) with write-once, replay-byte-identical semantics
- [ ] T020 [P] Create `IWebRunQueue` in `src/Validator.Application/Abstractions/IWebRunQueue.cs` — EnqueueAsync(WebRunId) port, called only after the record is durably Pending
- [ ] T021 [P] Create `WebRunOptionsValidator` in `src/Validator.Application/Web/WebRunOptionsValidator.cs` — pre-read option validation reusing the CLI's established rules and codes (table in `contracts/web-integration-contract.md`); validation completes before any content byte is interpreted (FR-007)
- [ ] T022 [P] Implement `FileWebRunStore` in `src/Validator.Infrastructure/Web/FileWebRunStore.cs` — file-based IWebRunStore under a configurable root following the `FileBenchmarkStore` pattern: atomic writes, guarded transitions, run-record and result-artifact persistence, deterministic behavior under races
- [ ] T023 [P] Implement `FileUploadedDatasetStore` in `src/Validator.Infrastructure/Web/FileUploadedDatasetStore.cs` — content-addressed write-once byte store keyed by SHA-256; OpenAsync replays the exact stored bytes through the existing CsvCandleSource so validation reads what was hashed (SC-008)
- [ ] T024 [P] Implement `InlineWebRunQueue` in `src/Validator.Infrastructure/Web/InlineWebRunQueue.cs` — simplest safe default per research R3: executes an accepted run synchronously through an injected Application run executor and lets the executor persist the terminal state
- [ ] T025 [P] Add NuGet package metadata (PackageId, Version) to `src/Validator.Application/Validator.Application.csproj` and `src/Validator.Infrastructure/Validator.Infrastructure.csproj`, and create `scripts/pack-validator-packages.ps1` producing versioned local/private packages via `dotnet pack` into a local feed folder

**Checkpoint**: Foundation ready — the run envelope, ports, adapters, and packaging exist; user story implementation can begin.

---

## Phase 3: User Story 1 — Run Dataset Validation in the Web Application (Priority: P1) 🎯 MVP

**Goal**: A user can submit an OHLCV dataset with equivalent options through the web boundary and receive the same validation result as the CLI, with a durable, retrievable run lifecycle.

**Independent Test**: Upload a known clean fixture and a fixture with known findings, select equivalent options, run each independently, and compare the web result with the established CLI result (quickstart scenarios 1–5).

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T026 [P] [US1] Write façade tests in `tests/Validator.Application.Tests/Web/ValidationWebServiceTests.cs` — clean fixture → CompletedClean with all six category counts zero and exposed separately; findings fixture → CompletedWithFindings with every category separate and overlaps retained; fatal inputs (T003 fixtures) → Failed with the established FatalDiagnostic codes and no counts/scores/export; invalid options → Rejected(INVALID_ARGUMENT) before any byte is stored or queued; unknown id → Unavailable; non-terminal run → NotReady carrying the real status
- [ ] T027 [P] [US1] Write determinism and idempotency tests in `tests/Validator.Application.Tests/Web/WebRunDeterminismTests.cs` — identical bytes + identical options → same WebRunId with JoinedExistingRun=true, exactly one record, no duplicate work; one changed material option → different, separately retrievable id; exports substantively equivalent; fixed vs. moved clock proves no wall-clock influence (quickstart scenario 4)
- [ ] T028 [P] [US1] Write interruption and retrieval tests in `tests/Validator.Application.Tests/Web/WebRunInterruptionTests.cs` — status polling during Pending/Running; re-query never restarts or duplicates; aborted run ends Failed with a diagnostic, never CompletedClean; unknown/removed id → Unavailable with a reason; completed runs stay retrievable under retain-until-deleted (quickstart scenario 5, SC-007)
- [ ] T029 [P] [US1] Write CLI↔web parity tests in `tests/Validator.Parity.Tests/ValidationParityTests.cs` — run the clean fixture and the every-category fixture through the CLI front end (in-process ValidateCommand or the built binary) and through `IValidationWebService` with equivalent resolved options; assert equality of the full substantive comparison surface (report status, six category counts, scan coverage, six check statuses in canonical order, reconciliation, complete finding sequence with evidence, source lines, timestamps, observed values, and both relationship directions) per `contracts/web-result-view-contract.md`

### Implementation for User Story 1

- [ ] T030 [US1] Implement `ValidationWebService` in `src/Validator.Application/Web/ValidationWebService.cs` — SubmitAsync (pre-read option validation → store upload via IUploadedDatasetStore → derive WebRunId → IWebRunStore.TryCreateAsync → Accepted with JoinedExistingRun on duplicate → IWebRunQueue.EnqueueAsync), GetStatusAsync (Known/Unavailable, never triggers work), GetResultAsync (terminal → Ready view; else NotReady/Unavailable), RetryAsync (the only permitted Failed → Pending transition)
- [ ] T031 [US1] Implement `WebRunExecutor` in `src/Validator.Application/Web/WebRunExecutor.cs` — the Application run executor invoked by the queue: transition Pending → Running, open the stored upload via IUploadedDatasetStore.OpenAsync, drive the existing `IDetailedValidationUseCase`, persist the result artifact, and transition to CompletedClean/CompletedWithFindings (guarded on `DetailedSummary.IsClean`) or Failed with FatalDiagnostic — never exposing partial counts
- [ ] T032 [US1] Run and pass all US1 boundary and parity tests (`dotnet test tests/Validator.Application.Tests --filter "FullyQualifiedName~Web"` and `dotnet test tests/Validator.Parity.Tests`)

### Website Integration for User Story 1 (Certus repository)

- [ ] T033 [US1] Create the DI composition extension `AddValidatorWebIntegration` in `src/Validator.Infrastructure/Web/ValidatorWebIntegrationExtensions.cs` — registers IValidationWebService → ValidationWebService, IWebRunStore → FileWebRunStore, IUploadedDatasetStore → FileUploadedDatasetStore, IWebRunQueue → InlineWebRunQueue, IApplicationClock → SystemClock with a configurable storage root (add the `Microsoft.Extensions.DependencyInjection` reference if absent), then re-run `scripts/pack-validator-packages.ps1` to publish the versioned packages
- [ ] T034 [US1] Consume the packages in Certus: create `D:\Certus\nuget.config` with a local feed pointing at the validator package output, add `Validator.Application` and `Validator.Infrastructure` PackageReferences to `D:\Certus\src\Certus.Dashboard\Certus.Dashboard.csproj`, and add the validator storage-root configuration section to `D:\Certus\src\Certus.Dashboard\appsettings.json`
- [ ] T035 [US1] Register the validator integration in `D:\Certus\src\Certus.Dashboard\Program.cs` by calling `AddValidatorWebIntegration` with configuration, following the existing Certus composition conventions
- [ ] T036 [US1] Create the submission page `D:\Certus\src\Certus.Dashboard\Components\Pages\Validation.razor` — MudBlazor upload control, the full option form (timeframe, market profile/calendar, timestamp interpretation including format/column/offset, delimiter, header handling, report version, instrument, scoring toggle, benchmark name, tolerance overrides per FR-003), submit action with duplicate-submission guard, explicit pending state, and fatal-diagnostic display; add the navigation entry to `D:\Certus\src\Certus.Dashboard\Components\Layout\NavMenu.razor`
- [ ] T037 [US1] Create the run detail page `D:\Certus\src\Certus.Dashboard\Components\Pages\ValidationRunDetail.razor` — polls GetStatusAsync until terminal (explicit progress, never a timeout-as-failure), renders run status, source identity, resolved context, scan coverage, and the six category summaries as separate values, distinguishes clean vs. findings vs. failed, and offers the explicit retry action for failed runs
- [ ] T038 [P] [US1] Write component tests in `D:\Certus\tests\Certus.ComponentTests\ValidationFlowTests.cs` — submit → pending → result summary with six separate categories; fatal input display; option-validation errors identified per input with actionable feedback
- [ ] T039 [P] [US1] Write the end-to-end happy path in `D:\Certus\tests\Certus.E2ETests\ValidationFlowE2ETests.cs` — upload a fixture through the running website, wait for completion, and locate the six summary counts without the command line (SC-005 core path)

**Checkpoint**: User Story 1 is fully functional and independently testable — validation runs through the website with CLI-equivalent results and a durable run lifecycle. This is the MVP.

---

## Phase 4: User Story 2 — Inspect and Export a Detailed Report (Priority: P1)

**Goal**: A user can inspect every validation finding in a navigable web report and download the machine-readable export with the same substantive content.

**Independent Test**: Run validation against a fixture with findings in every established category, inspect the report in the website, download the machine-readable report, and verify all required locations, evidence, relationships, and counts are present (quickstart scenario 2).

### Tests for User Story 2

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T040 [P] [US2] Write export tests in `tests/Validator.Application.Tests/Web/ValidationWebServiceExportTests.cs` — ExportAsync on a terminal success returns Written for each existing ReportRepresentation using the existing writers; Failed, non-terminal, and unknown runs return NotAvailable; export streams UTF-8 without materializing the whole report and is never silently truncated on the large-finding fixture; AvailableExports matches the terminal-success rule
- [ ] T041 [P] [US2] Write result-view detail tests in `tests/Validator.Application.Tests/Web/WebResultViewDetailTests.cs` — the view exposes CheckExecution (all six, canonical order), ReportReconciliation, and findings streamed through ICompletedFindingCatalog in canonical order; source lines, timestamps, and observed values are distinct typed members, not prose; missing-candle ↔ time-gap relationships are present in both directions; a Failed view carries the diagnostic and nothing else (FR-011, FR-019, SC-002)
- [ ] T042 [P] [US2] Write detailed-report parity tests in `tests/Validator.Parity.Tests/DetailedReportParityTests.cs` — CLI v2 export vs. web view and web export over the every-category fixture: complete finding sequence, evidence records, source lines, timestamps, observed values, and both relationship directions are identical

### Implementation for User Story 2

- [ ] T043 [P] [US2] Implement `ExportAsync` in `src/Validator.Application/Web/ValidationWebService.cs` — delegate to the existing `ISuccessReportWriter`/`IFatalDiagnosticWriter` implementations by ReportRepresentation, stream to the destination without a new serializer, and populate AvailableExports for terminal successes only (FR-014)
- [ ] T044 [P] [US2] Complete the `WebValidationSection` population in `src/Validator.Application/Web/WebRunExecutor.cs` and the view building in `src/Validator.Application/Web/WebResultView.cs` — carry Checks, Reconciliation, Summary, the streamed findings catalog, typed evidence members, and both-direction relationships from the `DetailedValidationReport` without materializing all findings
- [ ] T045 [US2] Run and pass all US2 tests (`dotnet test tests/Validator.Application.Tests --filter "FullyQualifiedName~Web"`, `dotnet test tests/Validator.Parity.Tests`, and `dotnet test tests/Validator.Infrastructure.Tests`)

### Website Integration for User Story 2 (Certus repository)

- [ ] T046 [US2] Create the report page `D:\Certus\src\Certus.Dashboard\Components\Pages\ValidationReport.razor` — navigable findings list (virtualized MudBlazor table so very large finding counts stay navigable), finding details with distinct typed evidence, related-finding navigation in both directions, check/reconciliation/coverage sections, and a clear fatal-diagnostic-vs-complete-report distinction that never offers a failed run as a report
- [ ] T047 [US2] Add export download actions to `D:\Certus\src\Certus.Dashboard\Components\Pages\ValidationReport.razor` — one action per AvailableExports representation calling ExportAsync and streaming the artifact as a file download using the same substantive content as the displayed report
- [ ] T048 [US2] Write component tests in `D:\Certus\tests\Certus.ComponentTests\ReportRenderingTests.cs` — findings and evidence rendering, related-finding navigation, and export-action availability rules (terminal success only)

**Checkpoint**: User Stories 1 and 2 both work independently — validation plus full detailed-report inspection and export through the website.

---

## Phase 5: User Story 3 — Review Dataset Scores (Priority: P1)

**Goal**: A user sees the six independent quality scores and the average in the website, matching the established scoring workflow exactly.

**Independent Test**: Score a clean fixture, a fixture with known defects, and a fixture where a metric is not applicable; verify per-metric states, counts, populations, weights, average coverage, and average value match the established workflow (quickstart scenario 6).

### Tests for User Story 3

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T049 [P] [US3] Write scoring tests in `tests/Validator.Application.Tests/Web/ValidationWebServiceScoringTests.cs` — Score=true produces a WebScoringSection with all six metrics (state, count, population, population kind, resolved weight, normalized share), the average with covered-metric count and excluded-metric reasons; not-applicable/not-scored/not-available are explicit states, never 0 or 100; custom weights covering all six metrics are accepted; invalid weight configurations are rejected before dataset processing; Score=false produces no scoring section and leaves validation counts, findings, order, and status unchanged (FR-005, FR-015, FR-018)
- [ ] T050 [P] [US3] Write scoring parity tests in `tests/Validator.Parity.Tests/ScoringParityTests.cs` — clean, defect, and not-applicable fixtures plus custom weights through both front ends: every metric state/count/population/kind/weight/share, the average, its coverage, and exclusions are identical

### Implementation for User Story 3

- [ ] T051 [US3] Implement scoring population in `src/Validator.Application/Web/WebRunExecutor.cs` and the `WebScoringSection` projection in `src/Validator.Application/Web/WebResultView.cs` — set ScoreRequest on ValidationOptions, compute via the existing scoring pipeline, project DatasetScoreReport into the section without recomputing anything, and prove scoring never alters the validation result (FR-005)
- [ ] T052 [US3] Run and pass all US3 tests (`dotnet test tests/Validator.Application.Tests --filter "FullyQualifiedName~Web"` and `dotnet test tests/Validator.Parity.Tests`)

### Website Integration for User Story 3 (Certus repository)

- [ ] T053 [US3] Create the score display component `D:\Certus\src\Certus.Dashboard\Components\Shared\ScoreSummary.razor` — six metrics with state/count/population/resolved weight/normalized share, the average with covered and excluded metrics and reasons, explicit not-applicable states, and the documented average calculation; integrate it into the report page and add the score-weights input to the option form in `D:\Certus\src\Certus.Dashboard\Components\Pages\Validation.razor`
- [ ] T054 [US3] Write component tests in `D:\Certus\tests\Certus.ComponentTests\ScoreDisplayTests.cs` — six metrics render with their states and values, NA states never render as numbers, and weights input validation matches the pre-read rules

**Checkpoint**: User Stories 1–3 work independently — validation, detailed reporting, and scoring all have web parity.

---

## Phase 6: User Story 4 — Manage and Compare Benchmark Datasets (Priority: P1)

**Goal**: A user can establish a validated dataset as an immutable named benchmark and compare candidates against it in the website, with deterministic conflict handling.

**Independent Test**: Establish a known dataset as an AUDUSD benchmark; compare an identical candidate, a tolerated opening-price variation, and a material difference with missing/extra candles; verify the web report matches the established comparison behavior (quickstart scenario 7).

### Tests for User Story 4

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T055 [P] [US4] Write benchmark establishment tests in `tests/Validator.Application.Tests/Web/ValidationWebServiceBenchmarkTests.cs` — establish records the immutable identity, source content, context, validation results, six scores, and dataset score; a duplicate name is refused explicitly, never silently replaced; two concurrent establishments on one name yield exactly one success and one deterministic conflict with no partial benchmark directory
- [ ] T056 [P] [US4] Write comparison tests in `tests/Validator.Application.Tests/Web/ValidationWebServiceComparisonTests.cs` — identical candidate → matched with no material discrepancies; tolerated opening-price variation → not material with auditable aggregate evidence; material difference plus missing/extra candles → separate matched/missing/extra reporting with discrepancy evidence (timestamp, field, both values, difference, resolved tolerance); incompatible timeframe → incompatible; no overlap → unavailable, never a perfect score; candidate quality score, benchmark-agreement score, and the benchmark's recorded scores are three separate members; benchmark source hashes unchanged before and after (FR-016, FR-017, FR-018, SC-008)
- [ ] T057 [P] [US4] Write store concurrency tests in `tests/Validator.Infrastructure.Tests/Benchmark/FileBenchmarkStoreConcurrencyTests.cs` — atomic create-if-absent under racing SaveAsync calls on one name, no partial directory left behind on conflict, deterministic conflict failure
- [ ] T058 [P] [US4] Write benchmark/comparison parity tests in `tests/Validator.Parity.Tests/BenchmarkComparisonParityTests.cs` — establish and compare the AUDUSD fixtures through both front ends; every comparison figure (matched/missing/extra counts and record sets, material discrepancies with resolved tolerances, tolerated aggregates, agreement score, coverage/applicability) matches the CLI output

### Implementation for User Story 4

- [ ] T059 [P] [US4] Amend the `IBenchmarkStore.SaveAsync` contract in `src/Validator.Application/Benchmark/IBenchmarkStore.cs` (atomic create-if-absent; deterministic conflict; no silent replacement — signature unchanged) and harden `FileBenchmarkStore` in `src/Validator.Infrastructure/Benchmark/FileBenchmarkStore.cs` to implement it (atomic directory creation, no partial state on conflict)
- [ ] T060 [US4] Implement the EstablishBenchmark and Compare operations in `src/Validator.Application/Web/ValidationWebService.cs` and `src/Validator.Application/Web/WebRunExecutor.cs` — delegate to the existing `EstablishBenchmarkUseCase` and `CompareDatasetsUseCase` (feeding the retained upload content), require scoring + v2 + instrument per the pre-read rules, and populate `WebBenchmarkSection` and `WebComparisonSection` per `contracts/web-result-view-contract.md`
- [ ] T061 [US4] Run and pass all US4 tests (`dotnet test tests/Validator.Application.Tests --filter "FullyQualifiedName~Web"`, `dotnet test tests/Validator.Parity.Tests`, and `dotnet test tests/Validator.Infrastructure.Tests`)

### Website Integration for User Story 4 (Certus repository)

- [ ] T062 [P] [US4] Create the benchmark management page `D:\Certus\src\Certus.Dashboard\Components\Pages\Benchmarks.razor` — establish a benchmark from a completed validated run (name + instrument), list benchmarks, inspect a benchmark's exact source content identity and validation context and recorded scores, and surface duplicate-name conflicts explicitly with a distinct replacement-oriented action required
- [ ] T063 [P] [US4] Create the comparison page `D:\Certus\src\Certus.Dashboard\Components\Pages\Compare.razor` — candidate upload, benchmark selection, and tolerance overrides; comparison result view showing matched/missing/extra separately, material discrepancies with full evidence, tolerated-difference aggregates, the agreement score separate from the candidate's quality score, and explicit unavailable/incompatible states
- [ ] T064 [US4] Write component tests in `D:\Certus\tests\Certus.ComponentTests\BenchmarkFlowTests.cs` — establish → list → inspect round-trip, duplicate-name conflict messaging, and comparison rendering for matched/tolerated/material/unavailable cases

**Checkpoint**: All four P1 workflows have web parity — validation, detailed reporting, scoring, and benchmark management/comparison.

---

## Phase 7: User Story 5 — Rely on a Consistent, Accessible Website Experience (Priority: P2)

**Goal**: The migrated workflows look and behave like the rest of the Certus website and remain usable with keyboard-only interaction, assistive technologies, and supported responsive layouts.

**Independent Test**: Exercise each primary workflow using the website's normal navigation, keyboard-only interaction, supported responsive layouts, and the host's established loading, empty, error, and success patterns (quickstart host-dependent scenarios).

- [ ] T065 [US5] Perform the host-conventions consistency pass across the new pages (`D:\Certus\src\Certus.Dashboard\Components\Pages\Validation.razor`, `ValidationRunDetail.razor`, `ValidationReport.razor`, `Benchmarks.razor`, `Compare.razor`, and `D:\Certus\src\Certus.Dashboard\Components\Shared\ScoreSummary.razor`) — terminology, controls, states, and layout consistent with the surrounding Certus/MudBlazor conventions unless a constitution requirement takes precedence (FR-028)
- [ ] T066 [US5] Implement keyboard accessibility in the new pages (`D:\Certus\src\Certus.Dashboard\Components\Pages\Validation.razor`, `ValidationRunDetail.razor`, `ValidationReport.razor`, `Benchmarks.razor`, `Compare.razor`) — logical tab order, focus management, keyboard-operable controls and finding navigation, aria-live announcements for run-status changes and errors, and understandable status/summary/error/finding information for supported assistive technologies (FR-031, SC-009)
- [ ] T067 [US5] Implement responsive layouts for the new pages (`D:\Certus\src\Certus.Dashboard\Components\Pages\Validation.razor`, `ValidationRunDetail.razor`, `ValidationReport.razor`, `Benchmarks.razor`, `Compare.razor`) — narrow and wide supported displays keep summaries and finding evidence readable, with no material evidence hidden solely because of layout (US5 scenario 5)
- [ ] T068 [US5] Implement duplicate-submission prevention and in-progress feedback in `D:\Certus\src\Certus.Dashboard\Components\Pages\Validation.razor` and `Compare.razor` — disable submission while a run is pending/running, communicate that an identical resubmission joined the existing run, and never lose run context on refresh or navigation (FR-010, US5 scenario 2)
- [ ] T069 [US5] Implement upload limits and recovery guidance in `D:\Certus\src\Certus.Dashboard\Components\Pages\Validation.razor` and `Compare.razor` with limits configured in `D:\Certus\src\Certus.Dashboard\appsettings.json` — enforce the host's configured upload/processing limits before accepting work and report them without unsafe server details; preserve safe user-entered context on rejection and provide actionable recovery guidance for invalid options, unavailable results, and interrupted sessions (FR-029, FR-032, US5 scenario 3)
- [ ] T070 [US5] Write accessibility and responsive E2E tests in `D:\Certus\tests\Certus.E2ETests\AccessibilityFlowTests.cs` — keyboard-only completion of the primary validation flow reaching status, summary, error, and finding information without a pointer device, plus narrow/wide layout checks, using the host's established harness
- [ ] T071 [US5] Validate the representative-user timing targets against the running Certus website (`D:\Certus\src\Certus.Dashboard`) — SC-005 (upload, start, locate the six summary counts, open the detailed report within five minutes) and SC-006 (identify material inconsistencies, tolerated differences, coverage, and weakest metric within two minutes)

**Checkpoint**: The website experience is consistent, accessible, and resilient for all migrated workflows.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, non-regression, coverage, and final validation across both repositories.

- [ ] T072 [P] Update `README.md` in the validator repository with the "Web Application Integration" section required by FR-034 and Principle VIII — the supported web workflow, the parity boundary, report/export access, the storage-root configuration, the retain-until-explicitly-deleted retention policy, NuGet package build/consumption instructions, and the location of the authoritative web guidance in the Certus repository
- [ ] T073 [P] Update `D:\Certus\README.md` with the data-validation feature — the new pages, the validator package feed and storage-root configuration, and the run/build instructions (FR-034)
- [ ] T074 Run the full validator test suite (`dotnet test FinancialDataCleaner.slnx --configuration Release`) and verify every existing CLI suite passes unchanged (SC-010, FR-033)
- [ ] T075 [P] Verify no source change attributable to this feature in `src/Validator.Domain/` or `src/Validator.Cli/` (FR-022, FR-033; quickstart scenario 8 step 2)
- [ ] T076 Run the coverage gate (`tools/coverage-run.ps1`) and restore 100% line and branch coverage over reachable Domain + Application code including the new `src/Validator.Application/Web/` code, documenting any exclusion per `docs/coverage-exclusion-policy.md` (Constitution II)
- [ ] T077 Execute quickstart scenarios 1–8 from `specs/006-web-application-integration/quickstart.md` and verify every expected outcome
- [ ] T078 Execute the host-dependent scenarios from the host-dependent table in `specs/006-web-application-integration/quickstart.md` against the real Certus website at `D:\Certus\src\Certus.Dashboard` (keyboard-only, conventions, responsive, duplicate-submission, upload limits, recovery, timing, retention)
- [ ] T079 Build both solutions in Release (`dotnet build FinancialDataCleaner.slnx` and `dotnet build D:\Certus\Certus.slnx`) with zero warnings under `TreatWarningsAsErrors`, and produce the final versioned NuGet packages via `scripts/pack-validator-packages.ps1`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Foundational — no dependencies on other stories
- **US2 (Phase 4)**: Depends on US1 (needs completed runs and the result view)
- **US3 (Phase 5)**: Depends on US1 (scoring rides the Validate operation)
- **US4 (Phase 6)**: Depends on US1 + US3 (establishment requires a scored validation; comparison views reuse score display)
- **US5 (Phase 7)**: Depends on US1–US4 (all pages exist before the experience pass)
- **Polish (Phase 8)**: Depends on all user stories being complete

### User Story Dependencies

- **US1 (P1)**: Can start after Foundational — the MVP
- **US2 (P1)**: After US1; runs in parallel with US3
- **US3 (P1)**: After US1; runs in parallel with US2
- **US4 (P1)**: After US1 + US3
- **US5 (P2)**: After US1–US4

### Within Each User Story

- Boundary tests MUST be written and FAIL before boundary implementation (Constitution Principle I)
- Entities/ports before executors/services; executors before façade operations
- Boundary implementation before website integration (the website consumes the packaged boundary)
- Website pages before their component/E2E tests (presentation wiring carries integration coverage per Constitution II's adapter exemption)
- Story complete before moving to the next priority

### Parallel Opportunities

- T002–T003: parity project and fixtures (different files)
- T004–T011: all foundational test files in parallel
- T012–T025: entity, port, adapter, and packaging tasks in parallel (different files)
- T026–T029: all US1 boundary/parity test files in parallel
- T040–T042, T049–T050, T055–T058: each story's test files in parallel
- T043–T044, T059 + T060, T062–T063: same-story implementation tasks touching different files
- T072–T073: the two repository READMEs in parallel
- Once US1 completes, US2 and US3 can proceed in parallel (different story surfaces)
- Validator-repo and Certus-repo work can proceed in parallel once the packages from T033 exist

---

## Parallel Example: User Story 1

```text
# Launch all US1 test files together:
Task: "ValidationWebServiceTests.cs in tests/Validator.Application.Tests/Web/"
Task: "WebRunDeterminismTests.cs in tests/Validator.Application.Tests/Web/"
Task: "WebRunInterruptionTests.cs in tests/Validator.Application.Tests/Web/"
Task: "ValidationParityTests.cs in tests/Validator.Parity.Tests/"

# Then boundary implementation (sequential — façade and executor interlock):
Task: "ValidationWebService.cs in src/Validator.Application/Web/"
Task: "WebRunExecutor.cs in src/Validator.Application/Web/"

# Then website integration (sequential — package, wiring, pages):
Task: "ValidatorWebIntegrationExtensions.cs in src/Validator.Infrastructure/Web/"
Task: "nuget.config + PackageReference + appsettings in D:\Certus"
Task: "Program.cs registration in D:\Certus\src\Certus.Dashboard"
Task: "Validation.razor + ValidationRunDetail.razor in D:\Certus\src\Certus.Dashboard\Components\Pages"
```

## Parallel Example: User Stories 2 + 3 (after US1)

```text
# Developer A (US2) and Developer B (US3) work simultaneously:
A: "Export + view-detail tests → ExportAsync + WebValidationSection → report page + export UI"
B: "Scoring tests → scoring population → ScoreSummary.razor + weights form"

# Both converge on their component test suites, then US4 begins.
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories)
3. Complete Phase 3: User Story 1 — validation through the website
4. **STOP and VALIDATE**: Run quickstart scenarios 1–5; demo the upload → result flow
5. Deploy/demo if ready — the primary workflow is migrated

### Incremental Delivery

1. Setup + Foundational → run envelope, ports, adapters, packaging ready
2. Add US1 → Validation through the website → Validate (MVP!)
3. Add US2 → Detailed report inspection + export → Validate
4. Add US3 → Six scores + average in the website → Validate
5. Add US4 → Benchmark establishment + comparison → Validate (full parity)
6. Add US5 → Consistent, accessible experience → Validate
7. Polish → READMEs, coverage gate, quickstart + host scenarios → Ship

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: US1 boundary + parity, then US2
   - Developer B: US1 website integration (after packages exist), then US3
3. After US1 + US3: US4 (boundary concurrency + website)
4. US5 as a dedicated UX/accessibility pass
5. Both: Polish phase

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable at its checkpoint
- Verify tests fail before implementing (Constitution Principle I)
- Commit after each task or logical group; stop at any checkpoint to validate independently
- After each validator-repo story checkpoint, re-run `scripts/pack-validator-packages.ps1` and bump the consumed package version in Certus so the website tracks the newest boundary surface
- All numeric values use `decimal`, all timestamps UTC-normalized, all parsing/formatting culture-invariant (constitution Technology Standards, FR-025)
- The 100% Domain + Application coverage gate includes the new `src/Validator.Application/Web/` code; Infrastructure adapters carry integration coverage instead (Constitution II)
- `src/Validator.Domain/` and `src/Validator.Cli/` must remain source-unchanged (FR-022, FR-033) — any apparent need to change them is a design signal, not an implementation option
- Research R4 (production storage), R5 (retention enforcement), and R6 (identity/tenancy) remain host-policy defaults (content-addressed file stores, retain-until-deleted, trusted internal deployment); do not invent them
- Total tasks: 79 (3 Setup + 22 Foundational + 14 US1 + 9 US2 + 6 US3 + 10 US4 + 7 US5 + 8 Polish)