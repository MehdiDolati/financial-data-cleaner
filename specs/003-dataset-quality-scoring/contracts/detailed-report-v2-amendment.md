# Amendment to Detailed Report v2: Optional Scoring Section

This records the exact, auditable delta feature 003 applies to the feature-002
contract
[`detailed-report-v2.schema.json`](../../002-detailed-error-report/contracts/detailed-report-v2.schema.json).

## Summary of the Delta

One optional top-level property, `scoring`, is added to the v2 success document.
Nothing else changes.

| Aspect | Before | After |
|---|---|---|
| `contractVersion` | `2` | `2` (unchanged) |
| Top-level required properties | 10 | 10 (unchanged) |
| Top-level optional properties | none | `scoring` |
| `additionalProperties` | `false` | `false` (unchanged) |
| Existing `$defs` | as published | unchanged |
| v1 contract | as published | unchanged |

## Why the Schema Must Change At All

The v2 success schema declares `"additionalProperties": false`. A document
carrying an undeclared `scoring` member would therefore *fail* its own contract.
Declaring the property is the minimum change that makes a scored v2 document
valid.

## Why This Is Backward Compatible

- The property is **optional**. Every document produced before this feature, and
  every document produced without `--score`, remains valid unchanged.
- The property is **absent**, not null, when scoring is not requested, so no
  consumer sees a new member it must handle.
- No existing property is added to `required`, removed, renamed, retyped, or
  constrained differently.
- Existing golden-output and contract tests for v2 continue to pass without
  modification, which is the acceptance test for this claim.

## Why `contractVersion` Stays 2

Version 2 consumers read named properties from a closed object. An optional
additive property does not break that promise: an existing consumer that never
reads `scoring` is unaffected. Bumping to version 3 would signal a breaking
change where none exists and would strand the very consumers this amendment is
designed not to disturb.

## Applied Change

Add to the top-level `properties` object:

```json
"scoring": { "$ref": "scoring-v2.schema.json" }
```

`scoring` is **not** added to the top-level `required` array.

The referenced section is fully specified in
[`scoring-v2.schema.json`](scoring-v2.schema.json).

## Presence Rules

| Run | `scoring` |
|---|---|
| `--format json --report-version 2` | Absent. |
| `--score --format json --report-version 2` | Present and complete. |
| `--score --format json` / `--report-version 1` | No document; the run is rejected as a v1 configuration conflict. |
| Fatal run | No success document at all, so no scoring. |

## Fatal Contract

[`fatal-diagnostic-v2.schema.json`](../../002-detailed-error-report/contracts/fatal-diagnostic-v2.schema.json)
is **unchanged**. Its `code` and `stage` enumerations remain closed, and scoring
failures reuse existing codes:

| Scoring failure | Code | Class / Stage |
|---|---|---|
| Invalid or incomplete `--score-weights` | `INVALID_ARGUMENT` | Configuration / ArgumentValidation |
| `--score` requested with the v1 contract | `INVALID_ARGUMENT` | Configuration / ArgumentValidation |
| Count exceeds its population (rate outside 0..1) | `REPORT_RECONCILIATION_FAILED` | Operational / Reconciliation |

## Verification

- A scored v2 document validates against the amended success schema.
- An unscored v2 document validates against the amended success schema and
  contains no `scoring` member.
- Both documents validate against the schema as published for feature 002 in
  every respect other than the new optional member.
- A v1 document is byte-identical to its pre-feature output.
