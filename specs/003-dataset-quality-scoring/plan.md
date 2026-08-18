# Implementation Plan: Dataset Quality Scoring

**Branch**: `003-dataset-quality-scoring` | **Date**: 2026-08-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/003-dataset-quality-scoring/spec.md`

## Summary

Score one validation run across the six established quality metrics on a 0-to-100
scale, `100 × (1 − defect rate)`, and report one weighted-mean average of exactly
the metrics that were scored. Scoring is opt-in through `--score`, weights are
overridable through `--score-weights`, and scores are additive: the six summary
counts, the findings, the finding order, the source dataset, and the exit codes
are untouched.

Scores are derived only from values the existing validation run already
establishes — the six summary counts, scan coverage, and per-check status — plus
one new deterministic population, the expected open-market candle count, which is
counted during the sequence walk the orchestrator already performs. No new check
runs and the dataset is not re-scanned.

Because a metric's defect rate is a ratio of integers, per-metric scores and the
weighted average are computed as exact rationals over `BigInteger` and rounded
once, half away from zero, to two decimal places for presentation only. This
avoids both `float`/`double` and accumulated decimal division drift, so the
average is reproducible by hand from the counts, populations, and weights printed
in the report.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (`net10.0`)

**Primary Dependencies**: Existing CsvHelper, NodaTime, xUnit, FluentAssertions,
and Coverlet dependencies. `System.Numerics.BigInteger` and `System.Text.Json`
come from the BCL. No new production or test package is required.

**Storage**: None. Scoring consumes in-run values only; it opens no file, creates
no temporary artifact, and never writes to the source dataset.

**Testing**: Test-first xUnit theories for exact-rational arithmetic, rounding,
per-metric score derivation, applicability and zero-population states, weight
parsing/validation, and average coverage; Infrastructure tests for the shared
text scoring section and the v2 `scoring` object; process-level CLI tests for
opt-in behaviour, the v1 configuration conflict, byte-identical unscored output,
determinism, and schema validation. Domain and Application scoring code is held
to 100% line and branch coverage.

**Target Platform**: Windows, Linux, and macOS with the .NET 10 runtime; offline
execution with no network dependency.

**Project Type**: Existing reusable Clean Architecture library plus CLI.

**Performance Goals**: No additional pass over the dataset and no per-row
allocation. The only new work inside the scan is one increment per expected
open-market slot in the sequence walk the orchestrator already performs; scoring
itself is fixed-size work over six metric records.

**Constraints**: Scoring is opt-in and additive; the JSON v1 contract is
unchanged and requesting scores with it fails fast; the six summary lines,
findings, finding order, source bytes, and exit codes are unchanged; all
arithmetic uses exact rationals or `decimal` and never `float`/`double`; output
is culture-invariant at exactly two decimal places using half-away-from-zero
rounding; a defect rate outside 0..1 fails the run instead of being clamped;
invalid weights and the v1 conflict are rejected before the dataset is read; a
fatal run emits no score.

**Scale/Scope**: One dataset per run, six metrics, one average, three population
kinds (expected candles, accepted rows, examined rows), 64-bit counts and
populations, and two reporting surfaces (human-readable text and JSON v2).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

### Pre-Research Gate

| Principle | Result | Plan Evidence |
|---|---|---|
| I. Test-First | PASS | Every scoring behaviour — rational arithmetic, rounding, each metric's population, applicability states, weight rejection, average coverage, rendering, and CLI routing — is introduced by a failing test first. `tasks.md` must order each test immediately before the behaviour it requires. |
| II. Framework-Agnostic, Fully Covered Business Logic | PASS | Exact-ratio arithmetic and score formatting live in Domain; metric scores, populations, weighting, average, and the score section live in Application. Neither references a serializer, console, or file system, and both stay under the 100% line and branch gate. |
| III. Clean Architecture | PASS | Application computes the complete score section and exposes it on the existing report aggregate; Infrastructure only renders it as text or JSON; the CLI only parses `--score`/`--score-weights` and routes. Scoring adds no new environment-touching concern, so no new port is required. |
| IV. Deterministic Results | PASS | Scores derive from integer counts and populations through exact rationals, are formatted invariantly to two decimals, are emitted in the established category order, and contain no wall-clock, locale, random, or `float`/`double` input. |
| V. Fail Safe | PASS | A defect rate outside 0..1 is an internal inconsistency that fails the run rather than being clamped; an unavailable average is stated with its reason instead of being substituted with 0.00 or 100.00; invalid weights and the v1 conflict fail before the dataset is read; a fatal run emits no score. |
| VI. Observable and Auditable | PASS | JSON v2 exposes each metric's score, count, population, population kind, state, reason, resolved weight, and normalised share, plus the average, its metric coverage, its excluded metrics, and its unavailability reason, as documented fields. |
| VII. Simplicity with Cheap Extension Points | PASS | The feature reuses the six established metrics, the existing detailed pipeline, the existing fatal codes, and the existing report aggregate. It adds no new check, no new fatal code or stage, no new port, no persistence, and no new package. |
| VIII. Documentation Ships with the Feature | PASS (impact identified) | `README.md` requires updates: the Options table gains `--score` and `--score-weights`, Usage gains a scored example, and Output documents the scoring text section, the optional v2 `scoring` field, and the v1 conflict. `tasks.md` must include this work before final validation. |

No constitutional violations require a complexity exception.

### Post-Design Re-check

PASS. `research.md` resolves every open technical question without leaving a
NEEDS CLARIFICATION marker, and each decision was checked against the gate it
touches:

- Exact rational arithmetic (R1) satisfies Principles IV and the spec's
  exactness rule while keeping `float`/`double` out of the code base entirely.
- Reusing `REPORT_RECONCILIATION_FAILED` and `INVALID_ARGUMENT` (R6) keeps
  Principle VII intact by leaving the frozen v2 fatal contract's closed `code`
  and `stage` enumerations untouched.
- Adding `scoring` as an optional property of the existing v2 schema (R7) keeps
  the contract versioned and additive: absent when scoring is not requested, so
  every existing v2 consumer and golden test is unaffected.
- Routing scored text through the detailed pipeline (R4) is what makes
  populations and check applicability available at all; the six established
  lines are emitted from one shared label list and pinned by a test that
  compares them byte-for-byte against an unscored run, protecting SC-006.
- `data-model.md` keeps every state explicit — `Scored`, `NotApplicable`, and
  `NotScored` each carry a reason when they carry no value — so no metric can be
  silently credited as flawless and no average can hide its coverage.
- `contracts/` defines the CLI surface, the additive v2 amendment, and the
  Application API an alternate front end would drive, satisfying Principle III's
  "could be driven by another front end" test.
- `quickstart.md` validates hand-recalculation, unavailable averages, weight
  rejection, determinism, v1 immutability, the v1 conflict, source protection,
  and the unchanged unscored output.

No constitutional violation remains, and no entry is required in Complexity
Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/003-dataset-quality-scoring/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── checklists/
│   └── requirements.md
├── contracts/
│   ├── application-api.md
│   ├── cli.md
│   ├── detailed-report-v2-amendment.md
│   └── scoring-v2.schema.json
└── tasks.md                          # Created by /speckit-tasks, not this command
```

