# Feature Specification: Dataset Quality Scoring

**Feature Branch**: `003-dataset-quality-scoring`

**Created**: 2026-08-18

**Status**: Draft

**Input**: User description: "we want to score every dataset based on the 6 metrics implemented in the system so that for example a dataset would receive a full score on missing candles but lower scores on say invalid OHLCV data. There need also to be a average score for a dataset."

## Clarifications

### Session 2026-08-18

- Q: How should each of the six metric counts become a per-metric score? → A: A 0–100 linear scale, `100 × (1 − defect rate)`.
- Q: Which population should each metric's defect rate be measured against? → A: The natural population for each metric; time gaps are measured against expected candles.
- Q: How should the single dataset average be combined and weighted? → A: A weighted mean whose default weights are equal for all six metrics, with a caller-supplied override.
- Q: How should a weight override be validated, and how are undefined metrics treated? → A: An override MUST list all six metrics explicitly, a weight of zero is allowed, an inapplicable metric is dropped and the remaining weights are renormalised, and an applicable metric with a zero denominator receives no score.
- Q: Where should scores appear and should they change the exit code? → A: Scoring is opt-in; scores appear in text output after the six summary lines and in the detailed v2 machine-readable report only; the v1 contract is untouched and exit codes are unchanged.
- Q: What happens when scoring is requested alongside the v1 machine-readable contract, what precision is used, and how is an unavailable average shown? → A: The combination fails fast as a configuration conflict, scores are reported to two decimal places, and an unavailable average is stated explicitly with its reason.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See Which Quality Dimension Is Weak (Priority: P1)

As a trader, quant researcher, or data engineer, I want each of the six established
quality metrics scored separately for a dataset so that I can see at a glance that a
file is, for example, perfect on missing candles but poor on invalid OHLC values,
instead of reading six raw counts whose severity depends on the size of the file.

**Why this priority**: This is the core of the request. Six independent scores are
what turn counts into a comparable judgement, and they are useful on their own even
before any combined figure exists.

**Independent Test**: Score a dataset with a known row count and deliberately
injected defects in some categories but not others, then verify that untouched
categories score a full 100.00 while affected categories score exactly the value
their defect rate implies.

**Acceptance Scenarios**:

1. **Given** a dataset with zero defects in every category, **When** scoring is requested, **Then** every applicable metric scores 100.00.
2. **Given** a dataset with no missing candles but a known number of invalid-OHLC rows, **When** scoring is requested, **Then** the missing-candle metric scores 100.00 and the invalid-OHLC metric scores below 100.00 in proportion to its defect rate.
3. **Given** any scored dataset, **When** the scores are reviewed, **Then** each metric shows its score, the count and population the score was derived from, and its scale, so the number can be checked by hand.
4. **Given** a dataset whose defect rate in one category is total, **When** scoring is requested, **Then** that metric scores 0.00 and no metric ever falls below 0.00 or exceeds 100.00.

---

### User Story 2 - Judge a Dataset by One Average Score (Priority: P1)

As someone choosing between candidate data files, I want one average score for the
dataset so that I can rank or accept files quickly, and drill into the per-metric
scores only when the average looks unacceptable.

**Why this priority**: The user explicitly requires an average. It is jointly
essential with the per-metric scores, and it is the value most likely to be used for
a fast accept/reject decision.

**Independent Test**: Score datasets with known per-metric scores and verify the
average equals the documented weighted mean of exactly the metrics that were scored,
including when some metrics are not applicable.

**Acceptance Scenarios**:

1. **Given** a dataset where all six metrics are scored, **When** the average is produced, **Then** it equals the mean of those six scores under equal default weights.
2. **Given** a dataset where one metric is not applicable, **When** the average is produced, **Then** the average covers only the scored metrics, the inapplicable metric is reported as not applicable with a reason, and it is not credited as a perfect score.
3. **Given** a flawless dataset, **When** the average is produced, **Then** the average is 100.00.
4. **Given** any average, **When** it is reported, **Then** it states how many metrics it covers so a reader cannot mistake it for a six-metric average when fewer metrics were scored.

---

### User Story 3 - Weight the Metrics for My Own Priorities (Priority: P2)

As a user whose tolerance differs by defect type, I want to supply my own weight for
each of the six metrics so that the average reflects what matters for my intended use
of the data, while the default stays neutral for everyone else.

**Why this priority**: Per-metric and default-average scoring already deliver value;
weighting refines the average and is therefore valuable but not required first.

**Independent Test**: Score the same dataset with default weights and with a supplied
weight set, and verify only the average changes, that it matches the weighted mean by
hand, and that invalid or incomplete weight input is rejected before scanning begins.

**Acceptance Scenarios**:

