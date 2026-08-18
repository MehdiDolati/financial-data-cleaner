# Feature Specification: Web Application Integration

**Feature Branch**: `005-web-application-integration`

**Created**: 2026-08-18

**Status**: Draft - Ready for Planning

**Input**: User description: "in this feature we want to use the business logic in a web application instead of command line. What I expect is that the integration goes just fine and without any tweeks all the functionality migrate to a web site. Be mindful about code style and concention compatibility with the website's code (being provided later). you can change the source code of web site to comply with our constituion"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run Dataset Validation in the Web Application (Priority: P1)

As a trader, quant researcher, or data engineer, I want to provide an OHLCV
dataset through a website and run the existing validation workflow so that I can
review data quality without using a command-line tool.

**Why this priority**: Moving the primary validation workflow to the website is
the minimum useful migration and establishes whether the existing business
logic can be driven by a different front end.

**Independent Test**: Upload a known clean fixture and a fixture containing
known findings, select the equivalent validation options in the website, run
each validation independently, and compare the web result with the established
command-line result.

**Acceptance Scenarios**:

1. **Given** a supported dataset and valid validation options, **When** the user
   starts validation, **Then** the website completes the same validation rules
   as the existing workflow and presents a result with the same six summary
   categories and values.
2. **Given** a dataset with missing candles, duplicate records, invalid OHLC,
   closed-market records, time gaps, and malformed rows, **When** validation
   completes, **Then** the website exposes each category and its count without
   merging categories or hiding overlapping findings.
3. **Given** a dataset and options that produce no findings, **When** the run
   completes, **Then** the website clearly identifies the result as clean and
   does not imply that the dataset was modified.
4. **Given** a dataset that cannot be read or validated safely, **When** the run
   fails, **Then** the website shows a fatal diagnostic, does not present partial
   quality counts as final, and allows the user to correct the input or options.
5. **Given** the same dataset content and equivalent options, **When** the user
   runs validation through the website and the existing command-line workflow,
   **Then** the substantive validation outcome, findings, ordering, and scores
   are equivalent.

---

### User Story 2 - Inspect and Export a Detailed Report (Priority: P1)

As a person responsible for data quality, I want to inspect every validation
finding in a navigable web report and obtain the machine-readable result so that
I can investigate source records or pass the result to another process.

**Why this priority**: The existing detailed-report capability is essential for
acting on findings; a summary-only website would not provide feature parity.

**Independent Test**: Run validation against a fixture containing one or more
findings in every established category, inspect the report in the website, and
download the corresponding machine-readable report. Verify that all required
locations, evidence, relationships, and counts are present.

**Acceptance Scenarios**:

1. **Given** a successfully completed detailed validation, **When** the user
   opens the report, **Then** the website shows run status, source identity,
   resolved context, scan coverage, category summaries, and all findings from
   completed checks.
2. **Given** a missing-candle finding related to a time gap, **When** the user
   inspects either finding, **Then** the website provides a way to navigate to
   the related finding and preserves the expected timestamps and context.
3. **Given** a finding with source lines, timestamps, or observed values, **When**
   the user opens its details, **Then** those values are shown as distinct,
   readable evidence and not only as prose.
4. **Given** a detailed report, **When** the user requests an export, **Then**
   the downloaded machine-readable document uses the established versioned
   report contract and contains the same substantive information as the web
   report.
5. **Given** an incomplete or fatal run, **When** the user views its outcome,
   **Then** the website distinguishes the fatal diagnostic from a successful
   data-quality report and does not offer it as a complete report.

---

### User Story 3 - Review Dataset Scores (Priority: P1)

As someone deciding whether to use a dataset, I want to see the six independent
quality scores and the average score in the website so that I can identify weak
quality dimensions without calculating them manually.

**Why this priority**: Dataset scoring is an existing business capability and
must migrate with validation rather than becoming a command-line-only feature.

**Independent Test**: Score a clean fixture, a fixture with known defects, and a
fixture where a metric is not applicable. Verify that the website displays the
same per-metric states, counts, populations, weights, average coverage, and
average value as the established scoring workflow.

**Acceptance Scenarios**:

