# Comparison Report Contract

**Feature**: 004-benchmark-dataset-comparison
**Version**: 1
**Date**: 2026-08-19

## Purpose

Defines the machine-readable (JSON) and human-readable (text) output formats for a benchmark comparison report. This contract is the boundary between the comparison subsystem and its consumers (users, scripts, downstream tools).

## JSON Contract (v2 JSON report, extended)

The comparison report extends the existing `DetailedValidationReport` v2 JSON contract with a new `benchmarkComparison` section. The existing report fields remain unchanged (FR-029).

### Top-Level Structure

```json
{
  "contractVersion": 2,
  "status": "Clean",
  "source": { /* existing SourceIdentity */ },
  "context": { /* existing ValidationContextSnapshot */ },
  "coverage": { /* existing ScanCoverage */ },
  "checks": [ /* existing CheckExecution[] */ ],
  "summary": { /* existing DetailedSummary */ },
  "reconciliation": { /* existing ReportReconciliation */ },
  "findings": { /* existing ICompletedFindingCatalog */ },
  "score": { /* existing DatasetScoreReport (candidate) */ },
  "benchmarkComparison": {
    "contractVersion": 1,
    "benchmark": { /* BenchmarkSnapshot */ },
    "configuration": { /* ComparisonConfiguration */ },
    "comparisonCoverage": { /* ComparisonCoverage */ },
    "materialDiscrepancies": [ /* FieldDiscrepancy[] */ ],
    "toleratedSummary": [ /* ToleratedDifferenceAggregate[] */ ],
    "agreementScore": { /* BenchmarkAgreementScore */ }
  }
}
```

### benchmarkComparison.configuration

```json
{
  "benchmarkName": "audusd-benchmark",
  "fields": [
    {
      "field": "Open",
      "enabled": true,
      "absoluteTolerance": null,
      "relativeTolerance": null,
      "resolvedAbsolute": "0.00010",
      "resolvedRelative": "0.0001"
    },
    {
      "field": "High",
      "enabled": true,
      "absoluteTolerance": null,
      "relativeTolerance": null,
      "resolvedAbsolute": "0.00010",
      "resolvedRelative": "0.0001"
    },
    {
      "field": "Low",
      "enabled": true,
      "absoluteTolerance": null,
      "relativeTolerance": null,
      "resolvedAbsolute": "0.00010",
      "resolvedRelative": "0.0001"
    },
    {
      "field": "Close",
      "enabled": true,
      "absoluteTolerance": null,
      "relativeTolerance": null,
      "resolvedAbsolute": "0.00010",
      "resolvedRelative": "0.0001"
    },
    {
      "field": "Volume",
      "enabled": true,
      "absoluteTolerance": null,
      "relativeTolerance": null,
      "resolvedAbsolute": "0",
      "resolvedRelative": "0.05"
    }
  ],
  "timestampMode": "Exact"
}
```

### benchmarkComparison.comparisonCoverage

```json
{
  "benchmarkRecordCount": 1650,
  "candidateRecordCount": 1648,
  "matchedCount": 1645,
  "missingFromCandidateCount": 5,
  "extraInCandidateCount": 3,
  "overlappingRange": {
    "start": "2020-01-02T00:00:00Z",
    "end": "2026-08-15T00:00:00Z"
  }
}
```

### benchmarkComparison.materialDiscrepancies

```json
[
  {
    "timestampUtc": "2025-03-15T00:00:00Z",
    "field": "Open",
    "benchmarkValue": "0.63421",
    "candidateValue": "0.63458",
    "difference": "0.00037",
    "directionalDifference": "0.00037",
    "resolvedAbsoluteTolerance": "0.00010",
    "resolvedRelativeTolerance": "0.0001",
    "toleranceDecision": "MaterialDifference"
  }
]
```

### benchmarkComparison.toleratedSummary

