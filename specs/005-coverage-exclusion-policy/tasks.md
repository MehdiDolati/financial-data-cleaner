---
description: "Task list for Coverage Exclusion Policy for Unreachable Defensive Code"
---

# Tasks: Coverage Exclusion Policy for Unreachable Defensive Code

**Input**: Design documents from `/specs/005-coverage-exclusion-policy/`

**Prerequisites**: plan.md, spec.md (updated with 2026-08-23 clarifications), research.md, data-model.md, contracts/coverage-gate.md, contracts/exclusion-record.md, contracts/decision-rule.md, quickstart.md

**Tests**: **REQUIRED.** Constitution Principle I (Test-First, NON-NEGOTIABLE) and the clarified decision (Q2) that an automated, build-failing check enforces justifications mean every reachable branch is covered by a failing test written *first*, and the justification guard is itself a red-first test.

**Organization**: Tasks are grouped by user story (US1–US4 from spec.md) so each story is independently implementable and testable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1, US2, US3, US4 (setup/foundational/polish carry no story label)
- Every task lists an exact file path.

## Path Conventions

.NET solution (per plan.md): business logic in `src/Validator.Domain/` and `src/Validator.Application/`; tests in `tests/Validator.*.Tests/`; CI in `.github/workflows/`; tooling in `tools/`; governance in `.specify/memory/`; contributor docs in `docs/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish the authoritative starting inventory of uncovered code the whole feature acts on.

- [ ] T001 Run the merged coverage baseline and enumerate every uncovered line and branch for `Validator.Domain` + `Validator.Application` by executing `tools/coverage-run.ps1` then `tools/coverage-gaps.ps1` (quickstart Scenario 0); paste the resulting uncovered inventory into `specs/005-coverage-exclusion-policy/research.md` under a new "## Baseline inventory (measured)" heading as the authoritative target list.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Classify the inventory so US1 and US3 know exactly what to test vs. exclude. **No US1/US3 work begins until T002 is complete.**

**⚠️ CRITICAL**: This classification is the shared foundation for the substantive stories.

- [ ] T002 Classify every uncovered arm from T001 as **test**, **restructure/remove**, or **exclude** by applying the reachability rule (spec.md Key Concepts + Clarifications Q1 + research.md §6): any arm reachable via the public API, a test-visible internal entry point, or an out-of-range/undeclared-enum-cast value is **test**; a mixed unit is **restructure**; only arms no test can execute by any means are **exclude**. Record the classification as a table appended to `specs/005-coverage-exclusion-policy/research.md` under "## Baseline inventory (measured)".
- [ ] T003 [P] Add `InternalsVisibleTo("Validator.Application.Tests")` (and the Domain equivalent in `src/Validator.Domain/Validator.Domain.csproj` if the classification needs it) to `src/Validator.Application/Validator.Application.csproj` ONLY if T002 marks any arm reachable exclusively through a test-visible internal entry point; otherwise record "not required" in research.md and skip.

**Checkpoint**: Every gap has a disposition. US1 and US3 can proceed.

---

## Phase 3: User Story 1 - An honest, enforceable 100% gate (Priority: P1) 🎯 MVP

**Goal**: Make the enforced gate a true 100% line / 100% branch over reachable Domain+Application code with the ratchet removed — by testing every reachable arm, isolating and excluding only the genuinely unreachable remainder, and raising the threshold.

**Independent Test**: `tools/coverage-run.ps1 -LineThreshold 100 -BranchThreshold 100` exits 0 on a clean build (Scenario 1); dropping a reachable test makes it fail and name the line (Scenario 3); no sub-100% threshold remains (Scenario 2).

### Tests for User Story 1 (write first, MUST FAIL before implementation) ⚠️

- [ ] T004 [P] [US1] For each **test**-classified reachable arm in the scoring closed-unions, add failing tests exercising the default/guard arm via out-of-range enum casts (mirroring the existing `(MetricPopulationKind)99` / `FindingCategory.Critical` pattern) in `tests/Validator.Application.Tests/Scoring/ScoringModelGuardTests.cs` covering `src/Validator.Application/Scoring/MetricPopulations.cs`, `MetricPopulationMap.cs`, and `ScoreSectionBuilder.cs`.
- [ ] T005 [P] [US1] For each **test**-classified reachable arm in the reporting closed-unions, add failing tests via out-of-range enum casts / undersized inputs in `tests/Validator.Application.Tests/Reporting/ReportingClosedUnionGuardTests.cs` covering `src/Validator.Application/Reporting/DetailedSummary.cs`, `EvidenceJoiner.cs`, `FindingCatalog.cs`, `FindingCatalogStatistics.cs`, and `FindingReferenceFactory.cs`.
- [ ] T006 [P] [US1] For each **test**-classified reachable arm in Comparison, add failing tests (e.g. `ParseOhlcvField` unknown-field throw, timeframe/instrument mismatch guards) in `tests/Validator.Application.Tests/Comparison/ComparisonGuardTests.cs` covering `src/Validator.Application/Comparison/ToleranceResolver.cs` and `CompareDatasetsUseCase.cs`.
- [ ] T007 [P] [US1] For any **test**-classified reachable arm in Domain surfaced by T001, add failing tests in the matching `tests/Validator.Domain.Tests/**` file covering the specific `src/Validator.Domain/**` member.

### Implementation for User Story 1

- [ ] T008 [US1] Make T004–T007 pass by exercising the existing production arms (no product-behavior change); confirm each newly added test now goes green via `dotnet test` for the affected suites.
- [ ] T009 [US1] Restructure every **restructure**-classified unit so the unreachable arm is isolated into its own smallest member while reachable logic stays inline and measured (e.g. extract the out-of-order reconciliation gate and async helpers in `src/Validator.Application/Validation/DetailedValidationOrchestrator.cs`); keep behavior identical (FR-011).
- [ ] T010 [P] [US1] Apply `[ExcludeFromCodeCoverage(Justification="…")]` at the smallest scope to the **exclude**-classified private-constructor invariant arms in `src/Validator.Application/Scoring/MetricScore.cs` and `src/Validator.Application/Scoring/DatasetScore.cs`, each justification naming the factory/guard that makes the arm unreachable (contracts/exclusion-record.md E1–E5).
- [ ] T011 [P] [US1] Apply `[ExcludeFromCodeCoverage(Justification="…")]` to the **exclude**-classified out-of-order reconciliation gate and compiler-generated async state-machine helpers isolated in T009, in `src/Validator.Application/Validation/DetailedValidationOrchestrator.cs`.
- [ ] T012 [P] [US1] Apply `[ExcludeFromCodeCoverage(Justification="…")]` to the **exclude**-classified `ToleranceResolver` static constructor, `PowerOfTen` positive-exponent loop, and any genuinely-unreachable `ParseOhlcvField` remainder in `src/Validator.Application/Comparison/ToleranceResolver.cs`.
- [ ] T013 [US1] Raise the enforced gate to a true 100/100 and delete the ratchet numbers by changing the run step to `./tools/coverage-run.ps1 -LineThreshold 100 -BranchThreshold 100` in `.github/workflows/coverage.yml` (contracts/coverage-gate.md G1–G3).
- [ ] T014 [US1] Verify the merged gate passes: run `tools/coverage-run.ps1 -LineThreshold 100 -BranchThreshold 100` (exit 0) and confirm `tools/coverage-gaps.ps1` prints "No uncovered lines or branches. Full coverage." (Scenario 1, SC-001).
- [ ] T015 [US1] Perform the one-time regression demonstration (Clarifications Q3, Scenario 3): temporarily disable one reachable test, confirm the gate exits non-zero and names the uncovered line, restore the test, and record the outcome in `specs/005-coverage-exclusion-policy/quickstart.md` Scenario 3 (SC-005). No permanent self-referential test is added.

**Checkpoint**: The gate is a true 100/100 over reachable code with no ratchet — MVP complete and independently demonstrable.

---

## Phase 4: User Story 2 - A clear rule for defensive branches (Priority: P2)

**Goal**: Publish the ordered decision rule (test → restructure → exclude) so any contributor handles a defensive branch consistently.

**Independent Test**: Given only the doc, a contributor selects the correct disposition for a reachable arm, an unreachable arm, and a mixed unit (Scenario 5, SC-007).

- [ ] T016 [US2] Author `docs/coverage-exclusion-policy.md` implementing the ordered rule and guarantees D1–D5 from `specs/005-coverage-exclusion-policy/contracts/decision-rule.md` (test-it default incl. out-of-range/enum-cast reachability; restructure/remove for mixed units; exclude-with-justification at smallest scope as last resort; preserve defense-in-depth; revisit when a branch becomes reachable).
- [ ] T017 [US2] Validate the doc against the three cases in quickstart Scenario 5 (reachable→test, unreachable→exclude, mixed→test/restructure) and record the confirmation in `docs/coverage-exclusion-policy.md` or the PR description (US2 AC1–AC3, SC-007).

**Checkpoint**: The durable decision rule exists and demonstrably yields the right disposition.

---

## Phase 5: User Story 3 - Auditable exclusions reviewed like code (Priority: P2)

**Goal**: Guarantee every exclusion is justified and the full set is enumerable, enforced automatically.

**Independent Test**: A blank/missing justification fails the build; the test enumerates every exclusion with its justification (Scenario 4, SC-002, FR-004/FR-008).

### Tests for User Story 3 (write first, MUST FAIL before implementation) ⚠️

- [ ] T018 [US3] Write a failing reflection test that scans the `Validator.Domain` and `Validator.Application` assemblies for `ExcludeFromCodeCoverageAttribute` and asserts each carries a non-blank `Justification`; prove it red by temporarily seeding one blank-justification exclusion, in `tests/Validator.Application.Tests/Coverage/ExclusionJustificationTests.cs` (contracts/exclusion-record.md E2, FR-004).

### Implementation for User Story 3

- [ ] T019 [US3] Remove the temporary seed from T018 and confirm the test passes against the real exclusions added in US1 (every one carries a justification), keeping the build green (US3 AC1, SC-002).
- [ ] T020 [US3] Extend the test (or add a sibling in the same file) to emit the full enumerated list of exclusions with their justifications so a reviewer can answer "what is excluded and why," in `tests/Validator.Application.Tests/Coverage/ExclusionJustificationTests.cs` (FR-008, US3 AC2).

**Checkpoint**: Exclusions are self-auditing — unjustified ones break the build and the set is enumerable.

---

## Phase 6: User Story 4 - One consistent story across charter, CI, and docs (Priority: P3)

**Goal**: Make the README, CI configuration, and constitution describe the same true-100%-over-reachable-code model.

**Independent Test**: Reading all three, none contradicts the others on measurement, exclusions, or the enforced target (Scenario 6, SC-004).

- [ ] T021 [P] [US4] Rewrite the Architecture coverage paragraph in `README.md` from the "99.28%/97.97% … 99.2%/97.9% ratchet … defensive arms" wording to "a true 100% line and branch over reachable Domain/Application code with documented, justified exclusions," and link `docs/coverage-exclusion-policy.md` (FR-012, US4 AC1).
- [ ] T022 [US4] Replace the ratchet footnote in the header comment of `.github/workflows/coverage.yml` with the true-100% description consistent with the 100/100 gate from T013 (FR-012; depends on T013 — same file).
- [ ] T023 [P] [US4] Clarify Principle II in `.specify/memory/constitution.md` to state 100% is measured over reachable code with documented, justified exclusions; bump the version **1.1.0 → 1.1.1** and record the rationale in the Sync Impact Report header (Clarifications Q4, FR-013, US4 AC2).
- [ ] T024 [US4] Verify no contradiction across `README.md`, `.github/workflows/coverage.yml`, and `.specify/memory/constitution.md` on how coverage is measured, what is excluded, and the enforced target (Scenario 6, SC-004).

**Checkpoint**: All three sources tell one honest, consistent story.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Confirm no product behavior changed and documentation is consistent.

- [ ] T025 [P] Run the full solution test suite `dotnet test FinancialDataCleaner.slnx --configuration Release` and confirm all pre-existing product/contract tests pass with unchanged outputs, finding order, and exit codes (Scenario 7, FR-011, SC-006).
- [ ] T026 [P] Run `tools/doc-status.ps1` and resolve any documentation drift this change introduced.
- [ ] T027 Execute quickstart.md Scenarios 0–7 end to end and confirm every item in the Success checklist passes.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on T001. **Blocks US1 and US3.**
- **User Stories (Phase 3–6)**: US1 and US3 depend on T002; US2 and US4 depend only on Setup and can begin in parallel with US1 (though T022 waits on T013).
- **Polish (Phase 7)**: Depends on all targeted stories being complete.

### User Story Dependencies

- **US1 (P1)**: After T002. Delivers the MVP (the honest gate). Independently testable.
- **US2 (P2)**: Independent (documentation only); may start after Setup.
- **US3 (P2)**: After T002; its green state (T019) depends on US1 having added the real exclusions, but the red test (T018) can be written independently via a seed.
- **US4 (P3)**: Independent, except T022 edits the same file as T013 (US1) and must follow it.

### Within Each User Story

- Tests (T004–T007, T018) are written and fail before implementation.
- Restructure (T009) precedes the exclusions it isolates (T011).
- Exclusions (T010–T012) precede raising the gate (T013) and verification (T014).

### Parallel Opportunities

- US1 test-writing: **T004, T005, T006, T007** run in parallel (different test files).
- US1 exclusions: **T010, T011, T012** run in parallel (different source files) after T008–T009.
- Cross-story: **US2 (T016)** and **US4 (T021, T023)** can proceed alongside US1.
- Polish: **T025, T026** run in parallel.

---

## Parallel Example: User Story 1 test-writing

```text
# Launch the reachable-arm test tasks together (different files):
Task: "T004 scoring closed-union guard tests in tests/Validator.Application.Tests/Scoring/ScoringModelGuardTests.cs"
Task: "T005 reporting closed-union guard tests in tests/Validator.Application.Tests/Reporting/ReportingClosedUnionGuardTests.cs"
Task: "T006 comparison guard tests in tests/Validator.Application.Tests/Comparison/ComparisonGuardTests.cs"
Task: "T007 Domain reachable-arm tests in tests/Validator.Domain.Tests/**"

# After T008–T009, launch the exclusion annotations together (different files):
Task: "T010 exclude scoring invariant arms in src/Validator.Application/Scoring/MetricScore.cs, DatasetScore.cs"
Task: "T011 exclude orchestrator gate/async helpers in src/Validator.Application/Validation/DetailedValidationOrchestrator.cs"
Task: "T012 exclude ToleranceResolver arms in src/Validator.Application/Comparison/ToleranceResolver.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup (T001) → Phase 2 Foundational (T002–T003).
2. Phase 3 US1 (T004–T015).
3. **STOP and VALIDATE**: the merged gate passes at 100/100 and a dropped reachable test fails it.
4. This alone delivers the honest, enforceable gate the request asked for.

### Incremental Delivery

1. Setup + Foundational → classification ready.
2. US1 → true 100/100 gate → **MVP**.
3. US3 → exclusions become self-auditing (build fails on a blank justification).
4. US2 → durable decision rule published.
5. US4 → README, CI, and constitution reconciled.
6. Polish → confirm zero product-behavior change.

### Parallel Team Strategy

After T002: Developer A drives US1 (T004–T015); Developer B writes US2 (T016–T017) and US4 docs (T021, T023); US3 (T018) can be drafted red immediately and turned green (T019–T020) once US1's exclusions land; reconcile T022 after T013.

---

## Notes

- [P] = different files, no dependencies.
- Every exclusion MUST be minimal-scope and justified (contracts/exclusion-record.md); the T018 guard enforces this automatically.
- No product behavior, output, contract, finding order, or exit code changes (FR-011); T025 is the safety net.
- Domain is already at 100%; the substantive gap is in Application, but T001 re-enumerates authoritatively rather than trusting the stale `coverage.yml` footnote.
- Commit after each task or logical group; stop at any checkpoint to validate a story independently.
