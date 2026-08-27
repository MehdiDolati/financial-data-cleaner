# Contract: Coverage Gate

**Feature**: `005-coverage-exclusion-policy`

The CI-enforced contract for Domain and Application coverage. This is the
project's "interface" to contributors and to the CI system: it defines what the
gate promises, what makes it pass, and what makes it fail.

## Interface

Realized by `.github/workflows/coverage.yml` invoking the existing
`tools/coverage-run.ps1` over the merged Domain+Application coverage of all four
test suites.

```
./tools/coverage-run.ps1 -LineThreshold 100 -BranchThreshold 100
```

## Guarantees

| ID | Guarantee | Requirement |
|----|-----------|-------------|
| G1 | Enforces **100% line** coverage over reachable Domain+Application code. | FR-001, SC-001 |
| G2 | Enforces **100% branch** coverage over reachable Domain+Application code, gated separately from line. | FR-001, SC-001 |
| G3 | Contains **no threshold below 100%** anywhere in configuration (no ratchet). | FR-002, US1/AC3 |
| G4 | Measures over the merged multi-suite run, so a branch reached only via the CLI/Infrastructure path still counts. | Assumptions |
| G5 | Excludes only members carrying `[ExcludeFromCodeCoverage]`; excluded members leave both numerator and denominator. | FR-003 |
| G6 | On a clean build with all reachable code covered, the gate **passes**. | US1/AC1 |
| G7 | When any reachable line or branch loses coverage, the gate **fails** and the run names the uncovered location. | FR-007, US1/AC2, SC-005 |

## Pass condition

```
line_total(measured)   == 100.00%   AND
branch_total(measured) == 100.00%
```
where `measured = reachable Domain+Application code = all code − Exclusion Set`.

## Fail conditions

- Any reachable line uncovered → non-zero exit; location surfaced by
  `tools/coverage-gaps.ps1` / Coverlet threshold output (G7).
- Any reachable branch uncovered → non-zero exit (G7).
- (Config-review check) A threshold argument or property below 100 present → the
  gate is non-compliant with G3 even if it passes.

## Non-goals

- Does not gate Infrastructure, CLI, or composition roots (charter; those are
  covered by integration/E2E tests).
- Does not change any product output, contract, finding order, or exit code
  (FR-011): this is a build-time gate only.

## Verification

- **US1/AC1** — clean build: `./tools/coverage-run.ps1 -LineThreshold 100 -BranchThreshold 100` exits 0.
- **US1/AC2, SC-005** — regression check: delete a test covering one reachable line;
  re-run; the gate exits non-zero and names that line. Restore the test.
- **US1/AC3, FR-002** — config inspection: `grep` the workflow and tooling for any
  threshold `< 100`; none exists.
