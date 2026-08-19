# Feature Specification: Benchmark Dataset Comparison

**Feature Branch**: `004-benchmark-dataset-comparison`

**Created**: 2026-08-18

**Status**: Draft - Ready for Planning

**Input**: User description: "the next feature I want is to be able to validate and score a dataset against a benchmark. For example I want to have an AUDUSD dataset set as benchmark with its own scores then given a new dataset downloaded from the internet I want to be able to measure the quality of this dataset against the benchmark. Also any incostistencies shall be reported. For example if the opening price of a day in the new dataset is different from the benchmark the output shall menton it. Be careful though the data are not so accurate and exact. Based on brokers some little difference are not only unavoidable but also acceptable. we need to handle this delicately so we wont miss incostistencies while not giving too much mismatches. come back with your questions and in every suggestion give your own prefered opinion as well"

## Clarifications

The following decisions have significant impact on the benchmark contract and comparison results.

### Session 2026-08-18

- **Q1: What should a saved benchmark contain?** → A: Save an immutable benchmark snapshot containing the source dataset, source identity, validation context, six metric scores, dataset score, and creation metadata.
- **Q2: What should the comparison score mean?** → A: Keep the candidate's independent six-metric quality score and add a separate benchmark-agreement score; do not replace either with a combined score.
- **Q3: How should acceptable broker differences be configured and timestamps aligned?** → A: Use configurable per-field absolute and relative tolerances, compare the union of timestamps, and report missing and extra candles separately.
- **Q4: What default tolerance profile should apply when the user does not provide one?** → A: Use a conservative forex-oriented profile: price fields accept the greater of one fractional quote-unit step or 0.01% of the benchmark value; volume accepts differences up to 5% of the benchmark volume. The user can override each field explicitly, and every resolved tolerance is reported. This is a deliberately cautious starting point: it filters ordinary broker rounding and small feed variation without making large price changes or broad history differences disappear.

### Session 2026-08-19

- **Q5: How should the fractional quote-unit step be determined when the instrument has no declared quote precision?** → A: Infer the step from the benchmark dataset's observed decimal precision — scan all Open/High/Low/Close values and determine the smallest positive fractional unit represented (e.g., values with up to 5 decimal places yield a step of 0.00001). No explicit user input is required.
- **Q6: What should the CLI exit code represent when --compare is used?** → A: Exit 0 if the comparison completed successfully (regardless of discrepancies found); exit 2 for fatal errors. The comparison is purely advisory (FR-026), so the report itself carries the findings rather than the exit code. This keeps comparison behavior consistent with the existing validator pattern where exit 1 means validation findings, and avoids overloading the exit code with threshold logic.
- **Q7: Where should benchmark snapshots be stored on disk?** → A: Default to a `benchmarks/` directory at the project root, overridable with a `--benchmark-dir <path>` option. The default directory is created on first benchmark establishment. The user can point to a shared location for team use.- **Q8: Should users be able to delete an existing benchmark?** → A: Yes. Support `--benchmark-delete <name>` to remove a benchmark's metadata and source copy. This lets users clean up stale or incorrect benchmarks. Deletion requires explicit confirmation or a `--yes` flag to prevent accidental removal.
- **Q9: What context mismatches should block comparison vs. merely be noted?** → A: Timeframe mismatch is a hard fail — comparing H1 candles against D1 candles is meaningless, so the operation is rejected with a clear diagnostic. Other context differences (calendar, timestamp interpretation, date range) are noted in the report as informational warnings but do not block comparison, since the union-of-timestamps approach handles range differences gracefully.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Establish a Trusted Benchmark (Priority: P1)

As a data engineer or trader, I want to designate a validated market-data dataset as a named benchmark with its validation context and scores so that future datasets can be compared against the same trusted reference.

**Why this priority**: A comparison is not reproducible or meaningful unless the reference dataset and the conditions under which it was scored are clearly identified.

