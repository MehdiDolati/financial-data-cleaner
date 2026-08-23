# Quickstart: Validating the Coverage Exclusion Policy

**Feature**: `005-coverage-exclusion-policy` | **Date**: 2026-08-23

Runnable scenarios that prove the feature works end-to-end: the gate enforces a true
100/100 over reachable code, every exclusion is justified and enumerable, a reachable
regression fails the build, and the charter/CI/README tell one consistent story — all
with **no** change to product behavior. See the contracts for the authoritative rules:
[coverage-gate](./contracts/coverage-gate.md), [exclusion-record](./contracts/exclusion-record.md),
[decision-rule](./contracts/decision-rule.md).

## Prerequisites

- .NET 10 SDK (`10.0.301`+) — `dotnet --version`
- PowerShell (Windows `powershell`, or `pwsh` on Linux/macOS)
- From repo root `d:\financial-data-cleaner`: `dotnet restore FinancialDataCleaner.slnx`

## Scenario 0 — Baseline: enumerate today's gaps

Establishes the starting inventory the implementation will classify (research §6).

```powershell
powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tools\coverage-run.ps1
powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tools\coverage-gaps.ps1
```

**Expected (before implementation)**: the summary reports a sub-100% figure and lists
the defensive arms (private-constructor invariants, closed-union default arms, the
orchestrator's out-of-order gate, async state-machine internals, and the
`ToleranceResolver`/`PowerOfTen`/`ParseOhlcvField` arms).

## Scenario 1 — The gate is a true 100/100 (US1/AC1, SC-001)

After every reachable branch is tested and only unreachable arms are excluded:

```powershell
powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tools\coverage-run.ps1 -LineThreshold 100 -BranchThreshold 100
```

**Expected**: exit code `0`; merged Domain+Application line and branch coverage of the
**measured** code is `100%`. `tools\coverage-gaps.ps1` prints
"No uncovered lines or branches. Full coverage."

## Scenario 2 — No ratchet anywhere (US1/AC3, FR-002)

```powershell
Select-String -Path .github\workflows\coverage.yml, tools\coverage-run.ps1 -Pattern "99\.|97\.|ratchet"
```

**Expected**: no match — no sub-100% threshold and no "ratchet" wording remain. The
workflow invokes the gate with `-LineThreshold 100 -BranchThreshold 100`.

## Scenario 3 — A reachable regression fails the gate (US1/AC2, SC-005)

Demonstrate the gate has teeth:

1. Temporarily comment out one test that covers a **reachable** Application line.
2. Re-run:
   ```powershell
   powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tools\coverage-run.ps1 -LineThreshold 100 -BranchThreshold 100
   ```
   **Expected**: non-zero exit; the threshold failure / `coverage-gaps.ps1` names the
   now-uncovered line.
3. Restore the test; re-run → back to exit `0`.

## Scenario 4 — Every exclusion is justified and enumerable (US3, SC-002, FR-004/008)

```powershell
dotnet test tests\Validator.Application.Tests\Validator.Application.Tests.csproj --filter "FullyQualifiedName~Exclusion"
```

**Expected**: the justification reflection test passes — every
`[ExcludeFromCodeCoverage]` in Domain and Application carries a non-blank
`Justification`, and the test enumerates the full set. Removing a justification (or
adding a blank one) makes this test **fail** (US3/AC1).

Cross-check by source scan:
```powershell
Select-String -Path src\Validator.Domain\**\*.cs, src\Validator.Application\**\*.cs -Pattern "ExcludeFromCodeCoverage"
```
**Expected**: every match includes `Justification =`.

## Scenario 5 — Contributor can act from the decision rule alone (US2, SC-007)

Open `docs/coverage-exclusion-policy.md` and, for each case below, confirm the doc
leads to the stated disposition without other context:

| Case | Expected disposition |
|------|----------------------|
| A guard a legal call can reach (incl. out-of-range input) | **Test it** (US2/AC1) |
| A guard no legal call can reach | **Exclude with justification**, smallest scope (US2/AC2) |
| A unit with reachable logic + one unreachable arm | **Test/restructure**, never wholesale exclude (US2/AC3) |

## Scenario 6 — One consistent story (US4, SC-004)

Read all three and confirm none contradicts the others on how coverage is measured,
what is excluded, and the enforced target:

- `README.md` → Architecture section (true 100/100 over reachable code + link to the doc)
- `.github/workflows/coverage.yml` → header description + `-LineThreshold 100 -BranchThreshold 100`
- `.specify/memory/constitution.md` → Principle II clarified; Sync Impact Report records the version bump (research §7)

## Scenario 7 — Product behavior unchanged (FR-011, SC-006)

```powershell
dotnet test FinancialDataCleaner.slnx --configuration Release
```

**Expected**: all pre-existing product tests and contract tests pass; outputs, report
contracts, finding order, and exit codes are unchanged. This feature adds no product
behavior — only tests, exclusion annotations, a raised gate, and documentation.

## Success checklist

- [ ] Merged gate passes at `-LineThreshold 100 -BranchThreshold 100` (Scenario 1)
- [ ] No sub-100% threshold or "ratchet" wording remains (Scenario 2)
- [ ] Removing coverage from a reachable line fails the gate (Scenario 3)
- [ ] Justification reflection test passes and enumerates all exclusions (Scenario 4)
- [ ] Decision-rule doc yields the correct disposition for all three cases (Scenario 5)
- [ ] README, coverage.yml, and constitution agree (Scenario 6)
- [ ] Full solution test run is green; no product behavior changed (Scenario 7)
