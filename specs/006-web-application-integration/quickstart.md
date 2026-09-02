# Quickstart: Validating the Web Application Integration

**Feature**: 006-web-application-integration | **Plan**: [plan.md](./plan.md)

Runnable validation scenarios that prove the integration works end to end. Each
scenario is written **test-first**: the assertion exists and fails before the
implementation that satisfies it (Principle I, FR-027).

Details are not duplicated here — see [data-model.md](./data-model.md) for entity
rules, [contracts/web-integration-contract.md](./contracts/web-integration-contract.md)
for the façade, [contracts/web-run-lifecycle.md](./contracts/web-run-lifecycle.md)
for states, and
[contracts/web-result-view-contract.md](./contracts/web-result-view-contract.md)
for the comparison surface.

## Prerequisites

- .NET 10 SDK
- Existing fixtures under `tests/Fixtures/` and `tests/Validator.Cli.Tests/Fixtures/`
- Existing AUDUSD benchmark material under `benchmarks/audusd-daily/`
- A built CLI for the parity baseline

```powershell
dotnet restore FinancialDataCleaner.slnx
dotnet build FinancialDataCleaner.slnx --configuration Release --no-restore
```

**Blocked until research R1 is answered**: any scenario requiring a running
website (browser, keyboard, responsive layout). Scenarios 1–8 below are
host-independent and runnable as soon as the boundary exists.

## Running the Suites

```powershell
# Full solution, including the new boundary and parity suites
dotnet test FinancialDataCleaner.slnx --configuration Release --no-build

# Boundary only
dotnet test tests/Validator.Application.Tests --configuration Release --no-build --filter "FullyQualifiedName~Web"

# CLI↔web parity only
dotnet test tests/Validator.Parity.Tests --configuration Release --no-build

# Coverage gate (Domain + Application must remain 100% over reachable code)
./tools/coverage-run.ps1
```

---

## Scenario 1 — Validation parity on a clean dataset

**Proves**: FR-001, FR-002, SC-001 (clean half), US1 scenarios 1 and 3

**Steps**
1. Run a clean fixture through the CLI and capture its v2 JSON export.
2. Submit the same bytes through `IValidationWebService.SubmitAsync` with
   equivalent resolved options.
3. Poll `GetStatusAsync` until terminal; call `GetResultAsync`.

**Expected**
- Status is `CompletedClean`.
- All six category counts are `0` and exposed separately.
- The result is identified as clean and nothing indicates the dataset was modified.
- The web view and the CLI export agree on every item in the substantive
  comparison surface.

---

## Scenario 2 — Validation parity across every finding category

**Proves**: FR-002, FR-013, SC-001, US1 scenario 2, US2 scenarios 1–3

**Steps**
1. Use a fixture containing missing candles, duplicate records, invalid OHLC,
   closed-market records, time gaps, and malformed rows.
2. Run it through both front ends with equivalent options.
3. Enumerate the web view's streamed findings and the CLI's v2 findings in order.

**Expected**
- Status is `CompletedWithFindings`.
- All six categories are reported separately; overlapping findings appear in both
  their categories and are not merged or hidden.
- Finding sequence, evidence records, source lines, timestamps, and observed values
  match exactly, in canonical order.
- Missing-candle ↔ time-gap relationships are present in both directions.
- Source lines, timestamps, and observed values are distinct typed members, not
  prose.

---

## Scenario 3 — Fatal input and invalid configuration never look successful

**Proves**: FR-007, FR-008, FR-011, SC-003, US1 scenario 4, US2 scenario 5

**Steps**
1. Submit each of: an empty file, a header-only file, a file with an unsupported
   encoding, and a structurally unparsable file.
2. Submit each invalid option combination from the pre-read validation table in the
   integration contract (for example score weights without scoring, or scoring
   under the frozen v1 JSON contract).
3. Attempt `GetResultAsync` and `ExportAsync` on each resulting run.

**Expected**
- Every case ends `Failed` with a `FatalDiagnostic` carrying its established code,
  class, and stage.
- No category count, score, or comparison evidence is exposed.
- `AvailableExports` is empty and `ExportAsync` returns `NotAvailable`.
- Invalid options are rejected **before** any dataset byte is interpreted — assert
  that no upload was stored and no work was queued.
- No run reads as clean at any point.

---

## Scenario 4 — Determinism and duplicate submission

**Proves**: FR-010, FR-012, SC-004, edge case "same run submitted more than once"

**Steps**
1. Submit a dataset with a given option set. Record the returned `WebRunId`.
2. Submit the identical bytes with identical options again.
3. Submit the identical bytes with **one changed** material option.
4. Compare exports from step 1 and step 2.

**Expected**
- Steps 1 and 2 return the same `WebRunId`; step 2 reports
  `JoinedExistingRun: true`.
- Exactly one run record exists for steps 1–2; no duplicate work was queued and no
  duplicate benchmark was created.
- Step 3 returns a **different** `WebRunId` and is separately retrievable.
- The two exports are substantively equivalent, byte-identical apart from members
  explicitly outside the comparison surface.
- No wall-clock value, sequence number, or random value influences the id.

---

## Scenario 5 — Long runs, refresh, and disconnect

**Proves**: FR-009, FR-012, SC-007, edge cases for refresh/navigation/timeouts

**Steps**
1. Submit a run and observe `GetStatusAsync` while it is still `Pending`/`Running`.
2. Re-query status and result with the same id, simulating a page refresh.
3. Attempt `GetResultAsync` before the run is terminal.
4. Simulate an aborted run and query its final state.
5. Query a run id that does not exist (or has been removed).

**Expected**
- Non-terminal runs return `NotReady` carrying the real status — never a clean
  result and never an empty success.
