# Coverage Exclusion Policy

**Purpose**: Provide a clear, ordered rule for handling defensive branches in the codebase so every contributor handles them consistently.

## The Decision Rule

When you encounter a defensive branch (a guard, `throw`, or invariant check), apply the following steps **in order**. Earlier steps are preferred:

### 1. Test It (Default)

**If any legal call can reach the branch**, write a test that exercises it.

This includes:
- The public API surface (public methods, constructors, properties)
- Test-visible internal entry points (via `InternalsVisibleTo`)
- Out-of-range inputs and boundary values
- Out-of-range enum casts (e.g., `(SomeEnum)99`)

**Do not exclude it.** A reachable branch must be covered by a test.

### 2. Restructure / Remove It

If the branch is **unreachable only because of how the surrounding code is shaped**, restructure to isolate the unreachable part:

- Extract the reachable logic so it stays measured and tested
- Isolate the unreachable remainder into its own smallest member
- Remove the branch if it is genuinely dead and its removal is an independently justified simplification

**A unit that mixes reachable logic with one unreachable arm MUST NOT be excluded wholesale.** The reachable part must remain measured.

### 3. Exclude It (Last Resort)

Only if the branch is **provably unreachable through any legal call**, annotate the smallest scope that isolates it:

```csharp
[ExcludeFromCodeCoverage(Justification = "Unreachable: <explain why it cannot be reached>.")]
private static …
```

The justification MUST:
- State exactly which earlier guard, factory, closed set, or invariant makes the arm impossible to reach
- Be non-blank and specific (not generic like "unreachable defensive code")
- Be enforced by the exclusion justification reflection test (build fails on blank justification)

**Keep defensive arms retained for defense-in-depth** — do not delete them just to reach a coverage number.

## Revisit

If a later change makes a previously-excluded branch reachable:
1. Remove the `[ExcludeFromCodeCoverage]` attribute
2. Cover the branch with a test (step 1)

## Examples

| Scenario | Disposition | Reason |
|----------|-------------|--------|
| A guard in a public constructor that validates input | **Test** | Reachable via public API |
| A default throw in a closed-union switch over a public enum | **Test** | Reachable via out-of-range enum cast |
| A private-constructor invariant that factory methods prevent | **Exclude** | Factory methods ensure the arm is unreachable; defense-in-depth |
| A compiler-generated async state-machine branch | **Exclude** | Cannot be reached through any legal call path |
| A unit mixing reachable logic with one unreachable arm | **Restructure** | Extract the unreachable part; keep the reachable part measured |

## Related Documents

- [Coverage Gate Contract](../specs/005-coverage-exclusion-policy/contracts/coverage-gate.md)
- [Exclusion Record Contract](../specs/005-coverage-exclusion-policy/contracts/exclusion-record.md)
- [Decision Rule Contract](../specs/005-coverage-exclusion-policy/contracts/decision-rule.md)
