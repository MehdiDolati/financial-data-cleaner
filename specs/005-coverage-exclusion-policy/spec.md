# Feature Specification: Coverage Exclusion Policy for Unreachable Defensive Code

**Feature Branch**: `005-coverage-exclusion-policy`

**Created**: 2026-08-23

**Status**: Draft

**Input**: User description: "I'm thinking of a refactor. I don't want to decrease the 100% test coverage we decided at the beginning. What I want is to exclude any exceptions like defensive arms from the test. Is this a best practice, and what do other people do in such situations?"

## Context *(informational)*

The project charter (Constitution Principle II) commits Domain- and
Application-layer business logic to **100% line and branch coverage, enforced in
CI**. In practice the Domain layer meets this, but the Application layer sits
below it and is held together by a CI *ratchet* threshold set just under the
measured figure, with a note listing "defensive arms" that no valid composition
can reach. The result is a gap between what the charter promises (a true 100%)
and what CI enforces (a sub-100% ratchet with a prose footnote). This feature
closes that gap by defining and applying a policy for how unreachable defensive
code is handled, so the enforced gate and the charter tell the same, honest
story.

This is an internal quality-and-governance change. It changes how coverage is
measured and enforced and how contributors decide what to do when they encounter
a defensive branch. It does **not** change any product behavior, output,
contract, or exit code.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An honest, enforceable 100% gate (Priority: P1)