### Source Code (repository root)

```text
src/
├── Validator.Domain/
│   └── Scoring/                      # ExactRatio arithmetic and invariant 2-dp score formatting
├── Validator.Application/
│   ├── Scoring/                      # Metric scores, populations, weighting, average, score section
│   ├── Reporting/                    # DetailedValidationReport gains an optional score section
│   └── Validation/                   # Orchestrator counts expected candles and builds the section
├── Validator.Infrastructure/
│   └── Reporting/                    # Shared text scoring section; scored concise text; v2 scoring object
└── Validator.Cli/
    └── Commands/                     # --score and --score-weights parsing, validation, and routing

tests/
├── Validator.Domain.Tests/
│   └── Scoring/                      # Ratio exactness, rounding, and formatting theories
├── Validator.Application.Tests/
│   └── Scoring/                      # Populations, applicability, weights, average coverage
├── Validator.Infrastructure.Tests/
│   └── Reporting/                    # Text section and v2 scoring rendering
└── Validator.Cli.Tests/              # Opt-in, v1 conflict, determinism, schema, unchanged output
```

**Structure Decision**: Retain the four existing Clean Architecture projects.
Feature 003 is a derived-measurement extension of features 001 and 002, not a new
module, so it adds one folder per existing layer rather than a new project.
Domain owns exact ratio arithmetic and score formatting because both are pure
value semantics; Application owns the metric/weight/average model because it
needs the established summary, scan coverage, and check statuses; Infrastructure
owns rendering only; the CLI owns option parsing and routing only.

## Complexity Tracking

No constitution violations require justification.
