using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Domain.Calendars;
using Validator.Domain.Findings;
using Validator.Domain.Findings.Evidence;

namespace Validator.Infrastructure.Reporting
{
    // Renders one successful detailed report as complete human-readable text.
    // The text opens with the established six summary lines so an existing
    // reader keeps its report, then adds the labeled detailed sections. Findings
    // are streamed one at a time from the completed catalog and flushed as they
    // are written, so peak memory stays bounded by the largest single finding
    // and the text is never paginated, sampled, or truncated.
    public sealed class VerboseReportWriter
    {
        private const int FlushThresholdChars = 32 * 1024;
        private const string NotApplicable = "not applicable";

        public async Task WriteAsync(
            DetailedValidationReport report,
            TextWriter destination,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(report);
            ArgumentNullException.ThrowIfNull(destination);

            var buffer = new StringBuilder();

            AppendSummaryLines(buffer, report.Summary);
            if (report.Score is not null)
            {
                // Scoring is additive and sits immediately after the six summary
                // lines; the rest of the detailed report is unchanged.
                ScoringTextSectionWriter.Append(buffer, report.Score);
            }

            AppendStatus(buffer, report);

            AppendSource(buffer, report.Source);
            AppendContext(buffer, report.Context);
            AppendCoverage(buffer, report.Coverage);
            AppendChecks(buffer, report.Checks);
            AppendReconciliation(buffer, report.Reconciliation, report.Summary);

            buffer.Append("Findings:").Append('\n');
            var any = false;
            await foreach (var cursor in report.Findings.ReadCanonicalAsync(cancellationToken).ConfigureAwait(false))
            {
                any = true;
                await AppendFindingAsync(buffer, cursor, cancellationToken).ConfigureAwait(false);
                await FlushAsync(buffer, destination, FlushThresholdChars, cancellationToken).ConfigureAwait(false);
            }

            if (!any)
            {
                buffer.Append("- none").Append('\n');
            }

            await FlushAsync(buffer, destination, 0, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private static async Task FlushAsync(
            StringBuilder buffer,
            TextWriter destination,
            int threshold,
            CancellationToken cancellationToken)
        {
            if (buffer.Length < threshold)
            {
                return;
            }

            var text = buffer.ToString();
            buffer.Clear();
            await destination.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        private static void AppendSummaryLines(StringBuilder buffer, DetailedSummary summary)
        {
            // The six leading lines come from the one shared label source so the
            // concise and verbose renderings cannot drift (SC-006).
            foreach (var line in SummaryLabels.Lines(summary))
            {
                buffer.Append(line).Append('\n');
            }

            buffer.Append('\n');
        }


        private static void AppendStatus(StringBuilder buffer, DetailedValidationReport report)
        {
            buffer.Append("Report status:").Append('\n');
            buffer.Append("- status: ").Append(report.Status.ToString()).Append('\n');
            buffer.Append("- validationCompleted: true").Append('\n');
            buffer.Append("- findingSetComplete: ")
                .Append(Boolean(report.FindingSetComplete))
                .Append(" (complete for every check listed as Completed under Check execution)")
                .Append('\n');
            buffer.Append("- contractVersion: ").Append(Number(report.ContractVersion)).Append('\n');
            buffer.Append('\n');
        }

        private static void AppendSource(StringBuilder buffer, SourceIdentity source)
        {
            buffer.Append("Source identity:").Append('\n');
            buffer.Append("- fileName: ").Append(Quote(source.FileName)).Append('\n');
            buffer.Append("- byteSize: ").Append(Number(source.ByteSize)).Append('\n');
            buffer.Append("- sha256: ").Append(source.Sha256).Append('\n');
            buffer.Append('\n');
        }

        private static void AppendContext(StringBuilder buffer, ValidationContextSnapshot context)
        {
            buffer.Append("Validation context:").Append('\n');
            buffer.Append("- timeframe: ").Append(context.Timeframe).Append('\n');
            buffer.Append("- calendarProfile: ").Append(context.Calendar.Profile).Append('\n');
            buffer.Append("- calendarName: ").Append(Quote(context.Calendar.Name)).Append('\n');
            buffer.Append("- calendarTimeZone: ")
                .Append(context.Calendar.TimeZone is null ? NotApplicable : Quote(context.Calendar.TimeZone))
                .Append('\n');
            buffer.Append("- calendarDefinitionSha256: ")
                .Append(context.Calendar.DefinitionSha256 ?? NotApplicable)
                .Append('\n');

            if (context.Calendar.Sessions.Count == 0)
            {
                buffer.Append("- calendarSessions: ").Append(NotApplicable).Append('\n');
            }
            else
            {
                buffer.Append("- calendarSessions:").Append('\n');
                foreach (var session in context.Calendar.Sessions)
                {
                    buffer.Append("  - ")
                        .Append(session.OpenDay.ToString())
                        .Append(' ')
                        .Append(DetailedReportV2Writer.ToLocalTimeText(session.OpenTime))
                        .Append(" .. ")
                        .Append(session.CloseDay.ToString())
                        .Append(' ')
                        .Append(DetailedReportV2Writer.ToLocalTimeText(session.CloseTime))
                        .Append('\n');
                }
            }

            buffer.Append("- timestampMode: ").Append(context.Timestamp.Mode.ToString()).Append('\n');
            // Only the fields the active layout actually uses are stated; the
            // others are absent rather than shown as empty values.
            if (context.Timestamp.Mode == TimestampMode.SeparateDateTime)
            {
                buffer.Append("- dateFormat: ").Append(Optional(context.Timestamp.DateFormat)).Append('\n');
                buffer.Append("- timeFormat: ").Append(Optional(context.Timestamp.TimeFormat)).Append('\n');
            }
            else
            {
                buffer.Append("- timestampFormat: ").Append(Optional(context.Timestamp.TimestampFormat)).Append('\n');
                buffer.Append("- timestampColumn: ").Append(Optional(context.Timestamp.TimestampColumn)).Append('\n');
            }

            buffer.Append("- sourceOffset: ").Append(context.Timestamp.SourceOffset).Append('\n');
            buffer.Append("- delimiter: ").Append(Quote(context.Delimiter)).Append('\n');
            buffer.Append("- hasHeader: ").Append(Boolean(context.HasHeader)).Append('\n');
            buffer.Append("- dateRange: ")
                .Append(context.DateRange is null
                    ? NotApplicable
                    : $"{Utc(context.DateRange.Start)} .. {Utc(context.DateRange.End)}")
                .Append('\n');
            buffer.Append('\n');
        }

        private static void AppendCoverage(StringBuilder buffer, ScanCoverage coverage)
        {
            buffer.Append("Scan coverage:").Append('\n');
            buffer.Append("- physicalRowsExamined: ").Append(Number(coverage.PhysicalRowsExamined)).Append('\n');
            buffer.Append("- acceptedRows: ").Append(Number(coverage.AcceptedRows)).Append('\n');
            buffer.Append("- malformedRows: ").Append(Number(coverage.MalformedRows)).Append('\n');
            buffer.Append('\n');
        }

        private static void AppendChecks(StringBuilder buffer, IReadOnlyList<CheckExecution> checks)
        {
            buffer.Append("Check execution:").Append('\n');
            foreach (var check in checks)
            {
                buffer.Append("- ")
                    .Append(check.Check.ToString())
                    .Append(": ")
                    .Append(check.Status.ToString());
                if (check.Status != CheckStatus.Completed)
                {
                    buffer.Append("; reason=").Append(Quote(check.Reason ?? string.Empty));
                }

                buffer.Append('\n');
            }

            buffer.Append('\n');
        }

        private static void AppendReconciliation(
            StringBuilder buffer,
            ReportReconciliation reconciliation,
            DetailedSummary summary)
        {
            buffer.Append("Category reconciliation:").Append('\n');
            foreach (var category in reconciliation.Categories)
            {
                buffer.Append("- ")
                    .Append(category.Category.ToString())
                    .Append(": summaryCount=")
                    .Append(Number(category.SummaryCount))
                    .Append("; entryCount=")
                    .Append(Number(category.EntryCount))
                    .Append("; contributionSum=")
                    .Append(Number(category.ContributionSum))
                    .Append('\n');
            }

            buffer.Append("- coverageReconciled: ").Append(Boolean(reconciliation.CoverageReconciled)).Append('\n');
            buffer.Append("- Sum of category counts (not unique root causes): ")
                .Append(Number(summary.TotalFindings))
                .Append('\n');
            buffer.Append('\n');
        }

        private static async Task AppendFindingAsync(
            StringBuilder buffer,
            IDetailedFindingCursor cursor,
            CancellationToken cancellationToken)
        {
            var header = cursor.Header;
            buffer.Append("- reference=")
                .Append(header.Reference.Value)
                .Append("; category=")
                .Append(header.Category.ToString())
                .Append("; title=")
                .Append(Quote(header.Title))
                .Append("; countContribution=")
                .Append(Number(header.CountContribution))
                .Append('\n');

            var lines = new List<long>();
            await foreach (var line in cursor.ReadSourceLinesAsync(cancellationToken).ConfigureAwait(false))
            {
                lines.Add(line);
            }

            buffer.Append("  location: sourceLines=")
                .Append(lines.Count == 0 ? NotApplicable : string.Join(",", lines))
                .Append("; timestampUtc=")
                .Append(header.Location.TimestampUtc.HasValue
                    ? Utc(header.Location.TimestampUtc.Value)
                    : NotApplicable)
                .Append("; originalTimestampText=")
                .Append(header.Location.OriginalTimestampText is null
                    ? NotApplicable
                    : Quote(header.Location.OriginalTimestampText))
                .Append('\n');

            buffer.Append("  explanation: ").Append(Quote(header.Explanation)).Append('\n');
            buffer.Append("  evidence: kind=").Append(header.EvidenceKind.ToString()).Append('\n');
            await foreach (var record in cursor.ReadEvidenceAsync(cancellationToken).ConfigureAwait(false))
            {
                buffer.Append("    ").Append(Describe(record)).Append('\n');
            }

            var relationships = 0;
            await foreach (var relationship in cursor.ReadRelationshipsAsync(cancellationToken).ConfigureAwait(false))
            {
                relationships++;
                buffer.Append("  relationship: kind=")
                    .Append(relationship.Kind)
                    .Append("; targetReference=")
                    .Append(relationship.TargetReference.Value)
                    .Append('\n');
            }

            if (relationships == 0)
            {
                buffer.Append("  relationship: ").Append(NotApplicable).Append('\n');
            }

            buffer.Append("  suggestedAction: ").Append(Quote(header.SuggestedAction)).Append('\n');
        }

        // One evidence record becomes one indented line of named values, so no
        // source text can introduce a new line or a new finding.
        private static string Describe(FindingEvidenceRecord record) => record switch
        {
            FindingEvidenceRecord.MissingCandle missing =>
                $"expectedTimestampUtc={Utc(missing.Evidence.ExpectedTimestampUtc)}" +
                $"; expectedTimeframe={missing.Evidence.ExpectedTimeframe}" +
                $"; timeGapReference={missing.Evidence.TimeGapReference.Value}" +
                $"; previousObservedTimestampUtc={Optional(missing.Evidence.PreviousObservedTimestampUtc)}" +
                $"; nextObservedTimestampUtc={Optional(missing.Evidence.NextObservedTimestampUtc)}" +
                $"; previousObservedSourceLine={Optional(missing.Evidence.PreviousObservedSourceLine)}" +
                $"; nextObservedSourceLine={Optional(missing.Evidence.NextObservedSourceLine)}",
            FindingEvidenceRecord.TimeGapHeader gap =>
                $"firstMissingTimestampUtc={Utc(gap.Evidence.FirstMissingTimestampUtc)}" +
                $"; lastMissingTimestampUtc={Utc(gap.Evidence.LastMissingTimestampUtc)}" +
                $"; expectedTimeframe={gap.Evidence.ExpectedTimeframe}" +
                $"; missingCandleCount={Number(gap.Evidence.MissingCandleCount)}" +
                $"; elapsedSeconds={Number(gap.Evidence.ElapsedSeconds)}" +
                $"; previousObservedTimestampUtc={Optional(gap.Evidence.PreviousObservedTimestampUtc)}" +
                $"; nextObservedTimestampUtc={Optional(gap.Evidence.NextObservedTimestampUtc)}" +
                $"; previousObservedSourceLine={Optional(gap.Evidence.PreviousObservedSourceLine)}" +
                $"; nextObservedSourceLine={Optional(gap.Evidence.NextObservedSourceLine)}",
            FindingEvidenceRecord.TimeGapMissingReference missing =>
                $"missingCandleReference={missing.TargetReference.Value}",
            FindingEvidenceRecord.DuplicateHeader duplicate =>
                $"sharedTimestampUtc={Utc(duplicate.Evidence.SharedTimestampUtc)}" +
                $"; classification={duplicate.Evidence.Classification}",
            FindingEvidenceRecord.DuplicateDifferingField field =>
                $"differingField={Quote(field.Field)}",
            FindingEvidenceRecord.DuplicateRow row =>
                $"row: sourceLine={Number(row.Row.SourceLine)}" +
                $"; originalTimestampText={(row.Row.OriginalTimestampText is null ? NotApplicable : Quote(row.Row.OriginalTimestampText))}" +
                $"; open={Number(row.Row.Open)}; high={Number(row.Row.High)}" +
                $"; low={Number(row.Row.Low)}; close={Number(row.Row.Close)}" +
                $"; volume={Number(row.Row.Volume)}",
            FindingEvidenceRecord.InvalidOhlcValues values =>
                $"observed: open={Number(values.Observed.Open)}; high={Number(values.Observed.High)}" +
                $"; low={Number(values.Observed.Low)}; close={Number(values.Observed.Close)}" +
                $"; volume={Number(values.Observed.Volume)}",
            FindingEvidenceRecord.InvalidOhlcViolation violation =>
                $"violation={violation.Code}",
            FindingEvidenceRecord.ClosedMarket closed =>
                $"marketProfile={Quote(closed.Evidence.MarketProfile)}" +
                $"; calendarName={Quote(closed.Evidence.CalendarName)}" +
                $"; calendarTimeZone={(closed.Evidence.CalendarTimeZone is null ? NotApplicable : Quote(closed.Evidence.CalendarTimeZone))}" +
                $"; closedRule={Quote(closed.Evidence.ClosedRule)}" +
                $"; closedFromUtc={(closed.Evidence.Boundary is null ? NotApplicable : Utc(closed.Evidence.Boundary.ClosedFromUtc))}" +
                $"; nextOpenUtc={(closed.Evidence.Boundary is null ? NotApplicable : Utc(closed.Evidence.Boundary.NextOpenUtc))}",
            FindingEvidenceRecord.MalformedHeader malformed =>
                $"parsedTimestampUtc={Optional(malformed.Evidence.ParsedTimestampUtc)}" +
                $"; originalTimestampText={(malformed.Evidence.OriginalTimestampText is null ? NotApplicable : Quote(malformed.Evidence.OriginalTimestampText))}" +
                $"; expectedSlotReserved={Boolean(malformed.Evidence.ExpectedSlotReserved)}",
            FindingEvidenceRecord.MalformedFieldErrorRecord error =>
                $"fieldError: field={Quote(error.Error.Field)}" +
                $"; originalValue={Quote(error.Error.OriginalValue)}" +
                $"; reasonCode={error.Error.ReasonCode}" +
                $"; reason={Quote(error.Error.Reason)}",
            FindingEvidenceRecord.MalformedSkippedCheck skipped =>
                $"checkNotApplied={skipped.Check}",
            _ => throw new ArgumentOutOfRangeException(nameof(record))
        };

        // Source-derived text is quoted and escaped, so a tab, newline, quote,
        // or control character in the data cannot forge a heading or an extra
        // finding line.
        internal static string Quote(string value)
        {
            var quoted = new StringBuilder(value.Length + 2);
            quoted.Append('"');
            foreach (var character in value)
            {
                switch (character)
                {
                    case '"':
                        quoted.Append("\\\"");
                        break;
                    case '\\':
                        quoted.Append("\\\\");
                        break;
                    case '\n':
                        quoted.Append("\\n");
                        break;
                    case '\r':
                        quoted.Append("\\r");
                        break;
                    case '\t':
                        quoted.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(character))
                        {
                            quoted.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            quoted.Append(character);
                        }

                        break;
                }
            }

            quoted.Append('"');
            return quoted.ToString();
        }

        private static string Optional(DateTimeOffset? value) =>
            value.HasValue ? Utc(value.Value) : NotApplicable;

        // A bracketing observed line locates an absence in the file. An
        // unavailable side at a dataset boundary is labeled rather than shown as
        // a number, so it cannot be misread as line zero (FR-040).
        private static string Optional(long? value) =>
            value.HasValue ? Number(value.Value) : NotApplicable;

        private static string Optional(string? value) =>
            value is null ? NotApplicable : Quote(value);

        private static string Utc(DateTimeOffset value) => DetailedReportV2Writer.ToUtcText(value);

        private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Boolean(bool value) => value ? "true" : "false";
    }
}
