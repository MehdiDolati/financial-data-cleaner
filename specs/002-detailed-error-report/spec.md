# Feature Specification: Detailed Dataset Error Report

**Feature Branch**: `002-detailed-error-report`

**Created**: 2026-08-16

**Status**: Draft

**Input**: User description: "I want a detailed report of whatever errors found in the dataset file."

## Clarifications

### Session 2026-08-16

- Q: When a caller uses `--format json` without requesting a contract version, which JSON contract should the validator emit? → A: Emit v1 by default; require explicit opt-in for v2.
- Q: When v2 JSON is selected and validation ends fatally before a successful report exists, where and in what form should the diagnostic be emitted? → A: Emit structured v2 fatal JSON on stderr; leave stdout and the report destination empty.
- Q: What memory guarantee should detailed reporting provide as the number of findings grows? → A: Keep memory bounded independently of total input-row and finding counts.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Review Every Detected Problem (Priority: P1)

As a trader, quant researcher, or data engineer, I want one detailed report that
lists every data-quality problem found during a successful dataset scan so that I
can judge whether the file is safe to use without repeating the validation or
searching the source file blindly.

**Why this priority**: The report's primary value is completeness. A summary count
without the corresponding problem details does not tell a user what must be
investigated or corrected.

**Independent Test**: Validate a structurally processable dataset containing known
examples from all six existing finding categories and verify that the report lists
every expected finding, identifies the completed checks, and reconciles every
category total with its detailed entries.

**Acceptance Scenarios**:

1. **Given** a dataset containing missing candles, duplicate records, invalid OHLC values, closed-market records, time gaps, and malformed rows, **When** a detailed report is requested, **Then** the report includes a summary and complete details for all six categories without silently omitting or sampling findings.
2. **Given** a successful validation in which one category has no findings, **When** the detailed report is produced, **Then** that category is shown with a zero count and no fabricated detail entries.
3. **Given** a dataset with findings in more than one category, **When** the report is reviewed, **Then** the report does not describe the sum of overlapping category counts as a count of unique root causes.
4. **Given** a successful scan, **When** the report is produced, **Then** it explicitly states that the scan completed and that the finding list is complete for the checks that ran.

---

### User Story 2 - Locate and Understand Each Problem (Priority: P2)

As a person responsible for data quality, I want each finding to show where the
problem occurred, what rule failed, the evidence observed, and a practical next
action so that I can investigate the vendor data or make a deliberate correction
outside the validator.

**Why this priority**: Complete findings become actionable only when a user can
trace them to the source and understand the specific defect without reverse
engineering a generic message.

**Independent Test**: Use a fixture containing one carefully controlled example
of each category and verify the category-specific location, evidence, explanation,
and remediation fields against the source data.

**Acceptance Scenarios**:

1. **Given** a row that violates multiple OHLC rules, **When** it is reported, **Then** one invalid-OHLC finding identifies the physical source line, timestamp, all violated rules, and the relevant observed values.
2. **Given** a conflicting duplicate group, **When** it is reported, **Then** the finding lists every participating source line and identifies every OHLCV field whose values differ across those rows.
3. **Given** a contiguous run of missing candles, **When** it is reported, **Then** users can see the gap boundaries, expected interval, number of missing candles, and the missing timestamps associated with that gap.
4. **Given** a malformed row with more than one independently detectable field error, **When** it is reported, **Then** the row appears once with each detected field error and clearly states which quality checks could not be applied to that row.

---

### User Story 3 - Diagnose an Incomplete Validation (Priority: P2)

As a user whose file cannot be fully validated, I want a detailed fatal diagnostic
that explains where processing stopped and which checks did not run so that I do
not mistake an incomplete scan for a clean or complete report.

**Why this priority**: A structurally invalid or unreadable dataset is itself a
critical dataset-file problem. Fail-safe reporting must make incompleteness
unmistakable while avoiding untrustworthy quality counts.