1. **Given** a successful validation with scoring enabled, **When** the result is
   displayed, **Then** all six metrics appear with their score or explicit
   not-applicable/not-scored state, count, population, and reason where needed.
2. **Given** a scored result, **When** the user reviews the average, **Then** the
   website identifies the metrics included, excluded metrics, resolved weights,
   and the documented average calculation.
3. **Given** an invalid scoring configuration, **When** the user submits it,
   **Then** the website rejects it before dataset processing and identifies the
   specific correction required.
4. **Given** scoring is not requested, **When** validation completes, **Then**
   the web result does not alter the established validation counts, findings, or
   result status.

---

### User Story 4 - Manage and Compare Benchmark Datasets (Priority: P1)

As a user evaluating data from a provider, I want to establish a validated
dataset as a benchmark and compare a candidate dataset against it in the
website so that tolerated broker variation is separated from material
inconsistency.

**Why this priority**: Benchmark comparison is the newest business workflow and
its value depends on being available in the same user-facing experience as
validation and scoring.

**Independent Test**: Establish a known dataset as an AUDUSD benchmark, compare
an identical candidate, a candidate with a tolerated opening-price variation,
and a candidate with a material opening-price difference plus missing and extra
candles. Verify that the web report matches the established comparison behavior.

**Acceptance Scenarios**:

1. **Given** a successfully validated dataset, **When** the user establishes it
   as a named benchmark, **Then** the website confirms the immutable benchmark
   identity, source content, context, validation results, six scores, and
   dataset score.
2. **Given** a benchmark name already in use, **When** the user attempts to use
   it again, **Then** the website prevents silent replacement and clearly
   requires a distinct or explicitly replacement-oriented action.
3. **Given** a benchmark and candidate with compatible context, **When** the
   user starts comparison, **Then** the website shows matched, missing, and
   extra records separately and reports material field discrepancies with the
   timestamp, field, values, difference, and resolved tolerance.
4. **Given** a candidate difference within the configured broker tolerance,
   **When** comparison completes, **Then** the difference is not presented as a
   material inconsistency while aggregate accepted-difference evidence remains
   auditable.
5. **Given** a candidate with no meaningful overlap or incompatible context,
   **When** comparison is requested, **Then** the website marks comparison as
   unavailable or incompatible rather than presenting a perfect or misleading
   agreement score.
6. **Given** an established benchmark, **When** a user reviews it later, **Then**
   the website can identify the exact source content and the validation context
   used when it was established.

---

### User Story 5 - Rely on a Consistent, Accessible Website Experience (Priority: P2)

As a user of the host website, I want the migrated workflows to look and behave
like the rest of the website while remaining understandable and usable with
different screen sizes and input methods.

**Why this priority**: A technically correct migration is not complete if users
cannot discover controls, understand failures, or use reports in the website's
normal interaction patterns.

**Independent Test**: Exercise each primary workflow using the website's normal
navigation, keyboard-only interaction, supported responsive layouts, and the
host website's established loading, empty, error, and success patterns.

**Acceptance Scenarios**:

1. **Given** the website's existing navigation and visual conventions, **When**
   the migrated feature is opened, **Then** its terminology, controls, states,
   and layout are consistent with the surrounding product unless a constitution
   requirement takes precedence.
2. **Given** a long-running validation or comparison, **When** the user waits
   for completion, **Then** the website communicates that work is in progress,
   prevents accidental duplicate submission, and eventually presents success,
   findings, or fatal failure without losing the run context.
3. **Given** invalid input or configuration, **When** the website reports the
   error, **Then** it identifies the affected input or option in plain language,
   preserves safe user-entered context where possible, and provides an
   actionable next step.
4. **Given** a user navigating without a pointing device or using a supported
   assistive technology, **When** they complete the primary workflow, **Then**
   all controls, status changes, findings, and error messages remain available
   and understandable.
5. **Given** a user viewing a report on a narrow or wide supported display,
   **When** they inspect summaries and finding details, **Then** content remains
   readable and no material evidence is hidden solely because of layout.

---

### Edge Cases

- An upload is empty, header-only, too large for the website's configured
  limits, or uses an unsupported encoding; the website must fail safely and
  explain whether processing started.
- The same run is submitted more than once because of a retry, refresh, or
  repeated click; the website must not create duplicate benchmark records or
  ambiguous run outcomes.
