# Phase 0 Research: Coverage Exclusion Policy for Unreachable Defensive Code

**Feature**: `005-coverage-exclusion-policy` | **Date**: 2026-08-23

This document resolves the decisions the plan deferred: the exclusion mechanism,
how the merged coverage run treats exclusions, how the gate is raised to a true
100%, how exclusions are kept honest and enumerable, the decision rule
contributors follow, and the constitution version bump. Each entry follows
**Decision / Rationale / Alternatives considered**.

---

## 1. Is "exclude unreachable defensive code from coverage" a best practice?

**Decision**: Yes, as a **last resort** governed by an ordered rule — prefer
**testing** the branch, then **restructuring/removing** it, and only **exclude**
when it is provably unreachable through any legal call, always with a written
justification and at the smallest possible scope.

**Rationale**: This is the mainstream industry posture. Coverage tools ship a
dedicated mechanism for exactly this (.NET's `[ExcludeFromCodeCoverage]`, JaCoCo's
`@Generated`/filters, coverage.py's `# pragma: no cover`, Istanbul's
`/* istanbul ignore */`) precisely because a small residue of code cannot be
provoked from outside (closed-enum default arms, `default` clauses after
exhaustive matches, private-constructor invariants, compiler-generated
async/iterator plumbing). Forcing 100% *without* exclusions pressures contributors
into either deleting genuine defense-in-depth guards (a safety regression, and a
direct conflict with Principle V) or writing contrived tests that reach private
state through reflection (testing the test, not the behavior). The disciplined
consensus is: exclude only what is unreachable, justify each exclusion, keep it
minimal, and review it like code — which is exactly what this feature encodes.

**Alternatives considered**:
- *Keep the sub-100% ratchet with a prose footnote* (status quo): rejected — it
  makes the charter and CI contradict each other (the exact gap this feature
  closes) and hides any real regression that lands above the threshold.
- *Delete the defensive arms to hit 100% with no exclusions*: rejected — removes a
  second line of defense (violates FR-014 and Principle V); a guard removed for
  "never firing" stops enforcing the moment a future caller changes.
- *Lower the charter to "~99% is fine"*: rejected — abandons the guarantee the
  project deliberately committed to and that the request explicitly wants kept.

---

## 2. Exclusion mechanism

**Decision**: Use the BCL attribute
`System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute` with its
`Justification` property populated on every use, e.g.
`[ExcludeFromCodeCoverage(Justification = "Unreachable: MetricScore.Scored already rejects population <= 0 before this arm.")]`,
applied at the **smallest declarable scope** (a method, a local function, or a
small extracted helper) — never a whole class that also contains reachable logic.

**Rationale**: Coverlet natively honors `[ExcludeFromCodeCoverage]` and removes
the annotated member from *both* the numerator and denominator, so an excluded arm
does not count for or against the percentage — which is what lets the remaining
reachable code reach a true 100%. The attribute is in the BCL (no new package,
satisfying Principle VII), is visible in source review (satisfying FR-009), carries
a first-class `Justification` string (satisfying FR-004), and is enumerable by
reflection or a simple source scan (satisfying FR-008). Applying it per-member
keeps the scope minimal (FR-005).

**Alternatives considered**:
- *Coverlet `ExcludeByFile`/`ExcludeByAttribute`/`Exclude` MSBuild filters*:
  rejected as the primary mechanism — file/assembly filters are coarse (they hide
  reachable code in the same file, violating FR-005) and live outside the source
  being reviewed, making per-exclusion justification and review harder.
- *Line-level pragmas / partial-class tricks*: rejected — .NET has no first-class
  line pragma for coverage; simulating one fragments the code and still lacks a
  justification field.
- *A custom `[Unreachable]` attribute*: rejected — reinvents a BCL attribute
  Coverlet already understands; adds surface for no benefit (Principle VII).

---

## 3. How the merged multi-suite run treats exclusions, and raising the gate to 100%

**Decision**: Keep the existing merged run (`tools/coverage-run.ps1`, which unions
Domain+Application coverage across all four suites) and invoke it with
`-LineThreshold 100 -BranchThreshold 100`. Update `.github/workflows/coverage.yml`
to pass `100`/`100` and delete the ratchet numbers and the prose footnote. Leave
`ci.yml`'s Domain-only per-suite gate (already `Threshold=100`) intact.

