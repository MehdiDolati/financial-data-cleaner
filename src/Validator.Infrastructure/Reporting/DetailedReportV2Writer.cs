using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
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
    // Renders one successful detailed report as a v2 JSON document. Findings are
    // streamed one at a time from the completed catalog and each finding's
    // child records are consumed before the next finding is read, so peak
    // memory stays bounded by the largest single finding rather than by the
    // number of findings. The encoded document is flushed to the destination
    // after every finding, so an arbitrarily large report never accumulates in
    // memory.
    public sealed class DetailedReportV2Writer
    {
        private const int FlushThresholdBytes = 64 * 1024;

        public async Task WriteAsync(
            DetailedValidationReport report,
            TextWriter destination,
            CancellationToken cancellationToken = default)
        {
            if (report is null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            using var buffer = new MemoryStream();
            await using var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false });

            json.WriteStartObject();
            json.WriteNumber("contractVersion", report.ContractVersion);
            json.WriteString("status", report.Status.ToString());
            json.WriteBoolean("findingSetComplete", report.FindingSetComplete);
            WriteSource(json, report.Source);
            WriteContext(json, report.Context);
            WriteCoverage(json, report.Coverage);
            WriteChecks(json, report.Checks);
            WriteSummary(json, report.Summary);
            WriteReconciliation(json, report.Reconciliation);

            json.WriteStartArray("findings");
            await foreach (var cursor in report.Findings.ReadCanonicalAsync(cancellationToken).ConfigureAwait(false))
            {
                await WriteFindingAsync(json, cursor, cancellationToken).ConfigureAwait(false);
                await FlushAsync(json, buffer, destination, FlushThresholdBytes, cancellationToken).ConfigureAwait(false);
            }

            json.WriteEndArray();
            json.WriteEndObject();
            await FlushAsync(json, buffer, destination, 0, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync().ConfigureAwait(false);
        }

        private static async Task FlushAsync(
            Utf8JsonWriter json,
            MemoryStream buffer,
            TextWriter destination,
            int threshold,
            CancellationToken cancellationToken)
        {
            await json.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (buffer.Length < threshold)
            {
                return;
            }

            var text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            buffer.SetLength(0);
            buffer.Position = 0;
            await destination.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        private static void WriteSource(Utf8JsonWriter json, SourceIdentity source)
        {
            json.WriteStartObject("source");
            json.WriteString("fileName", source.FileName);
            json.WriteNumber("byteSize", source.ByteSize);
            json.WriteString("sha256", source.Sha256);
            json.WriteEndObject();
        }

        private static void WriteContext(Utf8JsonWriter json, ValidationContextSnapshot context)
        {
            json.WriteStartObject("context");
            json.WriteString("timeframe", context.Timeframe);

            json.WriteStartObject("calendar");
            json.WriteString("profile", context.Calendar.Profile);
            json.WriteString("name", context.Calendar.Name);
            if (context.Calendar.TimeZone is not null)
            {
                json.WriteString("timeZone", context.Calendar.TimeZone);
            }

            if (context.Calendar.DefinitionSha256 is not null)
            {
                json.WriteString("definitionSha256", context.Calendar.DefinitionSha256);
            }

            if (context.Calendar.Sessions.Count > 0)
            {
                json.WriteStartArray("sessions");
                foreach (var session in context.Calendar.Sessions)
                {
                    WriteSession(json, session);
                }

                json.WriteEndArray();
            }

            json.WriteEndObject();

            json.WriteStartObject("timestamp");
            json.WriteString("mode", context.Timestamp.Mode.ToString());
            if (context.Timestamp.Mode == TimestampMode.SeparateDateTime)
            {
                json.WriteString("dateFormat", context.Timestamp.DateFormat);
                json.WriteString("timeFormat", context.Timestamp.TimeFormat);
            }
            else
            {
                json.WriteString("timestampFormat", context.Timestamp.TimestampFormat);
                json.WriteString("timestampColumn", context.Timestamp.TimestampColumn);
            }

            json.WriteString("sourceOffset", context.Timestamp.SourceOffset);
            json.WriteEndObject();

            json.WriteString("delimiter", context.Delimiter);
            json.WriteBoolean("hasHeader", context.HasHeader);
            if (context.DateRange is null)
            {
                json.WriteNull("dateRange");
            }
            else
            {
                json.WriteStartObject("dateRange");
                json.WriteString("from", ToUtcText(context.DateRange.Start));
                json.WriteString("to", ToUtcText(context.DateRange.End));
                json.WriteEndObject();
            }

            json.WriteEndObject();
        }

        private static void WriteSession(Utf8JsonWriter json, WeeklySession session)
        {
            json.WriteStartObject();
            json.WriteString("openDay", session.OpenDay.ToString());
            json.WriteString("openTime", ToLocalTimeText(session.OpenTime));
            json.WriteString("closeDay", session.CloseDay.ToString());
            json.WriteString("closeTime", ToLocalTimeText(session.CloseTime));
            json.WriteEndObject();
        }

        private static void WriteCoverage(Utf8JsonWriter json, ScanCoverage coverage)
        {
            json.WriteStartObject("coverage");
            json.WriteNumber("physicalRowsExamined", coverage.PhysicalRowsExamined);
            json.WriteNumber("acceptedRows", coverage.AcceptedRows);
            json.WriteNumber("malformedRows", coverage.MalformedRows);
            json.WriteEndObject();
        }

        private static void WriteChecks(Utf8JsonWriter json, IReadOnlyList<CheckExecution> checks)
        {
            json.WriteStartArray("checks");
            foreach (var check in checks)
            {
                json.WriteStartObject();
                json.WriteString("check", check.Check.ToString());
                json.WriteString("status", check.Status.ToString());
                if (check.Status != CheckStatus.Completed)
                {
                    json.WriteString("reason", check.Reason);
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        private static void WriteSummary(Utf8JsonWriter json, DetailedSummary summary)
        {
            json.WriteStartObject("summary");
            json.WriteNumber("missingCandles", summary.MissingCandles);
            json.WriteNumber("duplicateRecords", summary.DuplicateRecords);
            json.WriteNumber("invalidOhlc", summary.InvalidOhlc);
            json.WriteNumber("closedMarketRecords", summary.ClosedMarketRecords);
            json.WriteNumber("timeGaps", summary.TimeGaps);
            json.WriteNumber("malformedRows", summary.MalformedRows);
            json.WriteEndObject();
        }

        private static void WriteReconciliation(Utf8JsonWriter json, ReportReconciliation reconciliation)
        {
            json.WriteStartObject("reconciliation");
            json.WriteBoolean("coverageReconciled", reconciliation.CoverageReconciled);
            json.WriteStartArray("categories");
            foreach (var category in reconciliation.Categories)
            {
                json.WriteStartObject();
                json.WriteString("category", category.Category.ToString());
                json.WriteNumber("summaryCount", category.SummaryCount);
                json.WriteNumber("entryCount", category.EntryCount);
                json.WriteNumber("contributionSum", category.ContributionSum);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        private static async Task WriteFindingAsync(
            Utf8JsonWriter json,
            IDetailedFindingCursor cursor,
            CancellationToken cancellationToken)
        {
            var header = cursor.Header;
            json.WriteStartObject();
            json.WriteString("reference", header.Reference.Value);
            json.WriteString("category", header.Category.ToString());
            json.WriteString("title", header.Title);
            json.WriteString("explanation", header.Explanation);
            json.WriteNumber("countContribution", header.CountContribution);

            json.WriteStartObject("location");
            json.WriteStartArray("sourceLines");
            await foreach (var line in cursor.ReadSourceLinesAsync(cancellationToken).ConfigureAwait(false))
            {
                json.WriteNumberValue(line);
            }

            json.WriteEndArray();
            if (header.Location.TimestampUtc.HasValue)
            {
                json.WriteString("timestampUtc", ToUtcText(header.Location.TimestampUtc.Value));
            }

            if (header.Location.OriginalTimestampText is not null)
            {
                json.WriteString("originalTimestampText", header.Location.OriginalTimestampText);
            }

            json.WriteEndObject();

            var evidence = await ReadEvidenceAsync(cursor, cancellationToken).ConfigureAwait(false);
            WriteEvidence(json, header, evidence);

            json.WriteStartArray("relationships");
            await foreach (var relationship in cursor.ReadRelationshipsAsync(cancellationToken).ConfigureAwait(false))
            {
                json.WriteStartObject();
                json.WriteString("kind", relationship.Kind);
                json.WriteString("targetReference", relationship.TargetReference.Value);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteString("suggestedAction", header.SuggestedAction);
            json.WriteEndObject();
        }

        private static async Task<List<FindingEvidenceRecord>> ReadEvidenceAsync(
            IDetailedFindingCursor cursor,
            CancellationToken cancellationToken)
        {
            var records = new List<FindingEvidenceRecord>();
            await foreach (var record in cursor.ReadEvidenceAsync(cancellationToken).ConfigureAwait(false))
            {
                records.Add(record);
            }

            return records;
        }

        private static void WriteEvidence(
            Utf8JsonWriter json,
            DetailedFindingHeader header,
            List<FindingEvidenceRecord> records)
        {
            json.WriteStartObject("evidence");
            json.WriteString("kind", header.EvidenceKind.ToString());

            switch (header.Category)
            {
                case FindingCategory.MissingCandle:
                    WriteMissingCandleEvidence(json, records);
                    break;
                case FindingCategory.TimeGap:
                    WriteTimeGapEvidence(json, records);
                    break;
                case FindingCategory.DuplicateRecord:
                    WriteDuplicateEvidence(json, records);
                    break;
                case FindingCategory.InvalidOhlc:
                    WriteInvalidOhlcEvidence(json, records);
                    break;
                case FindingCategory.ClosedMarketRecord:
                    WriteClosedMarketEvidence(json, records);
                    break;
                case FindingCategory.MalformedRow:
                    WriteMalformedRowEvidence(json, records);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(header));
            }

            json.WriteEndObject();
        }

        private static void WriteMissingCandleEvidence(Utf8JsonWriter json, List<FindingEvidenceRecord> records)
        {
            var evidence = Require<FindingEvidenceRecord.MissingCandle>(records).Evidence;
            json.WriteString("expectedTimestampUtc", ToUtcText(evidence.ExpectedTimestampUtc));
            json.WriteString("expectedTimeframe", evidence.ExpectedTimeframe.ToString());
            json.WriteString("timeGapReference", evidence.TimeGapReference.Value);
            WriteOptionalTimestamp(json, "previousObservedTimestampUtc", evidence.PreviousObservedTimestampUtc);
            WriteOptionalTimestamp(json, "nextObservedTimestampUtc", evidence.NextObservedTimestampUtc);
        }

        private static void WriteTimeGapEvidence(Utf8JsonWriter json, List<FindingEvidenceRecord> records)
        {
            var evidence = Require<FindingEvidenceRecord.TimeGapHeader>(records).Evidence;
            json.WriteString("firstMissingTimestampUtc", ToUtcText(evidence.FirstMissingTimestampUtc));
            json.WriteString("lastMissingTimestampUtc", ToUtcText(evidence.LastMissingTimestampUtc));
            json.WriteString("expectedTimeframe", evidence.ExpectedTimeframe.ToString());
            json.WriteNumber("missingCandleCount", evidence.MissingCandleCount);
            json.WriteNumber("elapsedSeconds", evidence.ElapsedSeconds);
            WriteOptionalTimestamp(json, "previousObservedTimestampUtc", evidence.PreviousObservedTimestampUtc);
            WriteOptionalTimestamp(json, "nextObservedTimestampUtc", evidence.NextObservedTimestampUtc);

            json.WriteStartArray("missingCandleReferences");
            foreach (var record in Children<FindingEvidenceRecord.TimeGapMissingReference>(records))
            {
                json.WriteStringValue(record.TargetReference.Value);
            }

            json.WriteEndArray();
        }

        private static void WriteDuplicateEvidence(Utf8JsonWriter json, List<FindingEvidenceRecord> records)
        {
            var evidence = Require<FindingEvidenceRecord.DuplicateHeader>(records).Evidence;
            json.WriteString("sharedTimestampUtc", ToUtcText(evidence.SharedTimestampUtc));
            json.WriteString("classification", evidence.Classification.ToString());

            json.WriteStartArray("differingFields");
            foreach (var record in Children<FindingEvidenceRecord.DuplicateDifferingField>(records))
            {
                json.WriteStringValue(record.Field);
            }

            json.WriteEndArray();

            json.WriteStartArray("rows");
            foreach (var record in Children<FindingEvidenceRecord.DuplicateRow>(records))
            {
                var row = record.Row;
                json.WriteStartObject();
                json.WriteNumber("sourceLine", row.SourceLine);
                if (row.OriginalTimestampText is not null)
                {
                    json.WriteString("originalTimestampText", row.OriginalTimestampText);
                }

                json.WriteNumber("open", row.Open);
                json.WriteNumber("high", row.High);
                json.WriteNumber("low", row.Low);
                json.WriteNumber("close", row.Close);
                json.WriteNumber("volume", row.Volume);
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        private static void WriteInvalidOhlcEvidence(Utf8JsonWriter json, List<FindingEvidenceRecord> records)
        {
            var observed = Require<FindingEvidenceRecord.InvalidOhlcValues>(records).Observed;
            json.WriteStartObject("observed");
            json.WriteNumber("open", observed.Open);
            json.WriteNumber("high", observed.High);
            json.WriteNumber("low", observed.Low);
            json.WriteNumber("close", observed.Close);
            json.WriteNumber("volume", observed.Volume);
            json.WriteEndObject();

            json.WriteStartArray("violations");
            foreach (var record in Children<FindingEvidenceRecord.InvalidOhlcViolation>(records))
            {
                json.WriteStringValue(record.Code.ToString());
            }

            json.WriteEndArray();
        }

        private static void WriteClosedMarketEvidence(Utf8JsonWriter json, List<FindingEvidenceRecord> records)
        {
            var evidence = Require<FindingEvidenceRecord.ClosedMarket>(records).Evidence;
            json.WriteString("marketProfile", evidence.MarketProfile);
            json.WriteString("calendarName", evidence.CalendarName);
            if (evidence.CalendarTimeZone is not null)
            {
                json.WriteString("calendarTimeZone", evidence.CalendarTimeZone);
            }

            json.WriteString("closedRule", evidence.ClosedRule);
            if (evidence.Boundary is not null)
            {
                json.WriteStartObject("boundary");
                json.WriteString("closedFromUtc", ToUtcText(evidence.Boundary.ClosedFromUtc));
                json.WriteString("nextOpenUtc", ToUtcText(evidence.Boundary.NextOpenUtc));
                json.WriteEndObject();
            }
        }

        private static void WriteMalformedRowEvidence(Utf8JsonWriter json, List<FindingEvidenceRecord> records)
        {
            var evidence = Require<FindingEvidenceRecord.MalformedHeader>(records).Evidence;
            WriteOptionalTimestamp(json, "parsedTimestampUtc", evidence.ParsedTimestampUtc);
            if (evidence.OriginalTimestampText is not null)
            {
                json.WriteString("originalTimestampText", evidence.OriginalTimestampText);
            }

            json.WriteBoolean("expectedSlotReserved", evidence.ExpectedSlotReserved);

            json.WriteStartArray("fieldErrors");
            foreach (var record in Children<FindingEvidenceRecord.MalformedFieldErrorRecord>(records))
            {
                json.WriteStartObject();
                json.WriteString("field", record.Error.Field);
                json.WriteString("originalValue", record.Error.OriginalValue);
                json.WriteString("reasonCode", record.Error.ReasonCode.ToString());
                json.WriteString("reason", record.Error.Reason);
                json.WriteEndObject();
            }

            json.WriteEndArray();

            json.WriteStartArray("checksNotApplied");
            foreach (var record in Children<FindingEvidenceRecord.MalformedSkippedCheck>(records))
            {
                json.WriteStringValue(record.Check.ToString());
            }

            json.WriteEndArray();
        }

        private static void WriteOptionalTimestamp(Utf8JsonWriter json, string name, DateTimeOffset? value)
        {
            if (value.HasValue)
            {
                json.WriteString(name, ToUtcText(value.Value));
            }
        }

        private static TRecord Require<TRecord>(List<FindingEvidenceRecord> records)
            where TRecord : FindingEvidenceRecord
        {
            foreach (var record in records)
            {
                if (record is TRecord match)
                {
                    return match;
                }
            }

            throw new InvalidOperationException(
                $"A finding is missing its required {typeof(TRecord).Name} evidence record.");
        }

        private static IEnumerable<TRecord> Children<TRecord>(List<FindingEvidenceRecord> records)
            where TRecord : FindingEvidenceRecord
        {
            foreach (var record in records)
            {
                if (record is TRecord match)
                {
                    yield return match;
                }
            }
        }

        internal static string ToUtcText(DateTimeOffset value) =>
            value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        internal static string ToLocalTimeText(TimeSpan value) =>
            value.Seconds == 0
                ? value.ToString(@"hh\:mm", CultureInfo.InvariantCulture)
                : value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }
}