**Independent Test**: Establish an AUDUSD benchmark from a known dataset, then inspect the resulting benchmark record and verify that its identity, time range, market context, validation results, and scores can be retrieved without ambiguity.

**Acceptance Scenarios**:

1. **Given** a dataset that completes validation successfully, **When** the user establishes it as a named benchmark, **Then** the system records its identity, validation configuration, coverage, six metric results, and dataset score.
2. **Given** a dataset that cannot produce a trustworthy complete validation report, **When** the user attempts to establish it as a benchmark, **Then** the operation fails with an actionable diagnostic and no benchmark is created.
3. **Given** an existing benchmark name, **When** the user attempts to create another benchmark with the same name, **Then** the system rejects the operation or requires an explicit replacement action and never silently overwrites the existing reference.
4. **Given** a benchmark record, **When** it is used later, **Then** the system can identify the exact source content and validation context from the benchmark record.

---

### User Story 2 - Compare a Candidate Dataset Against the Benchmark (Priority: P1)

As a user evaluating a dataset downloaded from the internet, I want to compare it against the benchmark so that I can determine whether it covers the same market history and contains materially different OHLCV values.

**Why this priority**: Detecting candidate-versus-reference inconsistencies is the central value of the feature.

**Independent Test**: Compare a candidate with a known one-day opening-price difference, one missing candle, and one extra candle against an AUDUSD benchmark, then verify that each discrepancy is identified at the relevant timestamp and source location.

**Acceptance Scenarios**:

1. **Given** a candidate with the same timestamps and values as the benchmark, **When** comparison is requested, **Then** the report states that no material benchmark discrepancies were found and preserves both datasets unchanged.
2. **Given** a candidate whose opening price differs materially at a shared timestamp, **When** comparison is requested, **Then** the report identifies the timestamp, field, benchmark value, candidate value, difference, and applicable tolerance decision.
3. **Given** a candidate missing a timestamp present in the benchmark, **When** comparison is requested, **Then** the report identifies the missing benchmark candle separately from field-value mismatches.
4. **Given** a candidate containing a timestamp absent from the benchmark, **When** comparison is requested, **Then** the report identifies the extra candidate candle and does not fabricate benchmark values for it.
5. **Given** a candidate with a small broker-dependent difference within the accepted tolerance, **When** comparison is requested, **Then** the value is not reported as a material inconsistency and the tolerance decision remains auditable.

---

### User Story 3 - Review Comparison Quality and Scores (Priority: P1)

As someone deciding whether to use a candidate dataset, I want to see its standalone validation scores alongside its benchmark comparison results so that I can distinguish intrinsic data defects from legitimate provider differences.

**Why this priority**: A candidate may be structurally clean but differ slightly from the benchmark, or match the benchmark closely while containing malformed or invalid records. The report must make those cases distinguishable.

**Independent Test**: Compare candidates with known structural defects and known tolerated or material value differences, then verify that the report exposes independent validation scores, comparison outcomes, the benchmark scores, and an overall result according to the resolved score policy.

**Acceptance Scenarios**:

1. **Given** a candidate with independent validation findings, **When** comparison is requested, **Then** the report includes the candidate's six metric scores and the benchmark's corresponding scores without conflating their defect counts.
2. **Given** a comparison with both tolerated and material differences, **When** the report is reviewed, **Then** it distinguishes accepted differences from material inconsistencies and shows counts for each.
3. **Given** a candidate with no overlapping timestamps, **When** comparison is requested, **Then** the report marks comparison coverage as unavailable or insufficient rather than presenting a misleading perfect match score.
4. **Given** a completed comparison, **When** the user requests machine-readable output, **Then** every discrepancy, tolerance decision, score, and coverage statistic is available as a separate documented field.

---

### User Story 4 - Reproduce and Audit a Comparison (Priority: P2)

As a pipeline owner or auditor, I want a comparison to be deterministic and self-describing so that I can explain why a dataset was accepted, questioned, or rejected later.

**Why this priority**: Financial-data decisions need evidence that survives repeated runs, provider changes, and review by someone who did not perform the original comparison.