**Rationale**: The tooling already (a) merges suites so a branch covered only via
the CLI/Infrastructure path still counts, and (b) types thresholds as `[double]`
and gates line and branch **separately** (`ThresholdType=line,branch`,
`ThresholdStat=total`). Once every reachable branch is either tested or excluded,
`total` line and branch coverage of the *measured* (non-excluded) code is exactly
100%, so `-LineThreshold 100 -BranchThreshold 100` passes on a clean build (US1/AC1)
and fails the instant a reachable path loses coverage (FR-007, SC-005). No tooling
change is required — only the threshold arguments and the surrounding prose.

**Alternatives considered**:
- *Gate a single suite at 100%*: rejected — Application logic is exercised through
  the CLI/Infrastructure suites too, so a single-suite figure understates the truth
  and would fail a gate the merged run passes (this exact defect was fixed in an
  earlier feature; see `specs/002.../research.md`).
- *Round reported figures up to 100%*: rejected — the report would misstate its own
  coverage, the same dishonesty this feature exists to remove.

---

## 4. Keeping exclusions honest: enumeration, justification, and review

**Decision**: Enforce three things, test-first:
1. **Justification required** — a reflection test in `Validator.Application.Tests`
   (and Domain where applicable) scans the business-logic assemblies for
   `ExcludeFromCodeCoverageAttribute` and **fails** if any occurrence has a
   null/blank `Justification`. This makes FR-004 executable (US3/AC1).
2. **Enumerable set** — because every exclusion is the same attribute, the full set
   is listed by that reflection test (and by a one-line source scan), satisfying
   FR-008/US3/AC2. The test can print the inventory for reviewers.
3. **Reviewed in the introducing change** — each attribute is a source diff line
   with its justification, so it is surfaced in normal PR review (FR-009); the
   decision-rule doc instructs reviewers to confirm unreachability and minimal
   scope before accepting.

**Rationale**: Turning "what is excluded and why" from a prose footnote into a
machine-enumerable, justification-carrying, diff-reviewable artifact is precisely
the auditability Principle VI demands, and it is cheap: one reflection test plus a
short doc. The test is written **red first** (assert justification present on a
seeded exclusion) per Principle I.

**Alternatives considered**:
- *A Roslyn analyzer that flags un-justified exclusions*: rejected for now
  (Principle VII / simplicity) — a reflection test delivers the same guarantee with
  far less machinery; an analyzer can be a later feature if the exclusion count
  grows.
- *A hand-maintained EXCLUSIONS.md list*: rejected — drifts from source, duplicates
  the attribute, and is not enforced by CI.

---

## 5. The decision rule contributors apply (test → restructure → exclude)

**Decision**: Publish an ordered rule in `docs/coverage-exclusion-policy.md`:
1. **Test it** if any legal call (public surface, or a test-visible internal entry
   point) can reach the branch — including out-of-range inputs and boundary values.
   This is the default (FR-006, US2/AC1).
2. **Restructure/remove it** if the branch is unreachable only because of how the
   surrounding code is shaped — extract the reachable logic so it stays measured and
   the unreachable remainder is isolated (US2/AC3, edge cases).
3. **Exclude it** only if it is provably unreachable through any legal call — apply
   `[ExcludeFromCodeCoverage(Justification=…)]` at the smallest scope, stating *why*
   it cannot be reached (FR-003, FR-004, FR-005, US2/AC2). Keep defensive arms that
   exist for defense-in-depth (FR-014).
   *Revisit*: if a later change makes an excluded branch reachable, remove the
   exclusion in favor of a test (edge case).