- Re-querying does not restart, duplicate, or reset the run.
- An aborted run ends `Failed` with a diagnostic, never `CompletedClean`.
- An unknown or expired id returns `Unavailable` with a reason.
- A completed run stays retrievable for its retention window.

---

## Scenario 6 — Scoring parity and non-applicable states

**Proves**: FR-015, FR-018, SC-001 (score values), US3 scenarios 1–4

**Steps**
1. Score a clean fixture, a fixture with known defects, and a fixture where at
   least one metric is not applicable or has a zero population.
2. Repeat with custom weights covering all six metrics.
3. Run one submission with scoring **not** requested.

**Expected**
- All six metrics appear with state, count, population, population kind, resolved
  weight, and normalized share, matching the CLI exactly.
- The average matches, along with its covered-metric count and excluded-metric
  list.
- `not applicable` / `not scored` / `not available` are explicit states with
  reasons — never `0`, never `100`, never an inferred number.
- Invalid weight configurations are rejected before dataset processing.
- With scoring not requested, the view exposes no scoring section and the
  validation counts, findings, order, and status are unchanged.

---

## Scenario 7 — Benchmark lifecycle and comparison parity

**Proves**: FR-016, FR-017, FR-018, SC-006, SC-008, US4 scenarios 1–6

**Steps**
1. Establish a validated dataset as a named benchmark through the web boundary.
2. Attempt to establish a second benchmark with the same name.
3. Race two concurrent establishments on one unused name.
4. Compare: an identical candidate; a candidate with a tolerated opening-price
   variation; a candidate with a material opening-price difference plus missing and
   extra candles; a candidate with incompatible timeframe; and a candidate with no
   overlapping timestamps.
5. Hash the benchmark's stored source content before and after every comparison.

**Expected**
- Establishment records the immutable identity, source content, context,
  validation results, six scores, and dataset score.
- The duplicate name is refused explicitly — never silently replaced.
- The concurrent race yields exactly one success and one deterministic conflict,
  with no partial benchmark directory left behind.
- Matched, missing, and extra records are reported separately; material
  discrepancies carry timestamp, field, both values, difference, and resolved
  tolerance.
- The tolerated difference is **not** material, and its aggregate evidence remains
  auditable.
- Incompatible-context and no-overlap cases are marked unavailable or incompatible
  — never a perfect agreement score.
- Candidate quality score, benchmark-agreement score, and the benchmark's recorded
  scores are three separate members.
- Benchmark source hashes are unchanged before and after (SC-008).
- Every comparison figure matches the CLI's comparison output.

---

## Scenario 8 — Architecture, source safety, and CLI non-regression

**Proves**: FR-006, FR-021, FR-022, FR-024, FR-025, FR-030, FR-033, SC-008, SC-010,
SC-011

**Steps**
1. Assert the `Validator.Application` assembly references none of the prohibited
   types listed in the integration contract.
2. Assert `Validator.Domain` and `Validator.Cli` have no source change attributable
   to this feature.
3. Hash every uploaded dataset before and after validation, scoring, reporting,
   comparison, and export.
4. Submit a file name and source values containing markup, quotes, delimiters, and
   control characters; inspect the view and the export.
5. Run the full existing CLI test suite.
6. Run the validation suite under a non-invariant culture and a non-UTC local time
   zone.

**Expected**
- No web, HTTP, session, view, or filesystem-path input type appears in the
  Application boundary.
- The existing CLI suite passes unchanged.
- Uploaded bytes are byte-for-byte identical before and after every operation.
- The view carries typed values with no markup or pre-escaped strings; the export
  escapes through the existing writers, and no data-derived text alters document
  structure.
- File names are safe base names with no path components and no absolute path.
- Results are identical under the alternate culture and time zone.
- The Domain + Application coverage gate remains at 100% over reachable code.

---

## Host-Dependent Scenarios (blocked on research R1)

These stay in scope and MUST be executed against the real website before the
feature is complete. They are listed here so they cannot be quietly dropped.

| Scenario | Proves | Blocked by |
|---|---|---|
| Keyboard-only completion of the primary workflow; status, summary, errors, and finding details reachable without a pointer | FR-031, SC-009 | R1 (stack), R7 (harness) |
| Host conventions for navigation, terminology, loading, empty, error, and success states | FR-028, US5 scenario 1 | R1 |
| Narrow and wide responsive layouts keep evidence visible | US5 scenario 5 | R1 |
| Duplicate-submission prevention at the UI level; in-progress feedback | US5 scenario 2 | R1 |
| Upload limits enforced before work is accepted, reported without unsafe server detail | FR-029 | R1 (host limits) |
| User context preserved and recovery guidance offered on rejection or expiry | FR-032, US5 scenario 3 | R1 |
| Representative-user timing targets | SC-005, SC-006 | R1 |
| Retention/expiry behavior matches the host's documented policy | FR-012, research R5 | R5 |

---

## Definition of Done

- [ ] Scenarios 1–8 pass, each with its tests written before implementation
- [ ] Domain + Application coverage gate at 100% over reachable code, with any
      exclusion documented per `docs/coverage-exclusion-policy.md`
- [ ] The web adapter carries integration/end-to-end coverage rather than being
      counted in the line gate
- [ ] Existing CLI behavior and contracts unchanged (SC-010)
- [ ] `README.md` updated with the Web Application Integration section required by
      FR-034 and Principle VIII, including the parity boundary, report access,
      configuration, retention, and run instructions — or the authoritative
      location of that guidance if the website lives in another repository
- [ ] Research items R1, R4, R5, R6 (identity) resolved or explicitly accepted as
      deferred before release
- [ ] Host-dependent scenarios executed against the real website