- A user navigates away, refreshes the page, loses connectivity, or closes the
  browser while a run is in progress; the final run outcome must not be falsely
  reported as clean, and a completed run must remain retrievable according to
  the host website's retention policy.
- A dataset takes longer to process than a normal page request; the user must
  receive progress or an explicit pending state rather than a request timeout
  being mistaken for a validation failure.
- A report contains a very large number of findings or very long source values;
  the website must remain navigable and must not silently truncate the
  machine-readable or downloadable result.
- Two users or browser sessions attempt conflicting benchmark operations at the
  same time; the website must preserve benchmark immutability and report the
  conflict deterministically.
- The benchmark is unavailable, corrupted, deleted, or no longer matches the
  supplied source identity; comparison must stop safely with an actionable
  diagnostic.
- A candidate has malformed rows, duplicate timestamps, incompatible timeframe
  or market context, no overlapping timestamps, or only tolerated value
  differences; each condition must retain its established distinction in the
  web report.
- The website's existing conventions conflict with a constitution requirement;
  the constitution governs the final behavior and the conflict must be resolved
  deliberately rather than silently weakening the requirement.

## Requirements *(mandatory)*

### Functional Requirements

#### Workflow Parity

- **FR-001**: The website MUST make the complete existing validation workflow
  available without requiring the user to invoke the command line, including
  dataset submission, all supported input interpretations, validation options,
  validation execution, result status, and report access.
- **FR-002**: The website MUST preserve the established validation behavior,
  definitions, defaults, six summary categories, findings, deterministic order,
  and fail-safe outcomes when the same input and equivalent options are used.
- **FR-003**: The website MUST expose all existing validation inputs and options
  that materially affect a result, including timeframe, market profile or
  calendar, timestamp interpretation, delimiter, header handling, source
  timestamp format and column selection, report detail/version selection, and
  explicit output or export choice where applicable.
- **FR-004**: The website MUST provide the existing detailed-report capability,
  dataset scoring capability, and benchmark lifecycle/comparison capability as
  web workflows rather than leaving any of them available only through the
  command line.
- **FR-005**: When a user does not request an optional capability, the website
  MUST not silently enable it or change the established result contract.
- **FR-006**: The website MUST not modify, repair, reorder, or overwrite an
  uploaded dataset or an established benchmark as a side effect of validation,
  scoring, reporting, or comparison.

#### Run Lifecycle and User Feedback

- **FR-007**: The website MUST validate web-submitted options before dataset
  processing begins and MUST identify each invalid, conflicting, incomplete, or
  unsupported option with actionable feedback.
- **FR-008**: The website MUST represent a run with a clear lifecycle state,
  including at least pending, running, completed-without-findings,
  completed-with-findings, and failed, and MUST never represent a pending or
  failed run as clean.
- **FR-009**: The website MUST support runs whose duration exceeds a normal
  interactive page action by showing an explicit pending or progress state and
  allowing the user to retrieve the final result without restarting the run
  solely because the page was refreshed or temporarily disconnected.
- **FR-010**: The website MUST prevent accidental duplicate submissions from
  creating duplicate work or conflicting benchmark artifacts, while allowing a
  deliberate user retry after a failed run.
- **FR-011**: A fatal validation, reporting, scoring, or comparison failure MUST
  expose an actionable diagnostic and MUST NOT expose partial counts, scores, or
  reports as a successful result.
- **FR-012**: The website MUST preserve enough run identity and selected context
  for a user to distinguish separate runs and to retrieve a completed result
  while it remains within the host website's retention policy.

#### Reports, Scoring, and Comparison Contracts

- **FR-013**: The web report MUST expose the established concise and detailed
  validation information, including source identity, resolved context, scan
  coverage, check status, reconciliation, category summaries, finding evidence,
  and relationships where those are present in the underlying report contract.
- **FR-014**: The website MUST provide a download or equivalent machine-readable
  export containing the same substantive result as the displayed report and
  MUST preserve the established versioned contract for consumers.
- **FR-015**: The website MUST show all six independent dataset quality metrics,
  their states, counts, populations, scores, weights, average coverage, and
  average result according to the established scoring contract.
