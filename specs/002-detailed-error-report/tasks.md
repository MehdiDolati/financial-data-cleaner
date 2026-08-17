# Tasks: Detailed Dataset Error Report

**Input**: Design documents from `specs/002-detailed-error-report/` (plan.md, spec.md, data-model.md, contracts/, research.md, quickstart.md)

Phase 1 - Setup

- [X] T001 Ensure solution builds and restore packages in CI and locally (validate dotnet restore/build/test). Path: FinancialDataCleaner.slnx
- [X] T002 Add CI script/check to verify .NET 10 SDK and run `dotnet test` for existing projects. Path: .github/workflows/validate-dotnet.yml

Phase 2 - Foundational (blocking prerequisites)

- [X] T003 [P] Create failing unit test: `DetailedFinding` model invariants (reference uniqueness, count contribution positive) in tests/Validator.Domain.Tests/DetailedFindingTests.cs
- [X] T004 Implement `DetailedFinding`, `FindingReference`, and `FindingLocation` domain types to satisfy T003 in src/Validator.Domain/Findings/DetailedFinding.cs
- [X] T005 [P] Create failing unit tests for evidence discriminants and serialization contracts in tests/Validator.Domain.Tests/EvidenceModelTests.cs
- [X] T006 Implement evidence types: MissingCandleEvidence, TimeGapEvidence, DuplicateRecordEvidence, InvalidOhlcEvidence, ClosedMarketRecordEvidence, MalformedRowEvidence in src/Validator.Domain/Findings/Evidence/*.cs
- [X] T007 Create failing unit tests for `CategoryReconciliation` invariants (summary==contributionSum) in tests/Validator.Application.Tests/ReconciliationTests.cs
- [X] T008 Implement `ReportReconciliation` and in-memory constant-size counters to satisfy T007 in src/Validator.Application/Reporting/ReportReconciliation.cs
- [X] T009 [P] Define Application port interfaces: ISourceIdentityProvider, ISpoolWriter, ISpoolReader, IReportWriter in src/Validator.Application/Abstractions/*.cs
- [X] T034 [P] Create failing integration tests for temporary spool lifecycle, SHA-256 source identity, cleanup on success/fatal/cancellation, and source/output alias rejection in tests/Validator.Infrastructure.Tests/SpoolAndSourceIdentityTests.cs
- [X] T010 Implement minimal Infrastructure spools and hashing to satisfy T034 in src/Validator.Infrastructure/Findings/Spool*.cs and src/Validator.Infrastructure/Csv/SourceIdentityProvider.cs
- [X] T035 [P] Create failing unit tests for `DetailedValidationReport`, `ReportOutcome`, `SourceIdentity`, `ValidationContextSnapshot`, `ScanCoverage`, and `CheckExecution` invariants in tests/Validator.Application.Tests/DetailedReportModelTests.cs
- [X] T036 Implement the report outcome, source/context, coverage, and check-execution models to satisfy T035 in src/Validator.Application/Reporting/ and src/Validator.Application/Ingestion/
- [ ] T037 Create failing application tests proving all six established checks can produce typed detailed findings, preserve source traceability, and mark completed/not-completed checks correctly in tests/Validator.Application.Tests/DetailedFindingProductionTests.cs
- [ ] T038 Implement detailed-finding production and outcome aggregation for missing candles, duplicate records, invalid OHLC, closed-market records, time gaps, and malformed rows in src/Validator.Application/Validation/DetailedValidationOrchestrator.cs

Phase 3 - User Story 1 (P1) - Review Every Detected Problem

Goal: One complete detailed report listing every finding produced by each completed validation check; no silent truncation.

Independent test criteria: End-to-end process produces a v2 JSON whose `findingSetComplete==true`, `summaryCount == contributionSum` for every category, and contains every injected finding from the fixture.

- [ ] T011 [US1] Create an end-to-end integration test that runs the CLI against a cross-category fixture and asserts v2 JSON invariants in tests/Validator.Cli.Tests/DetailedReportEndToEndTests.cs
- [ ] T013 [P] Create failing unit tests for FindingCatalog append/read/replay semantics, deterministic reference uniqueness, and relationship validation in tests/Validator.Application.Tests/FindingCatalogTests.cs
- [ ] T012 Implement the FindingCatalog append/read/replay semantics to satisfy T013 in src/Validator.Application/Reporting/FindingCatalog.cs
- [ ] T039 [P] Create failing integration tests for bounded external-merge spool ordering, replay, large duplicate groups, and temporary-artifact cleanup in tests/Validator.Infrastructure.Tests/ExternalMergeSpoolTests.cs
- [ ] T014 Implement bounded external-merge spool-backed readers/writers to satisfy T039 in src/Validator.Infrastructure/Findings/ExternalMergeSpool.cs
- [ ] T040 Create failing unit tests for reconciliation validation of category contributions, physical row totals, check statuses, and finding-set completeness in tests/Validator.Application.Tests/ReconciliationValidatorTests.cs
- [ ] T015 Implement the reconciliation check to satisfy T040 in src/Validator.Application/Reporting/ReconciliationValidator.cs
- [ ] T018 [US1] Create failing integration tests for staged rendering, stdout publication, destination atomicity, input/output alias rejection, and unchanged destinations after fatal/render failures in tests/Validator.Cli.Tests/ReportCommitTests.cs
- [ ] T016 Implement atomic staged rendering and commit behavior to satisfy T018 in src/Validator.Infrastructure/Reporting/StageAndCommitWriter.cs
- [ ] T017 [US1] Add CLI wiring so `--format json --report-version 2` produces v2 JSON through the validated report writer; update Validator.Cli/Commands/ReportCommand.cs

Phase 4 - User Story 2 (P2) - Locate and Understand Each Problem

Goal: Each finding shows where it occurred, rule failed, evidence, and suggested action.

Independent test criteria: For each category-specific fixture, detailed evidence fields match the expected manifest.

- [ ] T019 [US2] Add unit tests for each evidence shape asserting required fields (MissingCandle, TimeGap, DuplicateRow details, InvalidOhlc codes, ClosedMarket details, MalformedField entries) in tests/Validator.Domain.Tests/EvidenceShapeTests.cs
- [ ] T041 [US2] Create failing unit tests for evidence-reference joining, missing-reference rejection, and deterministic relationship expansion in tests/Validator.Application.Tests/EvidenceJoinerTests.cs
- [ ] T020 Implement evidence rendering join logic to satisfy T041 in src/Validator.Application/Reporting/EvidenceJoiner.cs
- [ ] T021 [P] Create failing unit tests for duplicate-group evidence streaming, participating-row traceability, and differing-fields calculation in tests/Validator.Application.Tests/DuplicateGroupTests.cs
- [ ] T042 [P] Implement duplicate-group evidence streaming and differing-fields calculation to satisfy T021 in src/Validator.Application/Validation/DuplicateGroupProcessor.cs
- [ ] T043 Create failing unit tests for missing-candle and time-gap generation, large gaps, and bidirectional relationship edges in tests/Validator.Application.Tests/MissingCandleAndTimeGapTests.cs
- [ ] T022 Implement missing-candle and time-gap generation to satisfy T043 in src/Validator.Application/Validation/MissingCandleProcessor.cs and src/Validator.Application/Validation/TimeGapProcessor.cs

Phase 5 - User Story 3 (P2) - Diagnose an Incomplete Validation

Goal: Fatal diagnostic explains where processing stopped and which checks did not run; v2 fatal output is on stderr for v2 JSON runs.

Independent test criteria: Running v2 against unreadable/invalid fixtures produces a single fatal v2 JSON on stderr, empty stdout, exit code 2, and destination unchanged.

- [ ] T023 [US3] Create failing tests for FatalDiagnostic aggregate and stage/class/code invariants in tests/Validator.Application.Tests/FatalDiagnosticTests.cs
- [ ] T024 Implement the `FatalDiagnostic` model so fatal outcomes cannot be represented as successful reports in src/Validator.Application/Reporting/FatalDiagnostic.cs
- [ ] T025 [US3] Create failing CLI process-level tests asserting exactly one v2 fatal document on stderr, empty stdout, exit code 2, and unchanged destination in tests/Validator.Cli.Tests/FatalV2RoutingTests.cs
- [ ] T044 [US3] Implement fatal v2 routing to satisfy T025, including empty successful-report destinations and no stdout report payload, in src/Validator.Cli/Commands/ReportCommand.cs and src/Validator.Infrastructure/Reporting/
- [ ] T045 [US3] Create failing process-level tests for invalid options, unresolved timeframe, missing/unreadable input, unwritable destination, render failure, commit failure, and input/output aliasing with distinct failure class/code/stage assertions in tests/Validator.Cli.Tests/OperationalFailureTests.cs
- [ ] T046 [US3] Implement operational/configuration failure classification and actionable diagnostics to satisfy T045 in src/Validator.Application/Reporting/ and src/Validator.Cli/Commands/

Phase 6 - User Story 4 (P3) - Consume and Compare Reports Reliably

Goal: Deterministic, self-describing, and repeatable reports.

Independent test criteria: Re-running the same input/config produces byte-identical substantive fields and finding order.

- [ ] T026 [US4] Add deterministic-ordering unit tests for ID generation, tie-breakers, UTC ordering, and overall finding ordering in tests/Validator.Application.Tests/DeterminismTests.cs
- [ ] T027 Implement stable FindingReference generation and deterministic tie-breaker ordering to satisfy T026 in src/Validator.Application/Reporting/FindingReferenceFactory.cs
- [ ] T028 [US4] Add an automated repeatability test that hashes two separate runs' v2 outputs and asserts identical hashes in tests/Validator.Cli.Tests/RepeatabilityTests.cs

Phase 7 - Compatibility, Scale, and Polish

- [ ] T047 [P] Create failing compatibility tests for concise text preservation, `--verbose` detailed text, unversioned JSON v1, explicit JSON v2 opt-in, and substantive verbose/v2 parity in tests/Validator.Cli.Tests/ReportCompatibilityTests.cs
- [ ] T048 Implement verbose rendering and preserve concise text/JSON v1 compatibility to satisfy T047 in src/Validator.Infrastructure/Reporting/VerboseReportWriter.cs and src/Validator.Cli/Commands/
- [ ] T030 Add schema validation tests that validate produced v2 JSON and fatal v2 JSON against contracts/detailed-report-v2.schema.json and contracts/fatal-diagnostic-v2.schema.json in tests/Validator.Cli.Tests/SchemaValidationTests.cs
- [ ] T049 [US1] Add a bounded-memory acceptance test using at least 100,000 findings, arbitrarily large duplicate groups and missing-candle gaps, hostile source text, cancellation, and interrupted report writes; assert complete output, configured buffer limits, cleanup, and no complete partial artifact in tests/Validator.Cli.Tests/LargeReportMemoryTests.cs
- [ ] T029 Update quickstart.md with exact commands used by integration tests and example expected outputs. Path: specs/002-detailed-error-report/quickstart.md
- [ ] T031 Ensure Domain and Application reach 100% line and branch coverage: add missing tests and coverage thresholds in CI configuration. Path: .github/workflows/coverage.yml
- [ ] T032 [P] Add documentation comments to public Domain/Application types created above. Paths: src/Validator.Domain/**, src/Validator.Application/**
- [ ] T033 Final review: run the full quickstart (restore, build, test, representative fixtures, compatibility cases, fatal cases, and large-report case) and record results in specs/002-detailed-error-report/research.md

Dependencies

- Phase 1 tasks T001-T002 must complete before Phase 2.
- Every implementation task follows its failing test: T003->T004, T005->T006, T007->T008, T034->T010, T035->T036, T037->T038, T013->T012, T039->T014, T040->T015, T018->T016, T041->T020, T021->T042, T043->T022, T023->T024, T025->T044, T045->T046, T026->T027, and T047->T048.
- Foundational tasks T003-T010 and T034-T038 must complete before the US1-US4 implementation tasks that depend on domain and application types.
- T011, T012, T014, T015, and T016 must complete before T017 and T049.
- T012 and T014-T016 must complete before US2 and US3 integration paths.
- US1 T011-T018 is the MVP path and should be prioritized; US2 and US3 extend the same catalog, spool, reconciliation, and report-outcome pipeline.

Parallel opportunities

- Tasks marked [P] can be worked in parallel when their listed dependencies are satisfied: independent model/evidence tests, source/spool integration tests, catalog tests, external-spool tests, evidence tests, compatibility tests, and documentation.

MVP suggestion

- Focus on User Story 1 T011-T018 plus foundational T003-T010 and T034-T040. Deliver a complete, reconciled v2 report for the cross-category fixture with bounded memory and atomic commit before expanding category-specific and fatal workflows.

Format validation

- All tasks follow the required checklist format with Task IDs, story labels for user-story phases, test-first implementation ordering, and explicit file paths.

Generated file: D:\financial-data-cleaner\specs\002-detailed-error-report\tasks.md
Total tasks: 49
Tasks per story/phase:
- Setup: 2
- Foundational: 13
- US1: 10
- US2: 7
- US3: 6
- US4: 3
- Compatibility/Scale/Polish: 8
Parallel opportunities identified: 11 tasks marked [P]
Suggested MVP scope: Foundational tasks plus User Story 1 (T011-T018)

Next steps: begin with the failing foundational tests, then implement their paired models and adapters. Run T011 before the US1 report pipeline is considered complete.