**Independent Test**: Attempt validation with unreadable, invalid-encoding,
structurally inconsistent, and unresolved-timeframe fixtures and verify that each
produces a distinct fatal diagnostic, no successful data-quality report, and no
claim that all checks completed.

**Acceptance Scenarios**:

1. **Given** a file whose structure prevents reliable record processing, **When** validation stops, **Then** a fatal diagnostic identifies the failure stage, reason, source location when known, suggested next action, and checks that were not completed.
2. **Given** a fatal ingestion failure after some rows were observed, **When** the diagnostic is produced, **Then** partial observations are not presented as final category totals or as an exhaustive finding list.
3. **Given** a failure unrelated to dataset content, such as an invalid command option or an unwritable report destination, **When** it is reported, **Then** it is identified as an operational/configuration failure rather than mislabeled as a dataset defect.
4. **Given** v2 JSON output was selected, **When** validation ends fatally, **Then** stderr contains exactly one structured v2 fatal diagnostic and stdout and the report destination contain no report payload.

---

### User Story 4 - Consume and Compare Reports Reliably (Priority: P3)

As a pipeline owner or auditor, I want detailed report data to be structured,
deterministic, and self-describing so that I can archive it, compare repeated runs,
and consume each field without parsing human prose.

**Why this priority**: Automation and auditability increase the report's long-term
value, but the human investigation workflow remains independently useful first.

**Independent Test**: Run the same dataset and validation configuration repeatedly,
consume the machine-readable reports using documented fields, and verify identical
content, ordering, source identity, check status, category counts, and finding
details.

**Acceptance Scenarios**:

1. **Given** the same input bytes and validation configuration, **When** detailed reports are generated repeatedly, **Then** their substantive content and finding order are identical.
2. **Given** machine-readable output, **When** a consumer reads a finding, **Then** category, location, evidence, rule violations, relationships, and suggested action are available as distinct data rather than only inside a message.
3. **Given** a detailed report written to a destination, **When** report production cannot complete, **Then** no partial artifact is presented as a complete report.

### Edge Cases

- A clean or empty/header-only dataset processed with a valid timeframe override has no findings; the detailed report still records check completion and an unambiguous clean result.
- A finding may have no physical source line, such as an expected but missing candle; the report uses the expected timestamp and neighboring-record context rather than inventing a line number.
- A malformed row may have a valid timestamp but invalid values, or may have no usable timestamp at all; the report distinguishes these cases and states whether the expected candle slot was reserved.
- One source row may violate several OHLC rules but contributes only once to the invalid-OHLC category count; all violations remain visible within that one finding.
- One duplicate group may contain many rows and may contribute more than one to the duplicate count; all participating rows remain part of one group detail.
- Missing-candle and time-gap entries intentionally describe related aspects of the same absence; the report links them and does not imply they are independent root causes.
- A closed-market record may also contain invalid OHLC values; both independently completed checks report their findings and reference the same source row.
- Source values may contain control characters, delimiters, quotes, or very long text; they must remain attributable without being able to corrupt report structure or masquerade as report content.
- A dataset may contain more findings than fit comfortably on screen; detailed output remains complete and is not silently truncated, sampled, or replaced by only the first N findings.
- A source line number may exceed common 32-bit limits; report traceability must preserve the full positive physical line number.
- A report destination may be the same path as the input dataset; the operation must be rejected before any source data can be overwritten.
- If processing loses the ability to trust the dataset structure, the validator stops safely; the fatal diagnostic must state that additional defects may exist and were not evaluated.

## Requirements *(mandatory)*

### Functional Requirements

#### Report Scope and Status