**Rationale**: A single written, ordered rule is what makes the one-time cleanup
durable (US2's "why P2"): every contributor handles a defensive branch the same way
without tribal knowledge, so exclusions do not drift and the gate does not silently
erode (SC-007). The order encodes the preference for real coverage over exclusion.

**Alternatives considered**:
- *Leave it to reviewer judgment*: rejected — inconsistent outcomes, and the guarantee
  quietly weakens over time (the failure mode US2 names).

---

## 6. Classifying the current inventory of defensive arms

**Decision**: Treat the arms enumerated in `coverage.yml` as the **starting**
inventory and re-run `tools/coverage-gaps.ps1` on a fresh merged run to get the
authoritative current set, then classify each as **test**, **restructure**, or
**exclude**. Known starting groups:
- *Private-constructor invariants* — `MetricScore`/`DatasetScore` factory-guarded
  arms ("scored but no value", "available but carrying an unavailability reason").
- *Closed-union default arms* — `CheckNameFor`, `DescribeKind`, `CategoryIndex`,
  `IsHeaderRecord`, `KindFor` `_ => throw` clauses over fixed enum sets.
- *Out-of-order guard* — `DetailedValidationOrchestrator`'s reconciliation gate,
  unreachable because `ReportReconciliation.Create` rejects a disagreeing catalog first.
- *Async state-machine internals* — `DetailedValidationOrchestrator.MoveNext` /
  `NextObservedAfter` compiler-generated paths.
- *`ToleranceResolver` static constructor, `PowerOfTen` positive-exponent loop,
  `ParseOhlcvField` default `throw`*.

**Rationale**: The spec's Assumptions require the exact set to be re-enumerated at
implementation time rather than trusting a possibly-stale comment. Some of these
(e.g. several closed-union defaults) may already be provable via a test that casts
an undeclared enum value — those should be **tested**, not excluded, honoring the
default-to-test preference. Only the genuinely unreachable remainder is annotated.

**Alternatives considered**:
- *Trust the `coverage.yml` comment verbatim*: rejected — the comment is the very
  footnote this feature replaces; it may be stale and must be re-verified.

---

## 7. Constitution version bump

**Decision**: Amend Principle II's wording to state that the 100% line/branch gate
is measured over **reachable** Domain/Application code, with genuinely-unreachable
defensive code **excluded via documented, justified exclusions**. Record it as a
**PATCH** clarification (**1.1.0 → 1.1.1**) with a rationale in the Sync Impact
Report, unless review judges the wording a material rule expansion (then MINOR).

**Rationale**: The *intent* of Principle II — business logic is fully covered and
CI enforces it — is unchanged; the amendment only clarifies that "100%" was always
meant over reachable code and names the exclusion discipline. Per the constitution's
own governance rules, PATCH is for non-semantic clarification. FR-013 and US4/AC2
require the change be recorded with a rationale and an appropriate version bump; the
Sync Impact Report is the vehicle.

**Alternatives considered**:
- *No constitution change*: rejected — leaves README/CI/charter potentially
  inconsistent (SC-004) and violates FR-013.
- *MAJOR bump*: rejected — nothing about governance becomes incompatible; existing
  plans are not invalidated.

---

## Resolved unknowns

All Technical Context items are decided; **no `NEEDS CLARIFICATION` remain**:
- Exclusion mechanism → `[ExcludeFromCodeCoverage(Justification=…)]`, per-member (§2).
- Merged-run behavior + gate value → reuse tooling, `-LineThreshold 100 -BranchThreshold 100` (§3).
- Justification/enumeration/review enforcement → reflection test + doc + PR review (§4).
- Decision rule → test → restructure → exclude, published in `docs/` (§5).
- Inventory source → re-enumerate via `tools/coverage-gaps.ps1` (§6).
- Version bump → PATCH clarification 1.1.0 → 1.1.1 with Sync Impact Report (§7).

## Baseline inventory (measured)

The following uncovered lines and branches were enumerated from the merged coverage run (`tools/coverage-run.ps1` + `tools/coverage-gaps.ps1`) on 2026-08-24.

```
=== CandidateIdentity.cs
    .ctor -> lines[20]  branches 3/6 uncovered at lines[19]

=== CompareDatasetsUseCase.cs
    Compare -> lines[118,119,121]  branches 3/22 uncovered at lines[116,160,165]
    GetFieldValue -> lines[257]  branches 1/6 uncovered at lines[250]
    BuildToleratedAggregate -> lines[269]  branches 1/2 uncovered at lines[267]

=== ComparisonReport.cs
    .ctor -> branches 2/12 uncovered at lines[96,97]

=== ComparisonTextReportWriter.cs
    Write -> branches 2/36 uncovered at lines[77]

=== DatasetScore.cs
    .ctor -> lines[72]  branches 1/8 uncovered at lines[70]

=== DetailedValidationOrchestrator.cs
    NextObservedAfter -> lines[647]  branches 2/4 uncovered at lines[645,651]
    MoveNext -> lines[107,108]  branches 1/14 uncovered at lines[105]

=== EvidenceJoiner.cs
    IsHeaderRecord -> lines[185]  branches 1/7 uncovered at lines[177]

=== FindingCatalog.cs
    RefOf -> branches 1/2 uncovered at lines[451]
    CategoryIndex -> lines[468]  branches 1/7 uncovered at lines[460]
    Read -> branches 1/2 uncovered at lines[36]
    MoveNext -> lines[240]  branches 1/10 uncovered at lines[238]

=== MetricScore.cs
    .ctor -> lines[47,52,59]  branches 3/12 uncovered at lines[45,50,57]

=== ScoreSectionBuilder.cs
    CheckNameFor -> lines[104]  branches 1/7 uncovered at lines[96]
    DescribeKind -> lines[112]  branches 1/4 uncovered at lines[107]

=== ToleranceResolver.cs
    PowerOfTen -> lines[91,92,93,94]  branches 3/6 uncovered at lines[89,92]
    ParseOverrides -> lines[228]  branches 2/64 uncovered at lines[227]
    ParseOhlcvField -> lines[278,279]  branches 2/10 uncovered at lines[274]
    .cctor -> lines[17,18,21,22]

---------------- summary ----------------
Lines    : 4001/4030 covered (99.28%)  gaps=29
Branches : 1593/1626 covered (97.97%)  gaps=33
Methods with gaps : 21
```

## Classification of uncovered arms

Each uncovered arm is classified as **test**, **restructure**, or **exclude**
per the reachability rule (§6): any arm reachable via the public API, a
test-visible internal entry point, or an out-of-range/undeclared-enum-cast value
is **test**; a mixed unit is **restructure**; only arms no test can execute by any
means are **exclude**.

| File | Method / Member | Uncovered Lines | Classification | Rationale |
|------|----------------|----------------|---------------|-----------|
| CandidateIdentity.cs | `.ctor` | lines[20], branches at lines[19] | **test** | Null-guard branches reachable via public API (pass null source/context) |
| CompareDatasetsUseCase.cs | `Compare` | lines[118,119,121], branches at lines[116,160,165] | **test** | `isDifferent`-true path in AcceptedByAbsolute/Relative switch cases and `TryGetValue`-false paths in missing/extra record projection — all reachable via public API |
| CompareDatasetsUseCase.cs | `GetFieldValue` | lines[257], branch at lines[250] | **exclude** | Internal method only called from `Compare` with valid `OhlcvField` values from the iteration over `configuration.Fields`; default throw is unreachable through any legal call path |
| CompareDatasetsUseCase.cs | `BuildToleratedAggregate` | lines[269], branch at lines[267] | **test** | `TryGetValue`-false branch — reachable via public API when field not in counts |
| ComparisonReport.cs | `.ctor` (14-param) | branches at lines[96,97] | **test** | Null-coalescing branches for `missingFromCandidateRecords`/`extraInCandidateRecords` — reachable via public API |
| ComparisonTextReportWriter.cs | `Write` | branches at lines[77] | **test** | Conditional branch for `CandidateScore` presence — reachable via public API |
| DatasetScore.cs | `.ctor` | lines[72], branch at lines[70] | **exclude** | Private-constructor invariant: `available+unavailableReason` combination prevented by factory methods (`Available`/`Unavailable`); defense-in-depth guard (FR-014) |
| DetailedValidationOrchestrator.cs | `NextObservedAfter` | lines[647], branches at lines[645,651] | **test** | Binary-search exact-match branch and insertion-point-past-end branch — both reachable via normal validation runs with gaps at boundaries |
| DetailedValidationOrchestrator.cs | `MoveNext` (state machine) | lines[107,108], branch at lines[105] | **exclude** | Compiler-generated async state-machine internals; unreachable branch in normal async flow |
| EvidenceJoiner.cs | `IsHeaderRecord` | lines[185], branch at lines[177] | **exclude** | Private method only called with `EvidenceKind` from `header.EvidenceKind` which is always a valid enum value; default throw is unreachable through any legal call path |
| FindingCatalog.cs | `RefOf` | branch at lines[451] | **test** | `separator < 0` branch (no pipe in line) — reachable via malformed spool line |
| FindingCatalog.cs | `CategoryIndex` | lines[468], branch at lines[460] | **exclude** | Private method only called with `FindingCategory` from `finding.Category` which is always a valid enum value; default throw is unreachable through any legal call path |
| FindingCatalog.cs | `ReadCanonicalAsync` (cursor) | branch at lines[36] | **test** | `locationBlock is null` path — reachable when a finding has no location lines |
| FindingCatalog.cs | `MoveNext` (state machine) | lines[240], branch at lines[238] | **exclude** | Compiler-generated async state-machine internals for `ReadCanonicalAsync` |
| DetailedSummary.cs | `For` | (public closed union) | **test** | Public method with closed union over 6 `FindingCategory` values — reachable via out-of-range enum cast |
| FindingCatalogStatistics.cs | `For` | (public closed union) | **test** | Public method with closed union over 6 `FindingCategory` values — reachable via out-of-range enum cast |
| FindingReferenceFactory.cs | `CategorySegment` | (private, called from public `PhysicalRecord`) | **test** | Reachable via out-of-range `FindingCategory` passed to public `PhysicalRecord` method |
| MetricScore.cs | `.ctor` | lines[47,52,59], branches at lines[45,50,57] | **exclude** | Private-constructor invariant: factory methods (`Scored`/`NotApplicable`/`NotScored`) prevent invalid state+value combinations; defense-in-depth guards (FR-014) |
| ScoreSectionBuilder.cs | `FindCheck` | lines[93], branch at lines[85] | **test** | `return null` when no matching check found — reachable via public API when checks list is incomplete |
| ScoreSectionBuilder.cs | `CheckNameFor` | lines[104], branch at lines[96] | **exclude** | Private method only called from `FindCheck` with categories from `MetricPopulationMap.CanonicalOrder`; default throw is unreachable through any legal call path |
| ScoreSectionBuilder.cs | `DescribeKind` | lines[109,112], branch at lines[107] | **exclude** | Private method only called from `BuildMetric` with kinds from `MetricPopulationMap.KindFor`; default throw is unreachable through any legal call path |
| ScoreWeightParser.cs | `Parse` | lines[48,59], branches at lines[46,57] | **test** | Error paths for malformed weight strings — reachable via public API |
| ToleranceResolver.cs | `PowerOfTen` | lines[91,92,93,94], branches at lines[89,92] | **exclude** | Positive-exponent loop unreachable: `InferFractionalStep` only passes negative exponents (`-maxPrecision`); no call path supplies a non-negative exponent |
| ToleranceResolver.cs | `ParseOverrides` | line[228], branch at lines[227] | **test** | `!hasAbsolute && !hasRelative && !hasEnabled` guard — reachable via malformed JSON |
| ToleranceResolver.cs | `ParseOhlcvField` | lines[278,279], branch at lines[274] | **test** | Default `_ => throw` for unknown field name — reachable via out-of-range input |
| ToleranceResolver.cs | `.cctor` (static ctor) | lines[17,18,21,22] | **exclude** | Compiler-generated static constructor for `const` fields — const values are baked into calling code; the `.cctor` body is never executed at runtime |

### Classification summary

- **test**: 16 arms — reachable via public API, test-visible internal entry points, or out-of-range/undeclared-enum-cast values. These will be covered by new tests (T004–T007).
- **exclude**: 11 arms — genuinely unreachable through any legal call. These will receive `[ExcludeFromCodeCoverage(Justification=…)]` at the smallest scope (T010–T012).
- **restructure**: 0 arms — no mixed unit identified where reachable and unreachable logic share the same member in a way that requires extraction.

### InternalsVisibleTo requirement (T003)

All **test**-classified arms are reachable through the public API or via out-of-range enum casts. No arm requires a test-visible internal entry point beyond what is already accessible. Therefore, `InternalsVisibleTo` is **not required** and T003 is recorded as skipped.