1. **Given** no weight override, **When** a dataset is scored, **Then** all six metrics are weighted equally and the resolved weights are reported.
2. **Given** a complete weight set for all six metrics, **When** a dataset is scored, **Then** the average equals the weighted mean of the scored metrics using those weights, and the per-metric scores are unchanged.
3. **Given** a weight override that omits at least one metric, contains an unknown metric name, contains a negative or non-numeric value, or is otherwise unparseable, **When** the run starts, **Then** it fails fast with an actionable message before the dataset is read, and no report is produced.
4. **Given** a weight of zero for a metric, **When** the dataset is scored, **Then** that metric is still scored and reported but contributes nothing to the average.

---

### User Story 4 - Trust, Reproduce, and Automate the Scores (Priority: P3)

As a pipeline owner or auditor, I want scores to be deterministic, self-describing,
and available as discrete machine-readable fields so that I can archive them, compare
runs over time, and rely on them without parsing prose or re-deriving the arithmetic.

**Why this priority**: Automation and auditability increase long-term value, but the
human judgement workflow is independently useful first.

**Independent Test**: Score identical input bytes with an identical configuration
repeatedly and confirm identical scores, then read every score, weight, population,
and applicability value from documented machine-readable fields alone.

**Acceptance Scenarios**:

1. **Given** identical input bytes and identical scoring configuration, **When** the dataset is scored repeatedly, **Then** every reported score is identical, including its formatting.
2. **Given** machine-readable output, **When** a consumer reads the scoring data, **Then** each metric score, its count, population, applicability, resolved weight, normalised weight, and the average are separate documented fields.
3. **Given** a scored run, **When** the exit behaviour is observed, **Then** the score never changes the exit code, the six summary counts, the findings, or the source dataset.
4. **Given** a run that ends fatally, **When** scoring was requested, **Then** no scores are produced and the fatal diagnostic makes clear that scoring did not occur.

### Edge Cases

- A dataset with zero accepted rows leaves the duplicate, invalid-OHLC, and closed-market populations empty, so those metrics have an undefined rate and receive no score rather than a perfect one.
- A dataset with zero examined rows leaves the malformed-row population empty, so that metric receives no score.
- A market profile that is always open, such as crypto, never runs the closed-market check, so that metric is not applicable and is excluded from the average.
- A configuration in which sequence checks cannot run leaves missing candles and time gaps not applicable, so both are excluded from the average.
- Every metric may be unscored at once, in which case the average is explicitly unavailable with a reason and is never shown as 0.00 or 100.00.
- A weight set may place all non-zero weight on metrics that turn out to be inapplicable, which also leaves the average unavailable with a reason.
- A weight set of all zeros gives the average no basis at all and is rejected before scanning begins.
- A single time gap spanning many candles and many single-candle gaps can yield the same missing-candle score but different time-gap scores; both metrics are reported separately and neither is merged away.
- Very large counts and populations must not lose precision or overflow, and repeated runs must produce byte-identical score text.
- A defect rate that computes outside the range from 0 to 1 indicates an internal inconsistency and must fail rather than be silently clamped into range.
- Requesting scores alongside the frozen v1 machine-readable contract has nowhere compatible to place them and is rejected rather than silently ignored.
- A closed-market row that also has invalid OHLC values lowers both metric scores independently, because the two checks are independent.

## Requirements *(mandatory)*

### Functional Requirements

#### Scope and Activation

- **FR-001**: The system MUST be able to score a single dataset validation run across exactly the six established quality metrics — missing candles, duplicate records, invalid OHLC, closed-market records, time gaps, and malformed rows — without adding, removing, renaming, or redefining any metric.
- **FR-002**: Scoring MUST be opt-in. When it is not requested, all existing output, counts, findings, and exit behaviour MUST remain unchanged.
- **FR-003**: Scoring MUST NOT change, repair, reorder, or overwrite any content in the source dataset, and MUST NOT alter the six summary counts, the findings, the finding order, or the process exit code.
- **FR-004**: Scores MUST be derived only from the counts, populations, and check statuses already established by the existing validation run; scoring MUST NOT introduce a new data-quality check or re-scan the dataset.
- **FR-005**: When a run ends fatally and no trustworthy complete report exists, the system MUST NOT produce any score, and the fatal diagnostic MUST make clear that scoring did not occur.

#### Per-Metric Score Calculation

- **FR-006**: Every metric score MUST be expressed on a 0-to-100 scale where 100 is flawless, calculated as `100 × (1 − defect rate)`, and MUST be reported with its scale so the direction of the scale is unambiguous.
- **FR-007**: Each metric's defect rate MUST be its established summary count divided by the population in which that defect can occur, as follows:
  - missing candles ÷ expected candles in the evaluated range
  - time gaps ÷ expected candles in the evaluated range
  - duplicate records ÷ accepted rows
  - invalid OHLC ÷ accepted rows
  - closed-market records ÷ accepted rows
  - malformed rows ÷ examined rows