- **FR-001**: The system MUST provide a detailed-report mode for one dataset validation run while retaining the existing concise summary behavior for callers that do not request detailed human-readable output.
- **FR-002**: A detailed report for a structurally successful scan MUST include every finding produced by each completed validation check; it MUST NOT silently truncate, sample, aggregate away, or omit individual findings.
- **FR-003**: Every report outcome MUST have exactly one unambiguous status: `Clean` when every applicable check completed and all category counts are zero, `FindingsDetected` when every applicable check completed and one or more category counts are nonzero, or `Fatal` when any applicable check could not be completed or a trustworthy full scan could not otherwise be produced.
- **FR-004**: A successful report MUST state that its finding set is complete for the checks listed as completed. A fatal diagnostic MUST state that it is incomplete and MUST NOT claim clean data or final data-quality totals.
- **FR-005**: The report MUST distinguish data-quality findings, fatal dataset-ingestion failures, and operational/configuration failures so users do not mistake one class for another.
- **FR-006**: Detailed reporting MUST NOT change, repair, delete, reorder, or overwrite any content in the source dataset.

#### Run Context and Reconciliation

- **FR-007**: A successful detailed report MUST identify the source using a safe file name, byte size, and deterministic content fingerprint, without exposing an absolute source path by default.
- **FR-008**: A successful detailed report MUST record the resolved validation context that materially affects results, including timeframe, market calendar/profile, timestamp interpretation, source time-zone offset, delimiter, header handling, and the evaluated date range when available.
- **FR-009**: A successful detailed report MUST state the number of physical data rows examined, accepted rows, and malformed rows. The optional header is not a data row; every other physical record read from the dataset is an examined row, and examined rows MUST equal accepted rows plus malformed rows.
- **FR-010**: Every report outcome MUST list each established validation check with its status (`Completed`, `NotApplicable`, or `NotCompleted`) and an explanation for any check not marked `Completed`. A successful report MUST contain no `NotCompleted` check; any applicable check that cannot complete makes the outcome fatal.
- **FR-011**: A successful detailed report MUST preserve the six established summary categories and their meanings: missing candles, duplicate records, invalid OHLC, closed-market records, time gaps, and malformed rows.
- **FR-012**: For each category, the report MUST distinguish the category's summary count from the number of detailed entries when one entry can contribute more than one count, and each entry MUST expose its positive count contribution.
- **FR-013**: The report MUST avoid presenting a single undifferentiated "total errors" value as a count of unique problems because category counts can overlap or describe related conditions. If a cross-category sum is shown, it MUST be explicitly labeled as a sum of category counts, not unique root causes.
- **FR-014**: For every category, the sum of detailed-entry count contributions MUST exactly equal its summary count. A report that cannot reconcile MUST fail report completion rather than present contradictory results.

#### Common Finding Detail

- **FR-015**: Every detailed finding MUST include a deterministic reference unique within the report, category, concise title, plain-language explanation, count contribution, evidence, and a suggested investigation or remediation action that does not modify the source automatically.
- **FR-016**: Every detailed finding MUST identify its source location using all applicable values: physical line number(s), normalized UTC timestamp or expected UTC timestamp, and original timestamp text when it came from a source row. Fields that do not apply MUST be explicitly absent rather than populated with invented values.
- **FR-017**: Every timestamp in finding details MUST be unambiguous and normalized to UTC. Where source-time interpretation is useful for investigation, it MUST be presented in addition to, not instead of, UTC.
- **FR-018**: Evidence MUST be represented as explicit named data in machine-readable output. Consumers MUST NOT need to parse the plain-language explanation to recover source lines, observed values, violated rules, gap boundaries, or related finding references.
- **FR-019**: Source-derived text and values MUST be represented so they cannot alter report structure, inject false report entries, or make neighboring content ambiguous.
- **FR-020**: Findings MUST use one deterministic order across supported report representations: established category order, then applicable UTC timestamp, then applicable physical source line, with a documented deterministic tie-breaker for otherwise equal entries.
- **FR-021**: When findings have a deterministic relationship, such as missing candles belonging to a time gap, the report MUST expose cross-references in both directions without merging away either established category.

