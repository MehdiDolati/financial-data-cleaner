# Contract: Exclusion Record

**Feature**: `005-coverage-exclusion-policy`

The contract every coverage exclusion MUST satisfy to be accepted. This is the
"interface" a contributor implements when they exclude a branch and the checklist a
reviewer applies. Each exclusion is a source annotation, not a runtime type.

## Form

```csharp
using System.Diagnostics.CodeAnalysis;

[ExcludeFromCodeCoverage(Justification =
    "Unreachable: <state exactly which earlier guard/factory/closed set makes this arm impossible to reach through any legal call>.")]
private static … // the smallest member that isolates only the unreachable code
```

## Requirements

| ID | Requirement | Source |
|----|-------------|--------|
| E1 | The target MUST be **provably unreachable** through any legal call (public surface or test-visible internal entry point). | FR-003, SC-003 |
| E2 | The annotation MUST carry a **non-blank `Justification`** that explains *why* the code is unreachable. | FR-004, US3/AC1, SC-002 |
| E3 | The annotation MUST be at the **smallest scope** that isolates only unreachable code; a unit that also contains reachable logic MUST NOT be excluded as a whole. | FR-005, US2/AC3 |
| E4 | A **reachable** defensive branch MUST NOT be excluded — it MUST be tested instead. | FR-006, US2/AC1 |
| E5 | An unreachable guard kept for defense-in-depth MUST be **preserved** (excluded), not deleted merely to reach the number. | FR-014 |
| E6 | The exclusion MUST be **surfaced for review** in the change that introduces it (it is a visible source diff). | FR-009 |
| E7 | The full set of exclusions MUST be **enumerable**. | FR-008, US3/AC2 |
| E8 | If a later change makes the target reachable, the exclusion MUST be **removed** in favor of a test. | Edge cases |

## Acceptance / rejection

- **Accept** when E1–E7 hold and a reviewer has confirmed unreachability and minimal scope.
- **Reject** (US3/AC1) when the `Justification` is missing or blank — the
  justification reflection test fails the build.
- **Reject** when the scope is broader than the unreachable code (E3) or the target
  is actually reachable (E1/E4) — surfaced in review and by the gate still failing
  on the reachable path.

## Enumeration & justification enforcement

A reflection test in the business-logic test suites (written red-first):
1. Loads the `Validator.Domain` and `Validator.Application` assemblies.
2. Finds every member/type carrying `ExcludeFromCodeCoverageAttribute`.
3. **Asserts** each has a non-null, non-whitespace `Justification` (E2).
4. Optionally emits the full list as the enumerable inventory (E7).

Pseudocode of the assertion (illustrative, not the implementation):
```
foreach member with [ExcludeFromCodeCoverage] in {Domain, Application}:
    assert !string.IsNullOrWhiteSpace(attr.Justification)
```

## Verification

- **US3/AC1, SC-002** — add a temporary exclusion with a blank justification; the
  reflection test fails. Remove it.
- **US3/AC2** — run the reflection/inventory test; it lists every exclusion with its
  justification.
- **E1/E4, SC-003** — for each excluded arm, confirm (in review) the earlier
  guard/factory/closed set that makes it unreachable, matching the justification text.
