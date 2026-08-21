# Quickstart: Benchmark Dataset Comparison

**Feature**: 004-benchmark-dataset-comparison
**Date**: 2026-08-19

## Prerequisites

- .NET 10 SDK installed
- Project built: `dotnet build`
- Two OHLCV CSV files: a trusted reference dataset and a candidate to compare

## Scenario 1: Establish a Benchmark

**Goal**: Create a named benchmark from a validated dataset.

```powershell
# Build the project
dotnet build

# Establish AUDUSD benchmark from a known dataset
dotnet run --project src/Validator.Cli -- `
  tests/Fixtures/AUDUSD_D1_reference.csv `
  --timeframe D1 `
  --market forex `
  --format json `
  --report-version 2 `
  --score `
  --instrument AUDUSD `
  --benchmark audusd-daily
```

**Expected outcome**:
- Exit code 0 (clean dataset) or 1 (validation findings present, but benchmark still established)
- A `benchmarks/audusd-daily/` directory is created containing `benchmark.json` and `source.csv`
- Console output confirms benchmark establishment with source identity and scores

**Verify**:
```powershell
# Confirm benchmark file exists
Get-ChildItem benchmarks/audusd-daily/

# Confirm metadata is valid JSON
Get-Content benchmarks/audusd-daily/benchmark.json -Raw | ConvertFrom-Json | Out-Null
```

---

## Scenario 2: Compare Candidate Against Benchmark (Identical Data)

**Goal**: Verify that comparing identical inputs produces no discrepancies and a perfect agreement score.

```powershell
# Compare the same file against its own benchmark
dotnet run --project src/Validator.Cli -- `
  tests/Fixtures/AUDUSD_D1_reference.csv `
  --timeframe D1 `
  --market forex `
  --format json `
  --report-version 2 `
  --score `
  --instrument AUDUSD `
  --compare audusd-daily
```

**Expected outcome**:
- Material discrepancies: 0
- Agreement score: 100.00
- All tolerated differences: 0 (values are identical)
- Coverage: all timestamps matched

---

## Scenario 3: Compare Candidate With Known Differences

**Goal**: Verify that a candidate with one material price difference and one
tolerated broker difference is correctly reported.

**Setup**: Use `tests/Fixtures/` test data that includes these deliberate variations.

```powershell
# Compare candidate with known variations
dotnet run --project src/Validator.Cli -- `
  tests/Fixtures/AUDUSD_D1_candidate_with_differences.csv `
  --timeframe D1 `
  --market forex `
  --format json `
  --report-version 2 `
  --score `
  --instrument AUDUSD `
  --compare audusd-daily
```

**Expected outcome**:
- Coverage shows all 96 timestamps matched
- Exactly 1 material price discrepancy at the correct timestamp and field
- Tolerated broker-level differences are not flagged as material
- Agreement score reflects only the material discrepancy count
- Candidate's independent scores are present and separate from the agreement score

---

## Scenario 4: Compare With Custom Tolerances

**Goal**: Verify that custom tolerance overrides are applied correctly.

```powershell
# Compare with stricter price tolerance
dotnet run --project src/Validator.Cli -- `
  tests/Fixtures/AUDUSD_D1_candidate_with_differences.csv `
  --timeframe D1 `
  --market forex `
  --format json `
  --report-version 2 `
  --score `
  --instrument AUDUSD `
  --compare audusd-daily `
  --tolerances '{"Open": {"absolute": 0.00005}, "Volume": {"relative": 0.02}}'
```

**Expected outcome**:
- The Open field uses the custom absolute tolerance of 0.00005 instead of the inferred default
- The Volume field uses the custom relative tolerance of 2% instead of the default 5%
- The resolved tolerances are recorded in the report for auditability

---

## Scenario 5: Compare With No Overlap

**Goal**: Verify graceful handling when datasets have no overlapping timestamps.

```powershell
# Compare datasets from completely different time periods
dotnet run --project src/Validator.Cli -- `
  tests/Fixtures/AUDUSD_D1_candidate_no_overlap.csv `
  --timeframe D1 `
  --market forex `
  --format json `
  --report-version 2 `
  --score `
  --instrument AUDUSD `
  --compare audusd-daily
```

**Expected outcome**:
- Coverage: matchedCount = 0
- Agreement score: unavailable with reason "No overlapping timestamps"
- No material discrepancies reported
- No misleading perfect match score (FR-025)

---

## Scenario 6: Reject Duplicate Benchmark Name

**Goal**: Verify that creating a benchmark with an existing name fails cleanly.

```powershell
# First establishment succeeds
dotnet run --project src/Validator.Cli -- `
  tests/Fixtures/AUDUSD_D1_reference.csv `
  --timeframe D1 --market forex `
  --format json --report-version 2 --score `
  --instrument AUDUSD `
  --benchmark audusd-daily

# Second establishment with same name fails
dotnet run --project src/Validator.Cli -- `
  tests/Fixtures/AUDUSD_D1_reference.csv `
  --timeframe D1 --market forex `
  --format json --report-version 2 --score `
  --instrument AUDUSD `
  --benchmark audusd-daily
```

**Expected outcome**:
- Second command exits with code 2 (fatal error)
- Error message: "Benchmark 'audusd-daily' already exists. Use a different name or delete the existing benchmark."
- No benchmark files are overwritten

---

## Scenario 7: Reject Invalid Tolerance Configuration

**Goal**: Verify that invalid tolerance options are rejected before data is read.

```powershell
# Negative tolerance
dotnet run --project src/Validator.Cli -- `
  tests/Fixtures/AUDUSD_D1_candidate_identical.csv `
  --timeframe D1 --market forex `
  --format json --report-version 2 --score `
  --instrument AUDUSD `
  --compare audusd-daily `
  --tolerances '{"Open": {"absolute": -0.001}}'
```

**Expected outcome**:
- Exit code 2 (fatal error)
- Error message identifies the negative Open tolerance as invalid
- No dataset bytes are read

---

## Scenario 8: Deterministic Output

**Goal**: Verify that repeated comparisons produce byte-identical output.

```powershell
# Run comparison twice
dotnet run --project src/Validator.Cli -- `
  tests/Fixtures/AUDUSD_D1_candidate_with_differences.csv `
  --timeframe D1 --market forex `
  --format json --report-version 2 --score `
  --instrument AUDUSD `
  --compare audusd-daily `
  --output report1.json

dotnet run --project src/Validator.Cli -- `
  tests/Fixtures/AUDUSD_D1_candidate_with_differences.csv `
  --timeframe D1 --market forex `
  --format json --report-version 2 --score `
  --instrument AUDUSD `
  --compare audusd-daily `
  --output report2.json

# Compare
Compare-Object (Get-Content report1.json) (Get-Content report2.json)
```

**Expected outcome**:
- `Compare-Object` emits no differences (byte-identical output, SC-006)

---

## Running Tests

```powershell
# Run all tests
dotnet test

# Run only benchmark comparison tests
dotnet test --filter "Benchmark|Comparison"

# Run with verbose output
dotnet test --verbosity normal
```

**Expected outcome**:
- All tests pass
- Domain remains at 100% line and branch coverage, and the merged
  Domain/Application coverage ratchet passes the thresholds documented in
  `.github/workflows/coverage.yml`.