```json
[
  {
    "field": "Open",
    "totalCompared": 1645,
    "acceptedCount": 1643,
    "acceptedByAbsoluteCount": 820,
    "acceptedByRelativeCount": 823,
    "materialCount": 2
  },
  {
    "field": "High",
    "totalCompared": 1645,
    "acceptedCount": 1645,
    "acceptedByAbsoluteCount": 1645,
    "acceptedByRelativeCount": 0,
    "materialCount": 0
  },
  {
    "field": "Low",
    "totalCompared": 1645,
    "acceptedCount": 1645,
    "acceptedByAbsoluteCount": 1645,
    "acceptedByRelativeCount": 0,
    "materialCount": 0
  },
  {
    "field": "Close",
    "totalCompared": 1645,
    "acceptedCount": 1644,
    "acceptedByAbsoluteCount": 1000,
    "acceptedByRelativeCount": 644,
    "materialCount": 1
  },
  {
    "field": "Volume",
    "totalCompared": 1645,
    "acceptedCount": 1640,
    "acceptedByAbsoluteCount": 0,
    "acceptedByRelativeCount": 1640,
    "materialCount": 5
  }
]
```

### benchmarkComparison.agreementScore

```json
{
  "score": {
    "exact": "1637/1645",
    "rounded": "99.51"
  },
  "formula": "100 × (matchedPopulation - materialDiscrepancyTimestamps) / matchedPopulation",
  "matchedPopulation": 1645,
  "materialDiscrepancyTimestamps": 8,
  "unavailableReason": null
}
```

**When unavailable**:
```json
{
  "score": null,
  "formula": "100 × (matchedPopulation - materialDiscrepancyTimestamps) / matchedPopulation",
  "matchedPopulation": 0,
  "materialDiscrepancyTimestamps": 0,
  "unavailableReason": "No overlapping timestamps between benchmark and candidate"
}
```

---

## Text Report Contract

The text report adds a `BENCHMARK COMPARISON` section after the existing validation findings.

```
=== BENCHMARK COMPARISON ===

Benchmark: audusd-benchmark
  Source: AUDUSD_D1.csv (1,024,567 bytes, sha256=a1b2c3...)
  Scores: MissingCandle=100.00, DuplicateRecord=100.00, InvalidOhlc=99.88,
          ClosedMarketRecord=100.00, TimeGap=99.94, MalformedRow=100.00
  Dataset Average: 99.96

Coverage:
  Benchmark records: 1,650
  Candidate records: 1,648
  Matched timestamps: 1,645
  Missing from candidate: 5
  Extra in candidate: 3
  Overlapping range: 2020-01-02 to 2026-08-15

Material Discrepancies (3 found):

  [1] 2025-03-15 Open
      Benchmark: 0.63421  Candidate: 0.63458  Diff: +0.00037
      Tolerance: absolute=0.00010, relative=0.01%
      Decision: Material (exceeds both tolerances)

  [2] 2025-06-22 Close
      Benchmark: 0.65100  Candidate: 0.65062  Diff: -0.00038
      Tolerance: absolute=0.00010, relative=0.01%
      Decision: Material (exceeds both tolerances)

  [3] 2026-01-10 Volume
      Benchmark: 125,000  Candidate: 118,200  Diff: -6,800 (5.44%)
      Tolerance: absolute=0, relative=5%
      Decision: Material (exceeds relative tolerance)

Tolerated Differences:
  Open: 1,643 of 1,645 accepted (2 material)
  High: 1,645 of 1,645 accepted (0 material)
  Low:  1,645 of 1,645 accepted (0 material)
  Close: 1,644 of 1,645 accepted (1 material)
  Volume: 1,640 of 1,645 accepted (5 material)

Candidate Quality Score: 99.96
Benchmark-Agreement Score: 99.51 (8/1,645 timestamps with material discrepancies)
```

**When no overlap**:
```
=== BENCHMARK COMPARISON ===

Benchmark: audusd-benchmark
  ...

Coverage:
  ...
  Matched timestamps: 0

Comparison: UNAVAILABLE — no overlapping timestamps between benchmark and candidate.
A benchmark-agreement score cannot be computed.
```

---

## Contract Rules

1. The `benchmarkComparison` section is present **only** when `--compare` was specified.
2. When `--compare` is specified, the candidate's independent `score` section is always present (FR-021).
3. Material discrepancies are ordered by timestamp ascending, then field name alphabetically, then absolute difference descending.
4. All numeric values use decimal notation with invariant culture (no thousand separators in JSON; thousand separators in text for readability).
5. The JSON contract is deterministic: identical inputs and configuration produce byte-identical output (FR-031).
6. Fatal comparison failures produce no partial report (FR-030).
7. Missing timestamps produce coverage entries but no `FieldDiscrepancy` records.
8. Extra timestamps produce coverage entries but no `FieldDiscrepancy` records and no fabricated benchmark values (FR-009).
