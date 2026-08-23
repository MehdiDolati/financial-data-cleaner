# Contract: Decision Rule

**Feature**: `005-coverage-exclusion-policy`

The documented, ordered guidance every contributor applies to any defensive branch
(a guard or `throw` protecting an invariant). It is the "interface" that guarantees
everyone handles defensive code the same way. The authoritative copy lives at
`docs/coverage-exclusion-policy.md`; this contract defines what that doc MUST say
and how it is verified.

## The rule (ordered — earlier steps are preferred)

1. **Test it** — *default.*
   If any legal call can reach the branch — through the public surface, or through a
   test-visible internal entry point, including out-of-range inputs and boundary
   values — write a test that exercises it. Do **not** exclude it. (FR-006, US2/AC1)

2. **Restructure / remove it.**
   If the branch is unreachable only because of how the surrounding code is shaped,
   extract the reachable logic so it stays measured and the unreachable remainder is
   isolated; or remove the branch if it is genuinely dead and its removal is an
   independently justified simplification. A unit that mixes reachable logic with one
   unreachable arm MUST NOT be excluded wholesale. (US2/AC3, edge cases)

3. **Exclude it — last resort.**
   Only if the branch is **provably unreachable through any legal call**, annotate the
   smallest member that isolates it with
   `[ExcludeFromCodeCoverage(Justification = "…")]`, stating *why* it cannot be
   reached. Keep defensive arms retained for defense-in-depth (do not delete them just
   to reach the number). (FR-003, FR-004, FR-005, FR-014, US2/AC2)

**Revisit**: if a later change makes a previously-excluded branch reachable, remove
the exclusion and cover it with a test (step 1). (Edge cases)

## Guarantees

| ID | Guarantee | Source |
|----|-----------|--------|
| D1 | The rule yields exactly one disposition — **test**, **restructure/remove**, or **exclude** — for any defensive branch, in that order of preference. | FR-010 |
| D2 | A reachable branch is directed to **test**, never exclude. | FR-006, US2/AC1 |
| D3 | An unreachable branch is directed to **exclude with justification** (or remove/restructure), at the smallest isolating scope. | FR-005, US2/AC2 |
| D4 | A mixed unit is directed to **test or restructure**, never wholesale exclude. | US2/AC3 |
| D5 | A contributor can reach the correct disposition **from the doc alone**, without tribal knowledge. | SC-007, US2 Independent Test |

## Verification

- **US2/AC1 (D2)** — reachable example: following the doc leads to "test it".
- **US2/AC2 (D3)** — unreachable example: following the doc leads to "exclude with a
  written justification at the smallest scope" (or remove/restructure).
- **US2/AC3 (D4)** — mixed-unit example: following the doc forbids excluding the whole
  unit and leads to test/restructure.
- **SC-007 (D5)** — a contributor given a defensive branch and only the doc selects the
  correct disposition.

## Consistency (with the other two sources)

The doc MUST NOT contradict the [coverage-gate](./coverage-gate.md) contract or the
constitution: all three state a true 100% over reachable code with documented,
justified exclusions (FR-012, FR-013, SC-004). The README links to this doc.
