# Benchmark Snapshot Contract

**Feature**: 004-benchmark-dataset-comparison
**Version**: 1
**Date**: 2026-08-19

## Purpose

Defines the on-disk format for a benchmark snapshot: the JSON metadata file and the associated source copy. This contract is the boundary between the benchmark establishment, storage, and comparison subsystems.

## Benchmark Directory Layout

```
benchmarks/
└── <safe-name>/
    ├── benchmark.json
    └── source.csv
```

- `<safe-name>` is derived from the user-supplied name: lowercased, spaces replaced with hyphens, non-alphanumeric characters removed, no path separators.
- `source.csv` is an exact byte-for-byte copy of the original dataset file.
- `benchmark.json` is the machine-readable metadata.

## benchmark.json Schema

```json
{
  "contractVersion": 1,
  "name": "audusd-benchmark",
  "establishedAtUtc": "2026-08-19T14:30:00Z",
  "source": {
    "fileName": "AUDUSD_D1.csv",
    "byteSize": 1024567,
    "sha256": "a1b2c3d4e5f6...64 hex chars..."
  },
  "context": {
    "timeframe": "D1",
    "calendar": {
      "profile": "Forex"
    },
    "timestamp": {
      "interpretation": "UtcNormalized",
      "dateFormat": "yyyy.MM.dd",
      "timeFormat": "HH:mm",
      "tzOffset": "+02:00"
    },
    "delimiter": "comma",
    "hasHeader": false,
    "dateRange": {
      "start": "2020-01-02T00:00:00Z",
      "end": "2026-08-18T00:00:00Z"
    }
  },
  "coverage": {
    "totalRecords": 1650,
    "examinationPopulation": 1650
  },
  "checks": [
    { "check": "MissingCandles", "status": "Completed", "count": 0 },
    { "check": "DuplicateRecords", "status": "Completed", "count": 0 },
    { "check": "InvalidOhlc", "status": "Completed", "count": 2 },
    { "check": "ClosedMarketRecords", "status": "Completed", "count": 0 },
    { "check": "TimeGaps", "status": "Completed", "count": 1 },
    { "check": "MalformedRows", "status": "Completed", "count": 0 }
  ],
  "metrics": [
    {
      "category": "MissingCandle",
      "state": "Scored",
      "count": 0,
      "population": 1648,
      "populationKind": "ExpectedCandles",
      "score": { "exact": "100/1", "rounded": "100.00" }
    },
    {
      "category": "DuplicateRecord",
      "state": "Scored",
      "count": 0,
      "population": 1650,
      "populationKind": "AcceptedRows",
      "score": { "exact": "100/1", "rounded": "100.00" }
    },
    {
      "category": "InvalidOhlc",
      "state": "Scored",
      "count": 2,
      "population": 1650,
      "populationKind": "AcceptedRows",
      "score": { "exact": "824/825", "rounded": "99.88" }
    },
    {
      "category": "ClosedMarketRecord",
      "state": "Scored",
      "count": 0,
      "population": 1650,
      "populationKind": "AcceptedRows",
      "score": { "exact": "100/1", "rounded": "100.00" }
    },
    {
      "category": "TimeGap",
      "state": "Scored",
      "count": 1,
      "population": 1648,
      "populationKind": "ExpectedCandles",
      "score": { "exact": "1647/1648", "rounded": "99.94" }
    },
    {
      "category": "MalformedRow",
      "state": "Scored",
      "count": 0,
      "population": 1650,
      "populationKind": "ExaminedRows",
      "score": { "exact": "100/1", "rounded": "100.00" }
    }
  ],
  "weighting": {
    "source": "Default",
    "weights": [
      { "category": "MissingCandle", "weight": 1.0, "normalisedShare": "0.17" },
      { "category": "DuplicateRecord", "weight": 1.0, "normalisedShare": "0.17" },
      { "category": "InvalidOhlc", "weight": 1.0, "normalisedShare": "0.17" },
      { "category": "ClosedMarketRecord", "weight": 1.0, "normalisedShare": "0.17" },
      { "category": "TimeGap", "weight": 1.0, "normalisedShare": "0.17" },
      { "category": "MalformedRow", "weight": 1.0, "normalisedShare": "0.17" }
    ]
  },
  "dataset": {
    "average": { "exact": "3596483/3600", "rounded": "99.96" },
    "metricsCovered": 6,
    "coveredCategories": [
      "MissingCandle", "DuplicateRecord", "InvalidOhlc",
      "ClosedMarketRecord", "TimeGap", "MalformedRow"
    ],
    "excludedCategories": [],
    "unavailableReason": null
  }
}
```

## Contract Rules

1. `contractVersion` is always `1` for this version of the feature. A version bump signals a breaking change to the schema.
2. `name` matches the directory name exactly.
3. `source.sha256` is verified against `source.csv` bytes on every read; a mismatch is a fatal error.
4. `context`, `coverage`, `checks`, `metrics`, `weighting`, and `dataset` are copied verbatim from the validation run that produced the benchmark — they are never recomputed.
5. All scores use `ExactRatio` notation (numerator/denominator) plus a `rounded` two-decimal string.
6. The JSON file is written atomically (write to temp, then move) to prevent partial reads.

## Error Cases

| Condition | Behavior |
|-----------|----------|
| Missing `benchmark.json` | Fatal: benchmark not found |
| Missing `source.csv` | Fatal: benchmark source unavailable |
| SHA-256 mismatch on `source.csv` | Fatal: benchmark source corrupted |
| Invalid JSON | Fatal: benchmark metadata corrupted |
| Unknown `contractVersion` | Fatal: incompatible benchmark format |
| Name collision on creation | Fatal: benchmark already exists; no overwrite |