**Independent Test**: Compare identical benchmark and candidate inputs repeatedly with identical options, then verify byte-identical substantive output and a complete record of the inputs, configuration, tolerances, matching coverage, and findings.

**Acceptance Scenarios**:

1. **Given** identical benchmark and candidate content with identical comparison configuration, **When** the comparison is repeated, **Then** all scores, discrepancy ordering, tolerance decisions, and output values are identical.
2. **Given** a comparison report, **When** an auditor examines it, **Then** they can trace every material discrepancy to a benchmark timestamp and candidate source location where available.
3. **Given** invalid comparison configuration, **When** the run starts, **Then** it fails before the datasets are read and explains the specific configuration problem.

### Edge Cases

- A benchmark and candidate use different timeframes, market profiles, timestamp interpretations, or covered date ranges.
- Either dataset contains malformed rows, duplicate timestamps, invalid OHLC values, or multiple rows for a timestamp.
- The benchmark contains a defect; comparison must not silently treat every benchmark value as ground truth without exposing benchmark quality and limitations.
- A candidate contains a timestamp that is valid in its own market calendar but outside the benchmark's covered range.
- A candidate has a very small numerical difference at many timestamps and a single material difference at one timestamp.
- A price is zero or near zero, making a relative tolerance unstable or undefined.
- Decimal values have different textual precision but represent the same numeric value.
- The datasets have no overlapping timestamps, only one overlapping timestamp, or overlap only in a period with known market closure.
- A tolerance is negative, non-numeric, incomplete, contradictory, or so broad that all differences would be accepted.
- A benchmark is deleted, unavailable, corrupted, or its recorded source identity no longer matches the supplied source.
- Counts are large enough that discrepancy totals, coverage values, or score calculations could overflow or lose precision.
- A fatal validation or comparison failure occurs after one input has been read; no partial comparison score or misleading success report is emitted.

## Requirements *(mandatory)*

### Functional Requirements

#### Benchmark Lifecycle

- **FR-001**: The system MUST allow a user to establish one successfully validated dataset as a named benchmark with an unambiguous instrument identity, coverage range, timeframe, market context, source identity, validation configuration, six metric results, and dataset score.
- **FR-002**: The system MUST preserve the benchmark's recorded scores and validation context so later comparisons can show what was known about the reference when it was established.
- **FR-003**: The system MUST NOT silently overwrite, mutate, or replace an existing benchmark when a name collision occurs.
- **FR-004**: The system MUST reject benchmark creation when validation does not produce a trustworthy complete report, and MUST leave no partial benchmark artifact.
- **FR-005**: The benchmark record MUST identify the exact source content used to create it and MUST detect a mismatch when a later operation claims to provide different content for that benchmark.

#### Comparison Eligibility and Alignment

- **FR-006**: The system MUST reject comparison when the candidate and benchmark timeframes differ (e.g., H1 vs D1) with a fatal diagnostic explaining the incompatibility. For other context differences (calendar, timestamp interpretation, date range), the system MUST proceed with comparison and note the differences in the report as informational warnings.
- **FR-007**: The system MUST normalize timestamps to the established UTC comparison representation before matching records.
- **FR-008**: The system MUST match records by timestamp and MUST report benchmark timestamps absent from the candidate as missing candidate records.
- **FR-009**: The system MUST report candidate timestamps absent from the benchmark as extra candidate records and MUST NOT invent reference values for them.
- **FR-010**: The system MUST make the comparison coverage visible, including benchmark record count, candidate record count, matched count, missing count, extra count, and the overlapping time range.
- **FR-011**: The system MUST distinguish a value mismatch at a shared timestamp from a missing or extra timestamp and MUST preserve those categories in both human-readable and machine-readable output.

#### Field Comparison and Tolerance