- **FR-016**: The website MUST keep the candidate's independent quality score
  separate from the benchmark-agreement score and MUST show the benchmark's
  recorded scores separately from the candidate's scores.
- **FR-017**: The website MUST preserve the benchmark comparison distinctions
  between matched, missing, extra, tolerated, and material differences,
  including field-level discrepancy evidence and the resolved tolerance rule.
- **FR-018**: The website MUST show when a score or comparison result is not
  applicable, unavailable, or insufficiently covered and MUST never replace
  that state with a perfect score or an inferred value.
- **FR-019**: Web presentation MUST not require consumers to parse prose in order
  to recover required report fields, discrepancy evidence, score values,
  tolerance decisions, benchmark identity, or run status.
- **FR-020**: Equivalent web and command-line runs MUST produce equivalent
  substantive machine-readable results, even though their visual presentation
  and interaction may differ.

#### Architecture and Constitution Compliance

- **FR-021**: The website integration MUST drive the existing business logic
  through explicit application-facing contracts; business rules MUST remain
  independent of the website's presentation, transport, session, and storage
  concerns.
- **FR-022**: The Domain and Application layers MUST remain usable by both the
  existing command-line front end and the website without source changes made
  solely to move business behavior into the website.
- **FR-023**: Any website source changes required to integrate the feature MUST
  preserve inward dependency direction, keep environment-touching concerns at
  the appropriate boundary, and comply with the project constitution even when
  this requires changing the website's existing conventions.
- **FR-024**: The website MUST not duplicate validation, scoring, tolerance, or
  benchmark rules in presentation code when the established business logic
  already owns those rules.
- **FR-025**: Web-visible numeric and date/time values MUST use the established
  culture-invariant and UTC-normalized semantics; display localization or user
  time-zone presentation MUST not change computed results.
- **FR-026**: Every web run MUST retain a structured, auditable record of its
  inputs, resolved options, outcome, and result reference sufficient to explain
  what happened without relying on application internals.
- **FR-027**: The integration MUST be test-first, with failing tests preceding
  implementation for new behavior, and MUST preserve the constitution's full
  business-logic coverage gate plus appropriate web integration or end-to-end
  coverage for presentation and wiring.
- **FR-028**: The website MUST follow its established code style, naming,
  navigation, visual, loading, error, and interaction conventions where those
  conventions do not conflict with the constitution or the feature contracts.

#### Safety, Accessibility, and Compatibility

- **FR-029**: The website MUST enforce configured upload and processing limits
  before accepting work and MUST report limits without exposing unsafe server
  details or silently discarding input.
- **FR-030**: User-provided file names, source values, finding messages, and
  other data-derived text MUST be displayed and exported safely so they cannot
  alter page structure, report structure, or neighboring content.
- **FR-031**: The website MUST make all primary workflow controls, run states,
  validation errors, report summaries, and finding details usable through
  keyboard navigation and understandable to supported assistive technologies.
- **FR-032**: The website MUST preserve user context and provide clear recovery
  guidance for rejected uploads, invalid options, unavailable results, expired
  runs, and interrupted browser sessions.
- **FR-033**: The integration MUST preserve existing command-line behavior and
  contracts when the command-line front end is still used; adding the website
  MUST not regress non-web callers.
- **FR-034**: The feature MUST assess and update `README.md` in the same change
  to document the web application's supported workflow, parity boundaries,
  report access, configuration expectations, and any required build or run
  instructions. If the website is supplied in a separate repository, this
  repository's README MUST still document the integration boundary and the
  location of the authoritative web guidance.

### Key Entities *(include if feature involves data)*

- **Web Validation Run**: One submitted validation, scoring, or comparison
  operation with its identity, lifecycle status, source references, resolved
  options, and result reference.
- **Uploaded Dataset**: User-provided OHLCV source content retained only under
  the host website's approved retention and access rules, with safe source
  identity and content fingerprint.
- **Web Result View**: The user-facing representation of a completed or failed
  run, including summaries, detailed findings, scores, comparison evidence, and
  available actions.
- **Benchmark Reference**: The immutable benchmark snapshot established by the
  existing benchmark workflow and selected for later web comparisons.
- **Web Integration Contract**: The explicit boundary through which the website
  invokes business use cases and receives typed outcomes without owning their
  rules.