#### Category-Specific Detail

- **FR-022**: A missing-candle detail MUST include the expected UTC timestamp, expected timeframe, related time-gap reference, and nearest preceding and following observed timestamps when available. It MUST state that no physical source line exists for an absent record.
- **FR-023**: A time-gap detail MUST include the first and last missing expected timestamps, expected timeframe, number of missing candles, elapsed gap span, nearest preceding and following observed records when available, and references to all missing-candle details in that gap.
- **FR-024**: A duplicate-record detail MUST include the shared UTC timestamp, classification as exact or conflicting, count contribution, every participating physical source line, and the relevant OHLCV values for each row. A conflicting group MUST explicitly identify every field whose values differ.
- **FR-025**: An invalid-OHLC detail MUST include the physical source line, timestamp, observed Open, High, Low, Close, and Volume values, plus every violated OHLC or volume rule for that row. Multiple violations on the same row MUST remain one category entry with a count contribution of one.
- **FR-026**: A closed-market-record detail MUST include the physical source line, timestamp, selected market calendar/profile, and the applicable closed-session boundary or rule that caused the timestamp to be classified as closed.
- **FR-027**: A malformed-row detail MUST include the physical source line, parseable timestamp when available, each independently detectable field-level parsing error, the original offending value, a specific reason, whether the expected candle slot was reserved, and the checks that could not be applied to the row.
- **FR-028**: Detecting one malformed field MUST NOT prevent the report from recording other independently detectable malformed fields in the same structurally readable row. The row MUST still contribute exactly one to the malformed-row summary count.

#### Fatal Diagnostics and Representations

- **FR-029**: When a dataset cannot be fully scanned, the system MUST produce a fatal diagnostic distinct from a successful data-quality report. It MUST include a stable failure code, failure class, processing stage, plain-language reason, source location when known, corrective guidance, and the checks not completed.
- **FR-030**: A fatal diagnostic MAY include explicitly labeled observations made before failure for troubleshooting, but MUST NOT present them as exhaustive findings or final category totals.
- **FR-031**: If report generation itself fails, the system MUST provide an actionable operational diagnostic and MUST NOT leave or label a partial report as complete.
- **FR-032**: The detailed report MUST be available in both the established verbose human-readable text representation and an explicitly selected v2 machine-readable report representation. Both detailed representations MUST carry equivalent substantive information, while presentation may differ for readability.
- **FR-033**: Human-readable output MUST group run context, scan coverage, category summaries, and category-specific finding details under clear labels so a user can navigate a large report without interpreting machine field names.
- **FR-034**: The v2 machine-readable output MUST expose report status, run context, check status, reconciliation data, and category-specific evidence as documented fields under an explicitly versioned contract.
- **FR-035**: Existing summary category names, count meanings, successful-run exit behavior, and clean/findings distinction MUST remain compatible with the established validator behavior.
- **FR-036**: If a report is written to a destination, completion MUST be all-or-nothing, and the system MUST reject any destination that resolves to the source dataset before source content can be changed.
- **FR-037**: A caller that requests JSON without selecting a contract version MUST receive the existing v1 contract unchanged. The v2 detailed JSON contract MUST require explicit opt-in so strict v1-schema consumers do not receive incompatible fields.
- **FR-038**: When v2 JSON is selected and validation ends fatally, the system MUST emit exactly one structured v2 fatal diagnostic to stderr and MUST emit no report payload to stdout or the selected report destination. The diagnostic MUST expose the fields required by FR-029 as documented data rather than only as prose.

### Key Entities *(include if feature involves data)*

