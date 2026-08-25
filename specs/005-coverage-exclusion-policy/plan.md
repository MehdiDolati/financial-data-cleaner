# Implementation Plan: Coverage Exclusion Policy for Unreachable Defensive Code

**Branch**: `005-coverage-exclusion-policy` | **Date**: 2026-08-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/005-coverage-exclusion-policy/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Make the enforced coverage gate tell the same story as the charter: a true **100%
line and 100% branch** over all *reachable* Domain and Application code, with the
handful of genuinely-unreachable defensive arms individually excluded, justified,
and reviewable — instead of today's sub-100% ratchet (`99.2%` line / `97.9%`
branch) held together by a prose footnote in `.github/workflows/coverage.yml`.

Technical approach: (1) re-enumerate the current gaps with `tools/coverage-gaps.ps1`
and classify each per an ordered decision rule (**test → restructure/remove →
exclude**); (2) cover every *reachable* branch with a test; (3) isolate the
genuinely-unreachable remainder to the smallest declarable scope and annotate it
with `[ExcludeFromCodeCoverage(Justification = "…")]`; (4) raise the CI gate to
`-LineThreshold 100 -BranchThreshold 100` and delete the ratchet; (5) add a
reflection test that fails if any exclusion lacks a justification; (6) align the
README architecture note, the `coverage.yml` description, and the constitution so
all three describe the same "true-100%-over-reachable-code" model. No product
behavior, output, contract, finding order, or exit code changes.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`, `LangVersion=preview`, nullable enabled, warnings-as-errors)

**Primary Dependencies**: Coverlet (`coverlet.collector` 4.0.0) for measurement; `ReportGenerator` 5.1.17; xUnit 2.6.0 + FluentAssertions 6.11.0 for tests; `System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute` (BCL) as the exclusion mechanism.

**Storage**: N/A (governance/quality change; no persistence).

**Testing**: xUnit across four suites (`Validator.Domain.Tests`, `Validator.Application.Tests`, `Validator.Infrastructure.Tests`, `Validator.Cli.Tests`); coverage for Domain + Application is produced by all four and **merged** by `tools/coverage-run.ps1`, because the unit suites exercise them directly while the CLI/Infrastructure suites exercise them through the real pipeline.

**Target Platform**: Cross-platform CI (ubuntu-latest, windows-latest, macos-latest) on the .NET 10 SDK; GitHub Actions.

**Project Type**: Single .NET solution with four projects (Domain → Application → Infrastructure/Presentation) plus a CLI. This feature is an internal quality-and-governance change touching CI config, the two business-logic assemblies, their test suites, and documentation.

**Performance Goals**: N/A — no runtime behavior changes. The merged coverage run is not held to a latency target; determinism (Principle IV) is preserved because measurement does not alter business logic.

**Constraints**: Product behavior is **frozen** (FR-011): existing outputs, report contracts, finding order, and exit codes are unaffected. Exclusions MUST be provably unreachable (FR-003), justified (FR-004), and applied at the smallest scope that isolates only unreachable code (FR-005). The gate MUST NOT depend on any threshold below 100% (FR-002). Defensive arms kept for defense-in-depth are preserved, not deleted to reach a number (FR-014).

**Scale/Scope**: Domain + Application only. Composition roots (`Program.cs`) and thin adapter/wiring code and the Infrastructure/CLI layers remain exempt from the gate per the charter and are out of scope. Starting inventory of defensive arms is the set enumerated in `coverage.yml` (private-constructor invariants, closed-union default arms, an out-of-order orchestrator gate, async state-machine internals, and a few `ToleranceResolver`/`PowerOfTen`/`ParseOhlcvField` arms); the exact set is re-enumerated during implementation.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

This feature exists specifically to make CI enforcement match **Principle II**, so it
is aligned with the constitution by construction rather than in tension with it.

| Principle | Status | Rationale |
|---|---|---|
| I. Test-First (NON-NEGOTIABLE) | PASS | Every reachable branch that is currently uncovered gets a failing test before it is credited; the "each exclusion carries a justification" enforcement is itself introduced as a failing test first (red-green). Where a branch is restructured to isolate an unreachable arm, the reachable behavior is pinned by a test before the refactor. |
| II. Business Logic Framework-Agnostic & Fully Covered | PASS (strengthens) | This is the core outcome: the enforced gate becomes a true 100% line/branch over reachable Domain+Application code, replacing the ratchet. Exclusions are limited to provably-unreachable defensive code and are justified and enumerable. |
| III. Clean (Hexagonal) Architecture | PASS | No dependency direction or layering changes. Exclusions are member-level attributes on existing Domain/Application code; any restructuring only extracts an unreachable arm into a small same-layer helper. |
| IV. Deterministic, Reproducible Results | PASS | Measurement and annotations do not touch business logic; identical inputs still produce identical outputs. |
| V. Fail Safe, Never Fail Silent | PASS (strengthens) | Unreachable defensive guards are **preserved** (excluded, not deleted) per FR-014, keeping the second line of defense. Reachable guards are proven by tests. |
| VI. Observable and Auditable | PASS (strengthens) | Every exclusion becomes an explicit, justified, enumerable, reviewable artifact (`[ExcludeFromCodeCoverage(Justification=…)]` + a reflection test), replacing an un-auditable prose footnote. |
| VII. Simplicity, Extension Points Where Cheap | PASS | Uses the standard BCL attribute and the existing coverage tooling; adds no new package, tool, port, or persistence. |
| VIII. Documentation Ships with the Feature | PASS | README architecture note, the `coverage.yml` description, and the constitution are updated in the same change (FR-012, FR-013); a durable decision-rule doc is added. See "README impact" below. |

**README impact (Principle VIII)**: **Required.** The Architecture section currently
states "coverage workflow currently measures 99.28% line and 97.97% branch coverage
and enforces 99.2% and 97.9% ratchet thresholds; the remaining paths are defensive
arms…". It MUST be rewritten to describe a true 100%/100% gate over reachable code
with documented, justified exclusions, and to link the new decision-rule doc.

**Governance impact (FR-013)**: **Required.** Principle II's wording is clarified to
state that 100% is measured over *reachable* code with documented, justified
exclusions. Recommended as a **PATCH** clarification (1.1.0 → 1.1.1) because the
*intent* of Principle II is unchanged; final bump type is a research decision (see
research.md) and is recorded with a rationale in the constitution's Sync Impact Report.

**Result**: No violations. Complexity Tracking is not required.

## Project Structure

### Documentation (this feature)

```text
specs/005-coverage-exclusion-policy/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   ├── coverage-gate.md         # The enforced gate contract (100/100, no ratchet)
│   ├── exclusion-record.md      # Exclusion format, justification, enumeration, review
│   └── decision-rule.md         # Ordered test → restructure → exclude rule
├── checklists/
│   └── requirements.md  # Pre-existing spec quality checklist (all pass)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