- **Exported Report**: A downloadable representation of the established report
  contract with the same substantive content as the displayed result.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In 100% of parity tests covering clean data and every established
  validation finding category, equivalent web and command-line inputs produce
  identical substantive counts, statuses, finding evidence, ordering, and
  score values.
- **SC-002**: In 100% of contract tests, web consumers can obtain every required
  validation, detailed-report, scoring, and benchmark-comparison field without
  parsing human-readable page text.
- **SC-003**: In 100% of fatal-input and invalid-configuration tests, the website
  shows a failed/incomplete outcome, identifies the blocking reason, and emits
  no partial successful report or score.
- **SC-004**: In 100% of repeated-run tests using identical source content and
  equivalent options, the substantive web result and downloaded machine-readable
  result are deterministic and equivalent to prior runs.
- **SC-005**: At least 90% of representative users can upload a dataset, start a
  validation, locate the six summary counts, and open the detailed report within
  five minutes without using the command line or consulting source code.
- **SC-006**: At least 90% of representative users can identify the number of
  material benchmark inconsistencies, tolerated differences, comparison coverage,
  and weakest candidate metric within two minutes of opening a completed
  comparison.
- **SC-007**: In 100% of browser refresh, temporary disconnect, and long-running
  operation tests, a completed run remains retrievable and no pending or failed
  run is shown as clean.
- **SC-008**: In 100% of source-protection tests, including report export and
  benchmark comparison, uploaded source content and immutable benchmark content
  remain byte-for-byte unchanged.
- **SC-009**: In 100% of accessibility acceptance tests, keyboard-only users can
  complete the primary validation flow and reach status, summary, error, and
  finding information without relying on a pointer device.
- **SC-010**: In 100% of regression tests, existing non-web callers retain their
  established behavior and report contracts after the web integration is added.
- **SC-011**: A first-time website integration review finds no constitution
  violation in dependency direction, business-logic ownership, deterministic
  results, structured auditability, test-first sequencing, or README impact
  resolution.

## Assumptions

- The existing Domain and Application business logic, validation contracts,
  detailed report contract, scoring contract, and benchmark-comparison contract
  are authoritative; this feature exposes them rather than redefining them.
- The website codebase and its runtime will be provided during planning or
  implementation. Its established conventions are adopted where compatible;
  the constitution and this feature's contracts take precedence when they
  conflict.
- The first web integration targets the same primary users and supported OHLCV
  data workflows as the current validator. It does not introduce new financial
  analysis rules.
- The host website supplies its established identity, session, authorization,
  retention, and deployment policies. The integration must honor those policies
  and must not invent a conflicting account model.
- A deployment that has no existing user or authorization model is treated as a
  trusted internal deployment for this feature; public exposure, user roles, and
  multi-tenant isolation require explicit host-website requirements before
  release.
- Uploaded data and generated results are retained only as long as required by
  the host website's approved policy; retention and deletion behavior must be
  documented during planning rather than silently assumed.
- Progress is user-facing status information and does not change the underlying
  deterministic business result.
- Human-readable web labels may be arranged differently from CLI text, but the
  underlying meanings, values, categories, and machine-readable fields remain
  compatible.
- Automatic data repair, internet downloading, provider authentication, batch
  multi-dataset processing, portfolio aggregation, and a new web-only scoring or
  comparison algorithm remain out of scope.

## Out of Scope

- Rewriting or replacing the established validation, detailed-report, scoring,
  or benchmark-comparison business rules.
- Building a separate web-only validation engine or duplicating domain rules in
  page, controller, or browser code.
- Automatic repair, interpolation, deduplication, source mutation, or deciding
  which broker's observation is financially correct.
- Downloading datasets from providers, broker authentication, marketplace
  benchmark discovery, or automatic benchmark selection.
- New statistical analytics, charting, forecasting, strategy research, trading,
  or portfolio workflows not already represented by the migrated contracts.
- Defining a new identity, role, tenancy, retention, or deployment platform when
  the host website has not yet supplied those requirements.
- Replacing the existing command-line front end or removing its compatibility
  while the website is introduced.
- Translating report content or changing computed values based on display locale
  or user time zone.