- **FR-012**: For every matched timestamp, the system MUST compare the configured OHLCV fields and identify each field whose difference is outside the accepted tolerance.
- **FR-013**: Each material value discrepancy MUST report the timestamp, field name, benchmark value, candidate value, signed or directional difference, tolerance applied, and candidate source location when available.
- **FR-014**: Differences within the accepted tolerance MUST NOT be reported as material inconsistencies, but the report MUST expose enough aggregate information to audit how many differences were accepted.
- **FR-015**: When no tolerance profile is supplied, the system MUST use the default profile of one fractional quote-unit step or 0.01% of the benchmark value, whichever is greater, for Open, High, Low, and Close, and 5% of the benchmark value for Volume.
- **FR-016**: Users MUST be able to override absolute and relative tolerances independently for each compared OHLCV field, and MUST be able to disable comparison of a field explicitly.
- **FR-017**: A difference MUST be accepted when it is within the resolved absolute tolerance or within the resolved relative tolerance; the report MUST identify which tolerance rule accepted it when both rules are configured.
- **FR-018**: Tolerance evaluation MUST be deterministic, culture-independent, and consistent for equivalent numeric values regardless of textual formatting.
- **FR-019**: The system MUST reject invalid, negative, incomplete, or ambiguous tolerance configuration before reading input data, with an actionable diagnostic.
- **FR-020**: The comparison MUST treat benchmark and candidate values as potentially provider-dependent observations; it MUST NOT claim that every difference is an error when the difference is within the accepted tolerance.

#### Scores and Decision Support

- **FR-021**: The comparison report MUST include the candidate's independent six-metric validation scores and the benchmark's recorded six-metric scores, with their source and coverage clearly distinguished.
- **FR-022**: The comparison report MUST provide counts and rates for matched records, missing records, extra records, tolerated field differences, and material field discrepancies.
- **FR-023**: The benchmark-agreement score MUST be separate from the candidate's independent six-metric quality score and MUST NOT replace, merge with, or change it.
- **FR-024**: The benchmark-agreement score MUST be calculated from comparison coverage and material discrepancy outcomes, MUST state its formula and covered population, and MUST exclude unavailable populations explicitly.
- **FR-025**: The system MUST never present a comparison score as perfect when there is no meaningful comparison coverage or when the comparison is unavailable.
- **FR-026**: A comparison result MUST be advisory and MUST NOT mutate either dataset, automatically repair values, or silently change the candidate's independent validation result.

#### Reporting and Compatibility

- **FR-027**: Human-readable output MUST identify the benchmark, candidate, comparison coverage, both independent scores, the separate benchmark-agreement score, accepted differences, material inconsistencies, and the reason for any unavailable score.
- **FR-028**: Machine-readable output MUST expose benchmark identity, candidate identity, comparison configuration, coverage statistics, field-level discrepancies, tolerance decisions, independent scores, and the benchmark-agreement score as separate documented fields.
- **FR-029**: Existing validation output and behavior MUST remain unchanged when benchmark comparison is not requested.
- **FR-030**: Fatal validation, benchmark, or comparison failures MUST produce no partial success report or partial score and MUST state why comparison did not complete.
- **FR-031**: Equivalent benchmark and candidate inputs with equivalent configuration MUST produce deterministic discrepancy ordering, counts, scores, formatting, and substantive output.
- **FR-032**: The report MUST retain enough source references and benchmark identity information for a reviewer to trace every material inconsistency without relying on application internals.

### Key Entities *(include if feature involves data)*