This feature modifies existing files rather than adding a new module. The concrete
paths it touches:

```text
.github/workflows/
├── coverage.yml            # CHANGE: gate to 100/100; replace ratchet footnote with the true-100% model
└── ci.yml                  # CONFIRM: Domain per-suite gate already at 100 (no ratchet); leave intact

tools/
├── coverage-run.ps1        # CONFIRM: already accepts -LineThreshold/-BranchThreshold and gates line+branch separately
└── coverage-gaps.ps1       # USE: re-enumerate the current gaps to classify each arm

src/Validator.Domain/        # ANNOTATE/RESTRUCTURE: reachable branches tested; unreachable arms excluded at smallest scope
src/Validator.Application/   # ANNOTATE/RESTRUCTURE: same; this layer holds the current gap
    Scoring/                 #   MetricScore, DatasetScore private-ctor invariant arms; closed-union default arms
    Validation/              #   DetailedValidationOrchestrator reconciliation gate + async state-machine internals
    Comparison/              #   ToleranceResolver static ctor, PowerOfTen loop, ParseOhlcvField default arm

tests/Validator.Domain.Tests/       # ADD: tests for any reachable Domain branch surfaced by re-enumeration
tests/Validator.Application.Tests/  # ADD: tests for reachable Application branches + the exclusion-justification reflection test

README.md                    # CHANGE: Architecture section coverage model + link to the decision-rule doc
docs/
└── coverage-exclusion-policy.md   # NEW: durable decision rule + how exclusions are recorded, enumerated, reviewed
.specify/memory/constitution.md    # CHANGE: clarify Principle II wording; version bump + Sync Impact Report
```

**Structure Decision**: No new project or module is introduced (Principle VII). The
change lives in CI config (`.github/workflows/coverage.yml`), the two business-logic
assemblies and their test suites, and documentation (`README.md`, a new
`docs/coverage-exclusion-policy.md`, and `.specify/memory/constitution.md`). The
existing merged multi-suite coverage tooling (`tools/coverage-run.ps1` +
`tools/coverage-gaps.ps1`) is reused unchanged — this feature changes *what is
measured* and *the enforced threshold*, not the tooling choice.

## Complexity Tracking

> No Constitution Check violations. This section is intentionally empty.
