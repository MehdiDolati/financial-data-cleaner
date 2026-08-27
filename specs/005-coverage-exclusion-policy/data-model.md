# Phase 1 Data Model: Coverage Exclusion Policy

**Feature**: `005-coverage-exclusion-policy` | **Date**: 2026-08-23

This is a governance-and-quality feature: it introduces **process and CI-config
entities**, not runtime domain types. No C# record, class, table, or serialized
contract is added to the product. The "entities" below are the conceptual objects
the policy operates on; their concrete representations are source attributes, a CI
threshold, a test, and Markdown docs. They map directly to the spec's Key Concepts
and Functional Requirements.

---

## Entity: Exclusion Record

A single unit of unreachable code marked as excluded from coverage measurement,
paired with the justification for why it is unreachable.

| Field | Concrete representation | Rule |
|---|---|---|
| Target | The annotated member (method, local function, or extracted helper) | Smallest scope that isolates only unreachable code; a unit with reachable logic MUST NOT be excluded wholesale (FR-005, US2/AC3) |
| Marker | `[ExcludeFromCodeCoverage]` (BCL attribute) | Honored by Coverlet; removes target from numerator and denominator |
| Justification | `Justification = "…"` property | MUST be present and non-blank; states *why* the code is unreachable (FR-004, US3/AC1) |
| Unreachability | Prose in the justification + reviewer confirmation | MUST be provably unreachable through any legal call (FR-003, SC-003) |

**Validation rules**:
- An exclusion with a null/blank `Justification` is **invalid** and MUST fail the
  build (enforced by the justification reflection test).
- An exclusion covering reachable code is **invalid** (rejected in review; proven
  by the fact that removing coverage from reachable code still fails the gate).

**State transitions**:
```
Uncovered defensive branch
   ├─ reachable ───────────────► Tested            (FR-006, default; leaves Excluded set unchanged)
   ├─ reshapeable ─────────────► Restructured/Removed (reachable part Tested; remainder → Excluded)
   └─ provably unreachable ────► Excluded (justified, minimal scope)
Excluded ──(later change makes it reachable)──► Tested   (exclusion removed; edge case)
```

**Relationships**: Each Exclusion Record is one entry in the enumerable
**Exclusion Set** (FR-008). Every record is introduced by, and reviewed within, the
change that adds it (FR-009).

---

## Entity: Exclusion Set

The complete, enumerable collection of all Exclusion Records across Domain and
Application.

| Property | Representation | Rule |
|---|---|---|
| Members | All `[ExcludeFromCodeCoverage]` occurrences in the two business-logic assemblies | Enumerable by reflection and by source scan (FR-008, US3/AC2) |
| Every member justified | Reflection test assertion | Test fails if any member lacks a non-blank `Justification` |

**Relationships**: Consumed by the **Coverage Gate** (excluded members are outside
the measured population) and by reviewers answering "what is excluded and why".

---

## Entity: Coverage Gate

The CI check that enforces the coverage target over the measured (reachable) code
and fails the build when it is not met.

| Property | Representation | Rule |
|---|---|---|
| Line target | `-LineThreshold 100` → Coverlet `Threshold` (line) | Exactly 100; no value below 100 (FR-001, FR-002, SC-001) |
| Branch target | `-BranchThreshold 100` → Coverlet `Threshold` (branch) | Exactly 100; gated separately from line |
| Measured scope | Merged Domain+Application across all four suites, minus Exclusion Set | Reachable code only |
| Failure behavior | Non-zero exit; names the uncovered location | A newly uncovered reachable line/branch fails the build (FR-007, US1/AC2, SC-005) |
| No ratchet | No sub-100% threshold anywhere in config | The gate does not depend on a ratchet (FR-002, US1/AC3) |

**State transitions**:
```
Clean build, all reachable covered ──► PASS (100/100 over measured)   (US1/AC1)
Reachable line/branch loses coverage ─► FAIL (names location)         (US1/AC2, SC-005)
```

**Relationships**: Realized by `.github/workflows/coverage.yml` invoking
`tools/coverage-run.ps1`. Subtracts the **Exclusion Set** from its population.

---

## Entity: Decision Rule

The documented, ordered guidance contributors apply to any defensive branch.

| Property | Representation | Rule |
|---|---|---|
| Ordering | `test → restructure/remove → exclude` | Preference order; exclude is last resort (FR-010, SC-007) |
| Reachable → test | Doc step 1 | Reachable defensive branches are covered, not excluded (FR-006, US2/AC1) |
| Mixed unit → don't exclude wholesale | Doc step 2 | Isolate the unreachable part; keep reachable logic measured (US2/AC3) |
| Unreachable → exclude + justify | Doc step 3 | Smallest scope + written justification (FR-003/004/005, US2/AC2) |
| Preserve defense-in-depth | Doc note | Unreachable guards kept (excluded), not deleted for the number (FR-014) |
| Revisit on reachability change | Doc note | Excluded-then-reachable → remove exclusion, add test (edge case) |

**Relationships**: Governs how new **Exclusion Records** are created and reviewed;
lives in `docs/coverage-exclusion-policy.md`, linked from the README.

---

## Entity: Coverage Model Documentation (consistency set)

The three sources that MUST agree on how coverage is measured, what is excluded,
and what target is enforced (FR-012, FR-013, SC-004, US4).

| Source | Change |
|---|---|
| `README.md` (Architecture) | Rewrite the ratchet sentence to "true 100% line/branch over reachable code with documented, justified exclusions"; link the decision-rule doc |
| `.github/workflows/coverage.yml` | Gate at 100/100; replace the ratchet footnote with the true-100% description |
| `.specify/memory/constitution.md` (Principle II) | Clarify "100% over reachable code with documented exclusions"; version bump + Sync Impact Report |

**Validation rule**: No two sources may make mutually contradictory statements
about measurement, exclusions, or the enforced target (SC-004).

---

## Non-goals (explicitly not modeled)

- **No product/runtime entity** is added or changed. Outputs, report contracts,
  finding order, and exit codes are frozen (FR-011, SC-006).
- **Infrastructure/CLI and composition roots** remain outside the gate (charter);
  not modeled here.