- **Detailed Validation Report**: The complete outcome of one successful scan, including source identity, resolved validation context, scan coverage, category summaries, detailed findings, reconciliation data, and overall status.
- **Report Outcome**: The classification of the run as clean, findings detected, or fatal; determines whether quality findings are complete and trustworthy.
- **Source Identity**: Stable identification of the exact dataset bytes using a safe name, byte size, and content fingerprint without requiring disclosure of an absolute path.
- **Check Execution**: Records one validation check's name, completion status, and reason when the check was not completed or did not apply.
- **Detailed Finding**: A deterministic, report-unique description of one detected issue or issue group with common traceability, count contribution, evidence, explanation, and guidance.
- **Finding Evidence**: Category-specific observed and expected values used to explain a finding without requiring consumers to parse prose.
- **Finding Relationship**: A deterministic link between related findings, particularly a time gap and its constituent missing candles.
- **Fatal Diagnostic**: A non-success outcome that identifies why a trustworthy full validation could not be completed and which checks remain unevaluated.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Across an acceptance corpus containing at least one example of every finding category and every category-specific edge case in this specification, 100% of deliberately injected findings and their expected evidence appear in the detailed report.
- **SC-002**: In 100% of successful-report acceptance tests, every category summary exactly equals the sum of its detailed-entry count contributions, and all row totals reconcile according to their documented meanings.
- **SC-003**: In a task-based review with representative users, at least 90% can identify the affected source record or expected timestamp, explain the failed rule, and name the suggested next action for a sampled finding within two minutes without consulting application source code.
- **SC-004**: Across fatal fixtures for unreadable input, invalid encoding, invalid structure, and unresolved validation prerequisites, 100% produce an unambiguous incomplete/fatal diagnostic with failure stage, reason, guidance, and unevaluated checks, and 0% are presented as clean or complete quality reports. For v2 JSON runs, 100% of these diagnostics are parseable as the documented fatal contract from stderr while stdout and the report destination remain empty.
- **SC-005**: Repeated reports from identical input bytes and identical validation context have 100% identical substantive fields and finding order.
- **SC-006**: A successful validation producing at least 100,000 detailed findings reports all of them without silent truncation while process memory remains bounded independently of total input-row and finding counts; an interrupted report write leaves no artifact identified as complete.
- **SC-007**: Machine-readable consumers can obtain every required location, rule, evidence, relationship, and count-contribution value from documented fields in 100% of contract tests without parsing human-readable messages.
- **SC-008**: In all source-protection tests, including a report destination that aliases the input file, the source dataset remains byte-for-byte unchanged.

## Assumptions

- This feature extends the completed OHLCV CSV data-quality validator defined in `specs/001-ohlcv-data-quality-validator`; it does not replace ingestion or introduce a second validation engine.
- "Errors" means the six established data-quality finding categories plus fatal conditions that prevent a full dataset scan. It does not imply a new severity score or new statistical validation rules.
- The existing concise text summary remains the default. Detailed text is explicitly requested through the validator's established verbose reporting option. Existing JSON v1 remains the default for an unversioned JSON request, while detailed machine-readable output requires explicit selection of the v2 contract.
- Reports are in English for this feature, consistent with the existing validator scope.
- Source timestamps and physical line numbers can be reported only when they were recoverable; expected-but-absent records have timestamps but no source lines.
- Completeness applies to all findings discoverable during a structurally successful scan. Once safe parsing is no longer possible, fail-safe behavior takes precedence over continuing to search for additional defects.
- Suggested actions are advisory investigation steps. The validator remains detection-only and never repairs the dataset.
- The detailed report depends on the existing category definitions, market-calendar behavior, timeframe resolution, normalized UTC timestamps, and clean/findings/fatal process outcomes remaining available from the validator.

## Out of Scope

- New data-quality checks, anomaly detection, severity ranking, quality scoring, or a claim that one finding is financially more important than another.
- Automatic correction, deduplication, interpolation, deletion, or rewriting of source data.
- Combining multiple dataset files into one report or comparing findings across different datasets.
- Interactive, graphical, or web-based report exploration.
- Custom report templates, localization, or user-authored remediation text.
- Continuing validation after input structure becomes untrustworthy merely to collect more possible errors.