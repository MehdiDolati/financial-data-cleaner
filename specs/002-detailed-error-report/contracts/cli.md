# Command-Line Contract: Detailed Reporting

This contract extends the feature-001 command without changing existing defaults.

## Command Shape

```text
validator <input-file> [existing-options] [--report-version <1|2>]
```

All existing input, market, calendar, timeframe, output, and exit-code rules
continue to apply.

## Representation Selection

| Options | Successful representation |
|---|---|
| default text | Existing concise six-line summary. |
| `--verbose` | Complete detailed human-readable text. |
| `--format json` | Existing JSON v1 contract unchanged. |
| `--format json --report-version 1` | Existing JSON v1 contract unchanged. |
| `--format json --report-version 2` | Complete detailed JSON v2 contract. |

`--report-version` accepts only `1` or `2`, is valid only with
`--format json`, and defaults to `1`. `--verbose` remains accepted with JSON but
does not change JSON v1 or v2 content. Unsupported values, repeated conflicting
values, or use with text fail argument validation before CSV parsing.

The required help examples include:

```text
validator EURUSD_H1.csv --verbose
validator EURUSD_H1.csv --format json
validator EURUSD_H1.csv --format json --report-version 2
validator EURUSD_H1.csv --format json --report-version 2 --output report-v2.json
```

## Detailed Text Contract

Verbose text begins with the existing six summary lines in the existing order,
then emits these labeled sections:

```text
Report status
Source identity
Validation context
Scan coverage
Check execution
Category reconciliation
Findings
```

The status section states `Clean` or `FindingsDetected` and explicitly states
that validation completed and the finding set is complete for the listed checks.
Each category section shows both its summary count and detailed entry count. A
cross-category sum, if shown, is labeled `Sum of category counts (not unique root
causes)`.

Each finding displays its stable reference, category, title, count contribution,
location, explanation, category-specific named evidence, relationships, and
suggested action. Missing values are labeled `not applicable`; no physical line
is invented for a missing candle. A missing-candle or time-gap entry additionally
labels the source lines of the nearest preceding and following observed records
as `previousObservedSourceLine` and `nextObservedSourceLine`,
so an absence can be located in the file without inventing its line; a boundary
gap labels the unavailable side `not applicable`.

Source-derived strings are rendered as quoted escaped values. CR, LF, tab,
quotes, backslashes, and control characters cannot create headings or additional
finding lines. Detailed text is complete and never paginated, sampled, or
truncated by the validator.

## JSON Contracts

### V1

Unversioned `--format json` and explicit `--report-version 1` conform to
`specs/001-ohlcv-data-quality-validator/contracts/validation-report.schema.json`.
No v2 field is added, renamed, or substituted.

### V2 Success

Explicit `--report-version 2` conforms to
[`detailed-report-v2.schema.json`](detailed-report-v2.schema.json). Stdout
contains exactly one UTF-8 JSON document and no informational text when
`--output` is absent.

The v2 document exposes status, source identity, resolved validation context,
scan coverage, six check executions, established summary, reconciliation, and
all findings with typed evidence and relationships. The `findings` array follows
the canonical category/timestamp/line/reference order.

### V2 Fatal

If v2 was successfully selected and validation cannot produce or publish a
trustworthy report, stderr contains exactly one document conforming to
[`fatal-diagnostic-v2.schema.json`](fatal-diagnostic-v2.schema.json).

- Exit code is `2`.
- Stdout is empty.
- The selected report path is not created or modified.
- No successful report, final category totals, `isClean`, or complete-finding
  claim is emitted.

The CLI determines a recognizable valid v2 representation before validating
unrelated options, so a later argument/configuration failure can use structured
v2 stderr. If the representation options themselves are malformed or
contradictory, the CLI uses the existing actionable text diagnostic because no
valid v2 contract was selected.

## Output Destination

`--output <path>` retains the existing one-line success message on stdout after
an atomic report commit:

```text
Validation complete: findings=<sum-of-six-counts>; clean=<true|false>; report=<path>
```

`findings` remains explicitly the sum of the six established counts, not a count
of unique root causes. The path is printed as supplied. Full v2 content goes only
to the selected file.

The report is rendered to a temporary artifact and atomically moved into place
only after successful completion. If rendering or commit fails, a pre-existing
destination remains unchanged. A destination resolving to the source file is
rejected before the source is opened for validation.

## Fatal Text Compatibility

Concise text, verbose text, and JSON v1 requests retain an actionable stderr text
diagnostic on fatal exit. Feature 002 enriches it with stable code, class, stage,
guidance, source location when known, and unfinished checks, but it is not a
successful data-quality report and stdout remains empty.

## Exit Codes

| Code | Meaning |
|---:|---|
| `0` | Help requested, or successful `Clean` report. |
| `1` | Successful `FindingsDetected` report. |
| `2` | Fatal dataset, configuration, operational, reconciliation, rendering, or publication failure. |

Version selection does not change successful exit behavior or any six-category
count meaning.

## Determinism and Completeness

- Repeated runs over identical source bytes and options produce identical
  substantive text/v2 JSON and finding order.
- Reports contain no generated-at timestamp, random public reference, absolute
  source path, or locale-dependent number/date representation.
- Every successful detailed report explicitly sets or states completeness.
- Every category contribution sum equals its summary count before rendering.
- Large reports stream through bounded temporary buffers; screen size and finding
  count never cause silent truncation.