As a maintainer (and the CI system acting on the team's behalf), I want the
coverage gate to enforce a true 100% over all *reachable* Domain and Application
code, so that the "100% we committed to" is honest and any genuine loss of
coverage fails the build immediately instead of hiding beneath a ratchet.

**Why this priority**: This is the core outcome the request is really asking for.
Without it, the charter and the enforced gate keep contradicting each other and
the guarantee stays soft. Everything else supports this.

**Independent Test**: Can be fully tested by confirming the enforced gate is set
to 100% line and 100% branch over reachable code, and by verifying that removing
coverage from any reachable line causes the gate to fail. Delivers a trustworthy
guarantee on its own.

**Acceptance Scenarios**:

1. **Given** the policy has been applied, **When** the coverage gate runs on a
   clean build, **Then** it enforces 100% line and 100% branch over reachable
   Domain and Application code and passes.
2. **Given** the policy has been applied, **When** a reachable line or branch
   loses its test coverage, **Then** the gate fails and names the uncovered
   location.
3. **Given** the policy has been applied, **When** the gate configuration is
   inspected, **Then** it no longer depends on a sub-100% ratchet threshold to
   pass.

---

### User Story 2 - A clear rule for defensive branches (Priority: P2)

As a contributor who has just written or encountered a defensive branch (a guard
or `throw` that protects an invariant), I want a documented decision rule that
tells me whether to test it, restructure it, or exclude it, so that I handle it
the same way everyone else does and never weaken the guarantee by guessing.

**Why this priority**: The one-time cleanup is only durable if the team keeps
applying the same rule afterwards. Without a written rule, exclusions drift and
the gate silently erodes over time.

**Independent Test**: Can be tested by giving a contributor a defensive branch
and confirming they can determine the correct action (test / restructure /
exclude) from the documentation alone, without tribal knowledge.

**Acceptance Scenarios**:

1. **Given** a defensive branch that a legal call can reach, **When** the
   contributor consults the rule, **Then** the rule directs them to cover it with
   a test rather than exclude it.
2. **Given** a defensive branch that no legal call can reach, **When** the
   contributor consults the rule, **Then** the rule directs them to exclude it
   with a written justification (or to remove/restructure it) and to keep the
   exclusion at the smallest scope that isolates only the unreachable code.
3. **Given** a unit that contains both reachable logic and one unreachable
   branch, **When** the contributor consults the rule, **Then** the rule forbids
   excluding the whole unit and directs them to test or restructure instead.

---

### User Story 3 - Auditable exclusions reviewed like code (Priority: P2)

As a reviewer, I want every coverage exclusion to be individually visible,
justified, and reviewable in the change that introduces it, so that I can confirm
each one is genuinely unreachable and minimally scoped before it is accepted.

**Why this priority**: Exclusions are the one place where the guarantee can be
quietly hollowed out. Making each exclusion an explicit, justified, reviewable
artifact is what keeps the 100% meaningful rather than cosmetic.

**Independent Test**: Can be tested by enumerating all exclusions and confirming
each carries a justification and was surfaced for review, and that an exclusion
without a justification is rejected.

**Acceptance Scenarios**:

1. **Given** an exclusion is proposed, **When** it lacks a written justification,
   **Then** it is not acceptable and is flagged for correction.
2. **Given** the set of exclusions, **When** a reviewer asks "what is excluded and
   why," **Then** the answer is enumerable and each entry states why the code is
   unreachable.

---

### User Story 4 - One consistent story across charter, CI, and docs (Priority: P3)

As a maintainer, I want the constitution, the CI configuration, and the README to
describe the same coverage model, so that a newcomer reading any one of them
reaches the same understanding and there is no contradiction to reconcile later.

**Why this priority**: Consistency prevents future confusion and re-litigation,
but it depends on the substantive outcomes (P1–P2) being decided first.

**Independent Test**: Can be tested by reading all three sources and confirming
none contradicts the others on how coverage is measured, what is excluded, and
what the enforced target is.

**Acceptance Scenarios**:

1. **Given** the change is complete, **When** the README architecture note, the
   CI workflow description, and the constitution are read together, **Then** they
   agree that the gate is a true 100% over reachable code with documented
   exclusions.
2. **Given** the charter needs a wording clarification to match the enforced
   model, **When** the change is made, **Then** the clarification is recorded with
   a rationale and an appropriate version bump.

### Edge Cases

- A branch believed to be unreachable is actually reachable through an
  out-of-range value or a test-visible internal entry point → it MUST be tested,
  not excluded.
- A single unit contains both reachable logic and one unreachable defensive
  branch → the unit MUST NOT be excluded wholesale; the unreachable part is
  isolated (tested or restructured) so reachable logic stays measured.
- A branch cannot be exercised because tooling cannot reach compiler-generated or
  implicit paths → the reachable logic is separated and covered, and only the
  genuinely unreachable remainder is excluded.
- A later change makes a previously-excluded branch reachable again → the
  exclusion MUST be revisited and removed in favor of a test.
- An exclusion is proposed with no justification, or scoped too broadly so it
  hides reachable code → it MUST be rejected.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The enforced coverage gate for Domain and Application code MUST
  require 100% line and 100% branch coverage over code that is reachable through
  a legal call (public surface or test-visible internal entry point).
- **FR-002**: The gate MUST NOT depend on a coverage threshold below 100% (no
  ratchet) once the policy is applied.
- **FR-003**: Any code excluded from coverage MUST be provably unreachable
  through any legal call.
- **FR-004**: Every exclusion MUST carry a human-readable justification that
  states why the code is unreachable.
- **FR-005**: Exclusions MUST be applied at the smallest scope that isolates only
  the unreachable code; a unit that also contains reachable logic MUST NOT be
  excluded as a whole.
- **FR-006**: Reachable defensive branches MUST be covered by tests rather than
  excluded (default-to-test).
- **FR-007**: A newly-introduced uncovered *reachable* line or branch MUST cause
  the gate to fail.
- **FR-008**: The complete set of exclusions MUST be enumerable for review.
- **FR-009**: Each exclusion MUST be surfaced for human review in the change that
  introduces it.
- **FR-010**: The team MUST have a documented decision rule that, for any
  defensive branch, yields one of: test it, restructure/remove it, or exclude it
  with justification — in that order of preference.
- **FR-011**: Product behavior MUST remain unchanged: existing outputs, report
  contracts, finding order, and exit codes are unaffected by this change.
- **FR-012**: Documentation that describes the coverage model (at minimum the
  README architecture note and the CI workflow description) MUST be updated in the
  same change to reflect the true-100%-with-documented-exclusions model.
- **FR-013**: The governing charter MUST be consistent with the enforced gate; if
  a clarification is required to state that 100% is measured over reachable code
  with documented exclusions, it MUST be recorded with a rationale and a version
  bump.
- **FR-014**: Unreachable defensive branches that are retained for defense-in-depth
  MUST be preserved (excluded), not deleted solely to reach the number, unless
  removal is independently justified as a simplification.

### Key Concepts

- **Reachable code**: Code that some legal call can execute — through the public
  API, or through an internal entry point that tests are permitted to use.
- **Unreachable defensive branch**: A guard or error path that protects an
  invariant which no legal call can violate, so it cannot be executed from
  outside; kept as a second line of defense.
- **Exclusion record**: A specific unit of unreachable code marked as excluded
  from the coverage measurement, paired with a justification explaining why it is
  unreachable.
- **Coverage gate**: The CI check that enforces the coverage target over the
  measured (reachable) code and fails the build when it is not met.
- **Decision rule**: The documented, ordered guidance (test → restructure →
  exclude) contributors apply to any defensive branch.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The enforced coverage target for Domain and Application is 100% line
  and 100% branch, with no threshold set below 100%.
- **SC-002**: 100% of coverage exclusions carry a written justification.
- **SC-003**: Zero reachable code paths are excluded (every exclusion is verified
  unreachable).
- **SC-004**: The README, the CI configuration, and the constitution make no
  mutually contradictory statement about how coverage is measured, what is
  excluded, or what target is enforced.
- **SC-005**: Removing coverage from any single reachable line causes the gate to
  fail (demonstrable regression check).
- **SC-006**: All previously-passing product tests and contracts continue to pass
  with no change to expected outputs or exit codes.
- **SC-007**: A contributor can determine the correct disposition (test /
  restructure / exclude) for a given defensive branch using only the documented
  decision rule.

## Assumptions

- The current CI ratchet threshold (set just under the measured figure) is a
  temporary stand-in for the charter's true-100% intent and is to be replaced by
  a true 100% over reachable code.
- The existing coverage measurement mechanism (the merged multi-suite coverage
  run and the gap-enumeration tooling) remains in place; this feature changes
  *what is measured* and *the enforced threshold*, not the choice of tooling.
- The defensive arms currently enumerated in the CI workflow description are the
  starting inventory; the exact set is re-enumerated during implementation and
  each item is classified as testable, restructurable, or excludable.
- Composition roots and thin adapter/wiring code remain exempt from the coverage
  gate per the charter and are out of scope here; this feature concerns Domain and
  Application only.
- Restoring a true 100% is expected to require adding tests for reachable branches
  that are currently uncovered, isolating unreachable logic, and marking only the
  genuinely unreachable remainder as excluded.
- No product/user-facing behavior changes are in scope; exit codes, report
  contracts, finding order, and outputs are frozen for this change.
- Aligning the constitution is expected to be a clarification (a small version
  bump), not a change to the intent of Principle II.

## Dependencies

- Relies on the existing coverage run and gap-enumeration tooling and the CI
  coverage workflow.
- Touches the project constitution (governance), so it requires a documented
  amendment/clarification with a version bump.
- Interacts with Principle VIII (documentation ships with the feature): README and
  CI documentation updates are part of this change's definition of done.