- **FR-008**: Every metric score MUST report the exact count and population value used to produce it, so any score can be independently recalculated from the report alone.
- **FR-009**: A defect rate MUST fall between 0 and 1 inclusive. A rate outside that range indicates an internal inconsistency and MUST fail the run with an actionable diagnostic rather than be silently clamped, and no score MUST ever be reported below 0.00 or above 100.00.
- **FR-010**: Score calculations MUST be exact and free of accumulated rounding drift, and MUST NOT vary with the host machine's regional or locale settings.
- **FR-011**: Scores MUST be reported to exactly two decimal places using half-away-from-zero rounding and a culture-invariant representation. The average MUST be computed from unrounded metric scores and rounded only for presentation.

#### Applicability and Undefined Scores

- **FR-012**: A metric whose underlying check did not run for the selected configuration MUST be reported as not applicable, with the reason, and MUST NOT receive a score or be credited as flawless.
- **FR-013**: A metric whose population is zero has an undefined defect rate and MUST be reported as not scored, with the reason, and MUST NOT be credited as flawless.
- **FR-014**: A metric that is not scored under FR-012 or FR-013 MUST be excluded from the average, and its exclusion MUST be visible in the report rather than inferred from a missing value.
- **FR-015**: Every one of the six metrics MUST appear in a scoring report in the established category order with exactly one state: scored with a value, not applicable, or not scored, each carrying its reason when it has no value.

#### Average Score

- **FR-016**: The system MUST report one average score for the dataset on the same 0-to-100 scale, calculated as the weighted mean of the scored metrics only: the sum of each scored metric's score multiplied by its weight, divided by the sum of those same weights.
- **FR-017**: The default weighting MUST treat all six metrics as equally important.
- **FR-018**: The average MUST state how many metrics it covers and which metrics were excluded, so it cannot be mistaken for a six-metric average when fewer metrics were scored.
- **FR-019**: When no metric is scored, or when the weights of all scored metrics sum to zero, the average MUST be reported as unavailable together with the reason, and MUST NOT be reported as 0.00, as 100.00, or as any substitute value.
- **FR-020**: The average MUST NOT be presented as a count of problems or as a measure of unique root causes, and the existing prohibition on presenting a single undifferentiated total as unique problems MUST remain intact.

#### Caller-Supplied Weights

- **FR-021**: Users MUST be able to override the weighting through an explicit command-line option so the average reflects their own priorities.
- **FR-022**: A weight override MUST specify a weight for all six metrics explicitly. An override that omits any metric MUST be rejected; the system MUST NOT silently substitute a default weight for a missing metric.
- **FR-023**: Each supplied weight MUST be a non-negative number. A weight of zero MUST be accepted and MUST mean the metric still receives and reports its own score while contributing nothing to the average.
- **FR-024**: The system MUST reject, before the dataset is read and without producing a report, any weight input that is unparseable, negative, non-numeric, references an unknown metric, names a metric more than once, omits a metric, or sets every weight to zero. Each rejection MUST state the specific problem and the accepted form.
- **FR-025**: The report MUST echo the resolved weight of every metric together with its normalised share of the weights actually used for the average, so an average can be recalculated from the report alone.
- **FR-026**: Weights MUST affect only the average. They MUST NOT change any per-metric score, count, finding, or applicability state.
- **FR-027**: Scoring MUST be deterministic: identical input bytes with an identical validation and weighting configuration MUST produce identical scores, identical weight values, and identical formatting on every run.

#### Reporting Surfaces

- **FR-028**: When scoring is requested with human-readable text output, the scoring information MUST appear after the six established summary lines, leaving those six lines unchanged in content, order, and format.
- **FR-029**: When scoring is requested with the detailed v2 machine-readable report, scores MUST be exposed as documented fields under that versioned contract, covering each metric's score, count, population, state and reason, resolved and normalised weight, the average, the metric count the average covers, and the reason when the average is unavailable.
- **FR-030**: The v1 machine-readable contract MUST remain unchanged. Scoring MUST NOT add, remove, or alter any v1 field.
- **FR-031**: Requesting scores together with the v1 machine-readable contract MUST fail fast as a configuration conflict with a message naming the option needed to obtain scores, because the frozen contract has no compatible place for them. Scoring MUST NOT be silently ignored.
- **FR-032**: Human-readable scoring output MUST be labelled clearly enough that a reader can identify each metric, its score, its state, and the average without consulting field names or documentation.
- **FR-033**: Both supported scoring representations MUST convey equivalent substantive scoring information, though presentation may differ for readability.
- **FR-034**: Scoring output MUST make the relationship between a score and its established metric explicit, so a reader can always trace a score back to the count it was derived from.