- **Benchmark Dataset**: A named, immutable reference snapshot of a validated dataset, including exact source identity, instrument and context, validation results, scores, and establishment metadata.
- **Candidate Dataset**: The dataset being evaluated against a selected benchmark, including its source identity, validation results, scores, and comparison coverage.
- **Comparison Configuration**: The explicitly resolved rules for dataset compatibility, timestamp matching, fields compared, and acceptable numerical differences.
- **Comparison Coverage**: The counts and ranges describing benchmark records, candidate records, matched records, missing benchmark records, extra candidate records, and unusable records.
- **Field Discrepancy**: A timestamped difference between a benchmark value and candidate value, including field, values, difference, tolerance decision, and source references.
- **Benchmark Comparison Score**: A benchmark-relative agreement result kept separate from the candidate's independent six-metric quality score, with its formula, populations, tolerance profile, and unavailable state represented explicitly.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In 100% of benchmark-creation acceptance tests, the saved benchmark can be uniquely identified and reproduces the recorded source identity, context, six scores, and validation results.
- **SC-002**: In 100% of comparison tests with known timestamp sets, matched, missing, and extra record counts equal independently calculated values.
- **SC-003**: In 100% of field-comparison tests, every difference outside the configured tolerance is reported with the correct timestamp, field, values, difference, and tolerance decision, while every difference inside tolerance is excluded from material inconsistencies.
- **SC-004**: In 100% of tests using equivalent numeric values with different textual precision or formatting, the comparison produces the same tolerance decision.
- **SC-004a**: With the default forex-oriented profile, a one-fractional-step or 0.01%-or-less price difference and a 5%-or-less volume difference are accepted, while a deliberately larger difference is reported as material.
- **SC-005**: In 100% of runs without benchmark-comparison options, the existing validation output, findings, source bytes, and exit behavior remain unchanged.
- **SC-006**: In 100% of repeated comparisons using identical inputs and configuration, the substantive report is byte-identical and discrepancy ordering is unchanged.
- **SC-007**: In 100% of machine-readable contract tests, consumers can obtain benchmark and candidate identity, coverage, tolerance decisions, independent scores, and comparison scores without parsing prose.
- **SC-008**: In 100% of fatal or incompatible-input tests, no partial comparison score or success report is emitted and the diagnostic names the blocking reason.
- **SC-009**: At least 90% of task-based reviewers can identify the number of material inconsistencies, the number of tolerated differences, the comparison coverage, and the weakest candidate quality dimension within two minutes.
- **SC-010**: For a candidate containing one deliberately material opening-price difference, the report identifies the correct date and field without producing material mismatch findings for deliberately tolerated broker-level differences.

## Assumptions

- This feature extends the existing OHLCV validator and standalone dataset scoring capability; it does not replace either one.
- A benchmark is treated as an immutable reference snapshot for reproducibility. Any change to the source or scoring context creates a distinct benchmark identity rather than modifying history.
- Comparisons are detection-only and advisory. Automatic repair, interpolation, deduplication, and source mutation are out of scope.
- Instrument identity, timeframe, timestamp interpretation, market calendar, and covered range are required comparison context, even when the source file itself does not contain a symbol column.
- Numerical comparison is performed on the parsed numeric values rather than their original text representation.
- The default one-fractional-step price tolerance is resolved by inferring the fractional unit from the benchmark dataset's observed decimal precision across OHLC values (e.g., 5 decimal places → step of 0.00001). This avoids requiring an explicit instrument model while still providing a principled pip-level floor.
- The default 5% volume tolerance is a pragmatic broker-variation baseline, not a claim that volume is comparable across all providers; users may disable volume comparison or provide a stricter or wider field-specific rule.
- Existing six-metric definitions and score calculations remain authoritative for independent dataset quality scores.
- Human-readable reports remain in English and machine-readable reports remain versioned and deterministic.
- The system should expose accepted differences as aggregate evidence even when they are not material findings, because suppressed mismatches must remain auditable.

## Out of Scope

- Downloading benchmark or candidate data from the internet, provider authentication, or broker APIs.
- Selecting a benchmark automatically from a marketplace, ranking multiple benchmarks, or blending several reference datasets.
- Automatic correction of the candidate or benchmark data.
- Statistical forecasting, anomaly detection unrelated to benchmark comparison, or determining which broker is financially correct.
- Historical trend dashboards, long-term score storage, or portfolio-level quality aggregation.
- A graphical interface or interactive discrepancy exploration.
- Treating the benchmark as infallible or hiding benchmark defects from the user.
- Combining the candidate's independent quality score and benchmark-agreement score into one opaque composite score.