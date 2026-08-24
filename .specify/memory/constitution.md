<!--
Sync Impact Report
- Version change: 1.1.0 -> 1.1.1
- Modified principles: II. Business Logic Is Framework-Agnostic and Fully Covered
  (clarified: 100% is measured over reachable code with documented, justified exclusions)
- Added principles: None
- Added sections: None
- Removed sections: None
- Expanded guidance: None
- Removed guidance: None
- Follow-up TODOs: None
-->
# Financial Data Cleaner Constitution

This constitution governs a platform that starts with offline market-data
quality validation and is expected to grow into strategy research and,
eventually, a deployment/execution pipeline. Each stage arrives as its own
spec-kit feature (its own `/speckit.specify` → `/speckit.plan` →
`/speckit.tasks` → `/speckit.implement` cycle); the principles below apply to
all of them and are checked at the `/speckit.plan` "Constitution Check" gate.

## Core Principles

### I. Test-First (NON-NEGOTIABLE)
Every unit of behavior gets a failing test before its implementation is
written (red-green-refactor). No production code is written ahead of a test
that requires it. This applies to every module the platform grows to
include — data validation today, strategy backtesting and execution later —
not just the first feature.

### II. Business Logic Is Framework-Agnostic and Fully Covered
Domain and Application-layer code — the actual business rules — carries zero
dependency on any UI, transport, or infrastructure framework, and is held to
100% line and branch coverage over *reachable* code, enforced in CI.
Genuinely-unreachable defensive arms (private-constructor invariants,
compiler-generated async state-machine helpers, and other provably unreachable
branches) are individually excluded with documented justifications via
`[ExcludeFromCodeCoverage(Justification=…)]`. Composition roots and thin
adapter/wiring code are exempt from the coverage gate but MUST be covered by
integration or end-to-end tests instead. This is what lets any module be
driven by a CLI today and a web UI, API, or scheduler tomorrow without
touching the logic itself.

### III. Clean (Hexagonal) Architecture, Always
Every module is layered Domain → Application → Infrastructure/Presentation,
dependencies point inward only, and all environment-touching concerns
(file system, console, network, clock) are reached only through interfaces
owned by the Application layer. As the platform grows into multiple modules,
each is built this way independently, and modules talk to each other through
explicit, versioned contracts — never shared mutable state or reach-through
into another module's internals.

### IV. Deterministic, Reproducible Results
Given the same input data and configuration, any validation, analysis, or
backtest MUST produce identical output every time it runs. No hidden
dependence on wall-clock time, ambient OS locale, machine-specific
floating-point behavior, or unseeded randomness inside business logic. This
matters more with every module added: a backtest that isn't reproducible
isn't trustworthy, and a system feeding live decisions isn't auditable if it
can't be replayed.

### V. Fail Safe, Never Fail Silent
When input is invalid, ambiguous, or out of range, the system MUST stop and
report rather than guess and continue. This project starts as a data-quality
tool for exactly this reason; the same posture stays non-negotiable once the
pipeline reaches strategy execution or live deployment, where a silently
tolerated bad value is a financial risk, not a cosmetic bug.

### VI. Observable and Auditable by Default
Every run — validation today, backtests or trading decisions later —
produces a structured, machine-readable record of what happened and why, not
just a pass/fail. Findings/decisions are categorized, timestamped, and
traceable back to the specific input record that caused them. This is built
in from day one because it is far more expensive to retrofit once real
capital is involved.

### VII. Simplicity Now, Extension Points Where They're Cheap
Build only what the current spec asks for — no speculative features "for
later." Keep module boundaries (interfaces, contracts, data shapes)
intentionally clean so that later modules — strategy discovery, optimization,
deployment — can be added as new, independently specified features that
*consume* this one's output, rather than requiring this one to be reopened
and modified.

### VIII. Documentation Ships with the Feature
Every feature or behavior change MUST assess its documentation impact and MUST
update `README.md` in the same change whenever it affects installation, build
steps, usage, command-line options, inputs, outputs, exit behavior, public
contracts, architecture, or contributor workflow. README examples and links
MUST describe the implemented behavior and current contract versions. If a
feature has no README impact, its plan or review MUST record that conclusion
and the concrete reason; silence is not evidence that documentation was
considered. Documentation is part of the feature's public interface, so a
change with required but stale documentation is incomplete.

## Technology Standards

- Platform: C# / .NET (currently .NET 10) is the default for every module in
  this pipeline, for consistency. A future spec MAY introduce a different
  runtime for a specific module (e.g. a research notebook environment) only
  with documented justification — this is an intentional exception process,
  not a default.
- All numeric, date, and time parsing/formatting is culture-invariant; no
  module depends on host machine locale.
- Any value that influences a trading, reporting, or risk decision uses a
  fixed-point-safe numeric type (`decimal` in .NET) — never `float`/`double`.
- Time values are normalized to UTC internally in every module; a module may
  accept and display other offsets, but never computes against them directly.

## Development Workflow

- Every module ships through the full spec-kit cycle: `/speckit.specify` →
  `/speckit.clarify` (as needed) → `/speckit.plan` → `/speckit.tasks` →
  `/speckit.implement`, with `/speckit.checklist` and `/speckit.analyze` as
  recommended (not mandatory) quality gates before implementation begins.
- Every feature plan MUST identify whether the feature changes `README.md` and
  name the affected sections when it does. Generated feature tasks MUST include
  the required README work before final validation; when no README update is
  required, the plan or final review MUST state why.
- A module is "done" only when: its tests were written first (Principle I),
  its business-logic coverage is 100% (Principle II), and it could be driven
  by an alternate front end without source changes to Domain/Application
  (Principle III) — even if that front end isn't built yet — and its README
  impact has been resolved (Principle VIII).
- Findings from one module (e.g. a data-quality report) are treated as a
  typed contract other modules can depend on, not an implementation detail —
  future specs (e.g. a backtester) consume this validator's output shape
  directly rather than re-deriving it.

## Governance

This constitution supersedes ad hoc convention wherever the two conflict.
Amendments require a documented rationale and a version bump below; any
in-flight plan that conflicts with an amendment is re-checked against it
before implementation continues. Feature plans, task lists, implementation
reviews, and pull-request reviews MUST verify Principle VIII explicitly by
confirming the README is updated or by recording a specific no-impact
rationale. Constitution versions follow semantic versioning: MAJOR for
incompatible governance changes, MINOR for new or materially expanded rules,
and PATCH for non-semantic clarification.

**Version**: 1.1.1 | **Ratified**: 2026-08-05 | **Last Amended**: 2026-08-24