### Key Entities *(include if feature involves data)*

- **Metric Score**: The scored result for one of the six established metrics, holding its score on the 0-to-100 scale, the count and population it was derived from, and its state as scored, not applicable, or not scored with a reason.
- **Metric Population**: The number of opportunities in which a given metric's defect could have occurred — expected candles, accepted rows, or examined rows — used as the denominator of that metric's defect rate.
- **Score Weighting**: The resolved weight of each metric, its source as default or caller-supplied, and its normalised share of the weights used for the average.
- **Dataset Score**: The dataset's single average score, the number and identity of the metrics it covers, the metrics excluded from it, and the reason when no average is available.
- **Score Report Section**: The complete scoring result attached to one successful validation run, comprising the six metric scores, the resolved weighting, and the dataset average.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Across an acceptance corpus with independently known counts and populations, 100% of per-metric scores equal the value calculated by hand from the documented formula, and every metric with zero defects scores exactly 100.00.
- **SC-002**: In 100% of scored acceptance runs, the reported average equals the weighted mean of exactly the metrics reported as scored, recalculated by hand from the counts, populations, and weights present in the report alone.
- **SC-003**: In 100% of runs where a metric is not applicable or its population is zero, that metric is reported with an explicit state and reason, is excluded from the average, and is never shown as a perfect score.
- **SC-004**: 100% of invalid weight inputs — omitted metric, unknown metric, duplicate metric, negative value, non-numeric value, unparseable input, and all-zero weights — are rejected before any dataset content is read, with a message naming the specific problem, and produce no report.
- **SC-005**: Repeated scoring of identical input bytes with an identical configuration yields byte-identical scoring output in 100% of determinism tests.
- **SC-006**: In 100% of runs where scoring is not requested, output is byte-identical to the equivalent run before this feature existed, and in 100% of scored runs the six summary counts, findings, finding order, and exit code are unchanged from the equivalent unscored run.
- **SC-007**: Machine-readable consumers obtain every score, count, population, state, reason, resolved weight, normalised weight, and the average from documented fields in 100% of contract tests without parsing human-readable text.
- **SC-008**: In 100% of tests combining scoring with the v1 contract, the run fails with an actionable configuration-conflict message, and in 100% of v1 contract tests the v1 output remains unchanged.
- **SC-009**: In 100% of fatal-run tests where scoring was requested, no score is emitted and the diagnostic states that scoring did not occur.
- **SC-010**: In all source-protection tests, the source dataset remains byte-for-byte unchanged when scoring is requested.
- **SC-011**: A user reviewing a scored report can identify the weakest metric, state the count and population behind its score, and name the average's coverage within two minutes without consulting application source code, in at least 90% of task-based review attempts.

## Assumptions

- This feature extends the existing OHLCV CSV data-quality validator and its detailed reporting; it neither replaces validation nor introduces a second validation engine.
- The six metric definitions, their counting rules, their check applicability states, the expected-candle sequence, and the accepted, examined, and malformed row totals remain available unchanged from the existing validator, and scoring consumes them rather than recomputing them.
- "Expected candles" means the count of expected open-market timestamps the validator already derives for the evaluated range using the resolved timeframe and market calendar.
- "Accepted rows" and "examined rows" carry their established meanings, where examined rows equal accepted rows plus malformed rows and an optional header row is not a data row.
- A higher score always means better quality, and 100 always means no detected defects in that metric's population.
- Scores describe detected defect rates only. They do not rank the financial importance of one category against another, and the default equal weighting is deliberately neutral rather than an assertion that all defect types are equally harmful.
- Because the average covers only scored metrics, averages from datasets whose applicable metric sets differ are not directly comparable; the reported metric coverage exists so a reader can detect that case.
- Scoring is reported in English, consistent with the existing validator scope.
- Reports remain detection-only and advisory; a score never triggers a repair, a retry, or a change in process outcome.

## Out of Scope

- New data-quality checks, anomaly detection, statistical outlier detection, or any additional metric beyond the six established categories.
- Letter grades, pass/fail badges, score thresholds, or failing a run because a score is too low.
- Automatic correction, deduplication, interpolation, or any modification of source data based on a score.
- Scoring or comparing multiple datasets in one run, ranking files against each other, or tracking score history across runs.
- Persisting, aggregating, or trending scores outside the single run's report.
- Adding scoring to the frozen v1 machine-readable contract.
- Weighting presets, named scoring profiles, per-metric sensitivity curves, or configurable score scales.
- Interactive, graphical, or web-based score exploration, and localisation of score labels.
