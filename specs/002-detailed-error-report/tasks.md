# Tasks: Detailed Dataset Error Report

**Input**: Design documents from `specs/002-detailed-error-report/` (plan.md, spec.md, data-model.md, contracts/, research.md, quickstart.md)

Phase 1 — Setup

- [ ] T001 Ensure solution builds and restore packages in CI and locally (validate dotnet restore/build/test). Path: FinancialDataCleaner.slnx
- [ ] T002 Add CI script/check to verify .NET 10 SDK and run `dotnet test` for existing projects. Path: .github/workflows/validate-dotnet.yml

Phase 2 — Foundational (blocking prerequisites)

- [ ] T003 [P] Create failing unit test: `DetailedFinding` model invariants (reference uniqueness, count contribution positive) in tests/Validator.Domain.Tests/DetailedFindingTests.cs
- [ ] T004 Implement `DetailedFinding`, `FindingReference`, and `FindingLocation` domain types to satisfy T003 in src/Validator.Domain/Findings/DetailedFinding.cs
- [ ] T005 [P] Create failing unit tests for evidence discriminants and serialization contracts in tests/Validator.Domain.Tests/EvidenceModelTests.cs
- [ ] T006 Implement evidence types: MissingCandleEvidence, TimeGapEvidence, DuplicateRecordEvidence, InvalidOhlcEvidence, ClosedMarketRecordEvidence, MalformedRowEvidence in src/Validator.Domain/Findings/Evidence/*.cs
- [ ] T007 Create failing unit tests for `CategoryReconciliation` invariants (summary==contributionSum) in tests/Validator.Application.Tests/ReconciliationTests.cs
- [ ] T008 Implement `ReportReconciliation` and in-memory constant-size counters in src/Validator.Application/Reporting/ReportReconciliation.cs
- [ ] T009 [P] Define Application port interfaces: ISourceIdentityProvider, ISpoolWriter, ISpoolReader, IReportWriter in src/Validator.Application/Abstractions/*.cs
- [ ] T010 Implement minimal Infrastructure stubs for spools and hashing to satisfy compile in src/Validator.Infrastructure/Findings/ and src/Validator.Infrastructure/Csv/ (temporary files and SHA-256). Path: src/Validator.Infrastructure/Findings/Spool*.cs

Phase 3 — User Story 1 (P1) — Review Every Detected Problem

Goal: One complete detailed report listing every finding produced by each completed validation check; no silent truncation.

Independent test criteria: End-to-end process produces a v2 JSON whose `findingSetComplete==true`, `summaryCount == contributionSum` for every category, and contains every injected finding from the fixture.

- [ ] T011 [US1] Create an end-to-end integration test that runs the CLI against a cross-category fixture and asserts v2 JSON invariants in tests/Validator.Cli.Tests/DetailedReportEndToEndTests.cs
- [ ] T012 Implement the FindingCatalog that appends normalized finding headers, evidence references, and bidirectional relationships into replayable spools in src/Validator.Application/Reporting/FindingCatalog.cs
- [ ] T013 [P] Create unit tests for FindingCatalog append/read/replay semantics in tests/Validator.Application.Tests/FindingCatalogTests.cs
- [ ] T014 Implement bounded external-merge spool-backed readers/writers used by FindingCatalog in src/Validator.Infrastructure/Findings/ExternalMergeSpool.cs
- [ ] T015 Implement the reconciliation check that validates summary counts against contribution sums and scan coverage in src/Validator.Application/Reporting/ReconciliationValidator.cs
- [ ] T016 Implement atomic staged rendering: render to a temporary staged artifact, validate, then commit atomically to destination or stdout in src/Validator.Infrastructure/Reporting/StageAndCommitWriter.cs
- [ ] T017 [US1] Add CLI wiring: `--format json --report-version 2` produces v2 JSON via the new writer; update Validator.Cli/Commands/ReportCommand.cs
- [ ] T018 [US1] Add integration test to validate stdout/destination atomic behavior and that fatal runs leave destination unchanged in tests/Validator.Cli.Tests/ReportCommitTests.cs

Phase 4 — User Story 2 (P2) — Locate and Understand Each Problem

Goal: Each finding shows where it occurred, rule failed, evidence, and suggested action.

Independent test criteria: For each category-specific fixture, detailed evidence fields match expected manifest.

- [ ] T019 [US2] Add unit tests for each evidence shape asserting presence of required fields (MissingCandle, TimeGap, DuplicateRow details, InvalidOhlc codes, ClosedMarket details, MalformedField entries) in tests/Validator.Domain.Tests/EvidenceShapeTests.cs
- [ ] T020 Implement evidence rendering join logic that joins finding headers to streamed evidence by Reference in src/Validator.Application/Reporting/EvidenceJoiner.cs
- [ ] T021 [P] Implement duplicate-group evidence streaming and differing-fields calculation in src/Validator.Application/Validation/DuplicateGroupProcessor.cs and tests in tests/Validator.Application.Tests/DuplicateGroupTests.cs
- [ ] T022 Implement missing-candle and time-gap generation with bidirectional relationship edges in src/Validator.Application/Validation/MissingCandleProcessor.cs and src/Validator.Application/Validation/TimeGapProcessor.cs

Phase 5 — User Story 3 (P2) — Diagnose an Incomplete Validation

Goal: Fatal diagnostic explains where processing stopped and which checks did not run; v2 fatal on stderr for v2 JSON runs.

Independent test criteria: Running v2 against unreadable/invalid fixtures produces a single fatal v2 JSON on stderr, empty stdout, exit code 2, and destination unchanged.

- [ ] T023 [US3] Create failing tests for FatalDiagnostic aggregate and stage/class/code invariants in tests/Validator.Application.Tests/FatalDiagnosticTests.cs
- [ ] T024 Implement `FatalDiagnostic` model and routing so fatal outcomes cannot produce a successful report in src/Validator.Application/Reporting/FatalDiagnostic.cs
- [ ] T025 [US3] Add CLI process-level tests asserting stderr contains exactly one v2 fatal document and stdout/destination remain empty/unchanged in tests/Validator.Cli.Tests/FatalV2RoutingTests.cs

Phase 6 — User Story 4 (P3) — Consume and Compare Reports Reliably

Goal: Deterministic, self-describing, and repeatable reports.

Independent test criteria: Re-running the same input/config produces byte-identical substantive fields and finding order.

- [ ] T026 [US4] Add deterministic-ordering unit tests for ID generation, tie-breakers, and overall finding ordering in tests/Validator.Application.Tests/DeterminismTests.cs
- [ ] T027 Implement stable FindingReference generation logic and deterministic tie-breaker ordering in src/Validator.Application/Reporting/FindingReferenceFactory.cs
- [ ] T028 [US4] Add an automated repeatability test that hashes two separate runs' v2 outputs and asserts identical hashes in tests/Validator.Cli.Tests/RepeatabilityTests.cs

Phase 7 — Polish & Cross-Cutting Concerns

- [ ] T029 Update quickstart.md with exact commands used by integration tests and example expected outputs. Path: specs/002-detailed-error-report/quickstart.md
- [ ] T030 Add schema validation tests that validate produced v2 JSON and fatal v2 JSON against contracts/detailed-report-v2.schema.json and contracts/fatal-diagnostic-v2.schema.json in tests/Validator.Cli.Tests/SchemaValidationTests.cs
- [ ] T031 Ensure Domain and Application reach 100% line and branch coverage: add any missing tests and coverage thresholds in CI configuration. Path: .github/workflows/coverage.yml
- [ ] T032 [P] Add documentation comments to public Domain/Application types created above. Paths: src/Validator.Domain/**, src/Validator.Application/**
- [ ] T033 Final review: run full quickstart (restore, build, test, run representative fixtures) and record results in specs/002-detailed-error-report/research.md

Dependencies

- Phase 1 tasks (T001-T002) must complete before Phase 2.
- Foundational tasks T003-T010 must complete before US1-US4 implementation tasks that depend on domain and application types (T011-T028).
- US1 (T011-T018) is the MVP path and should be prioritized; US2 and US3 depend on having the FindingCatalog and spool writers (T012-T016).

Parallel opportunities

- Tasks marked [P] can be worked in parallel (model tests and their implementations, evidence type implementations, documentation updates, and deterministic tests that do not change core catalogs).

MVP suggestion

- Focus on User Story 1 (T011-T018) as MVP. Deliver a complete, reconciled v2 report for the cross-category fixture with bounded memory and atomic commit.

Format validation

- All tasks follow the required checklist format with Task IDs, story labels for user-story phases, and explicit file paths.

Generated file: D:\financial-data-cleaner\specs\002-detailed-error-report\tasks.md
Total tasks: 33
Tasks per story/phase:
- Setup/Foundational/Polish: 12
- US1 (P1): 8
- US2 (P2): 4
- US3 (P2): 3
- US4 (P3): 3
Parallel opportunities identified: 7 tasks marked [P]
Suggested MVP scope: User Story 1 (T011-T018)

Next steps: begin work on T011 (create the end-to-end failing integration test) and then implement T012 (FindingCatalog) to drive test-first implementation.
