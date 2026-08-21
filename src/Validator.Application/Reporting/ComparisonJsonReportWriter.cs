using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Validator.Application.Comparison;
using Validator.Domain.Comparison;

namespace Validator.Application.Reporting
{
    /// <summary>
    /// Renders a ComparisonReport as JSON per comparison-report-contract.md JSON format.
    /// Extends the existing DetailedReportV2Writer with benchmarkComparison section.
    /// </summary>
    public sealed class ComparisonJsonReportWriter
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        /// <summary>
        /// Renders the comparison report as JSON string.
        /// </summary>
        public string Write(ComparisonReport report)
        {
            ArgumentNullException.ThrowIfNull(report);

            var section = BuildBenchmarkComparisonSection(report);
            return JsonSerializer.Serialize(section, Options);
        }

        /// <summary>
        /// Writes the benchmarkComparison section to a Utf8JsonWriter.
        /// Used by DetailedReportV2Writer to append to the existing report.
        /// </summary>
        public void WriteSection(Utf8JsonWriter writer, ComparisonReport report)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(report);

            writer.WriteStartObject("benchmarkComparison");
            writer.WriteNumber("contractVersion", 1);

            // Benchmark
            writer.WritePropertyName("benchmark");
            JsonSerializer.Serialize(writer, report.Benchmark, Options);

            writer.WriteStartObject("candidateIdentity");
            writer.WriteString("instrument", report.Candidate.Instrument);
            writer.WriteStartObject("source");
            writer.WriteString("fileName", report.Candidate.Source.FileName);
            writer.WriteNumber("byteSize", report.Candidate.Source.ByteSize);
            writer.WriteString("sha256", report.Candidate.Source.Sha256);
            writer.WriteEndObject();
            writer.WritePropertyName("context");
            JsonSerializer.Serialize(writer, report.Candidate.Context, Options);
            writer.WriteEndObject();

            // Configuration
            writer.WritePropertyName("configuration");
            JsonSerializer.Serialize(writer, report.Configuration, Options);

            // Coverage
            writer.WritePropertyName("comparisonCoverage");
            WriteCoverage(writer, report.Coverage);

            // Missing and Extra timestamps (T076)
            writer.WritePropertyName("missingFromCandidateTimestamps");
            WriteTimestampArray(writer, report.MissingFromCandidateTimestamps);
            writer.WritePropertyName("extraInCandidateTimestamps");
            WriteTimestampArray(writer, report.ExtraInCandidateTimestamps);
            writer.WritePropertyName("missingFromCandidateRecords");
            WriteAlignmentReferences(writer, report.MissingFromCandidateRecords);
            writer.WritePropertyName("extraInCandidateRecords");
            WriteAlignmentReferences(writer, report.ExtraInCandidateRecords);

            writer.WriteStartArray("contextWarnings");
            foreach (var warning in report.ContextWarnings)
                writer.WriteStringValue(warning);
            writer.WriteEndArray();

            // Material Discrepancies
            writer.WritePropertyName("materialDiscrepancies");
            WriteDiscrepancies(writer, report.MaterialDiscrepancies);

            // Tolerated Summary
            writer.WritePropertyName("toleratedSummary");
            WriteToleratedSummary(writer, report.ToleratedSummary);

            // Agreement Score
            writer.WritePropertyName("agreementScore");
            WriteAgreementScore(writer, report.AgreementScore);

            writer.WritePropertyName("candidateScore");
            if (report.CandidateScore is null)
                writer.WriteNullValue();
            else
                JsonSerializer.Serialize(writer, report.CandidateScore, Options);

            writer.WriteEndObject();
        }

        private static object BuildBenchmarkComparisonSection(ComparisonReport report)
        {
            return new
            {
                contractVersion = 1,
                benchmark = report.Benchmark,
                candidateIdentity = new
                {
                    instrument = report.Candidate.Instrument,
                    source = new
                    {
                        fileName = report.Candidate.Source.FileName,
                        byteSize = report.Candidate.Source.ByteSize,
                        sha256 = report.Candidate.Source.Sha256
                    },
                    context = report.Candidate.Context
                },
                configuration = new
                {
                    benchmarkName = report.Configuration.BenchmarkName,
                    fields = report.Configuration.Fields,
                    timestampMode = report.Configuration.TimestampMode.ToString()
                },
                comparisonCoverage = new
                {
                    benchmarkRecordCount = report.Coverage.BenchmarkRecordCount,
                    candidateRecordCount = report.Coverage.CandidateRecordCount,
                    matchedCount = report.Coverage.MatchedCount,
                    missingFromCandidateCount = report.Coverage.MissingFromCandidateCount,
                    extraInCandidateCount = report.Coverage.ExtraInCandidateCount,
                    overlappingRange = report.Coverage.OverlappingRangeStart.HasValue
                        ? new
                        {
                            start = report.Coverage.OverlappingRangeStart.Value,
                            end = report.Coverage.OverlappingRangeEnd!.Value
                        }
                        : null
                },
                missingFromCandidateTimestamps = report.MissingFromCandidateTimestamps,
                extraInCandidateTimestamps = report.ExtraInCandidateTimestamps,
                missingFromCandidateRecords = report.MissingFromCandidateRecords,
                extraInCandidateRecords = report.ExtraInCandidateRecords,
                contextWarnings = report.ContextWarnings,
                materialDiscrepancies = report.MaterialDiscrepancies,
                toleratedSummary = report.ToleratedSummary,
                candidateScore = report.CandidateScore,
                agreementScore = new
                {
                    score = report.AgreementScore.Score.HasValue
                        ? new
                        {
                            exact = report.AgreementScore.Score.Value.Exact.ToString(),
                            rounded = report.AgreementScore.Score.Value.Format()
                        }
                        : null,
                    formula = report.AgreementScore.Formula,
                    matchedPopulation = report.AgreementScore.MatchedPopulation,
                    materialDiscrepancyTimestamps = report.AgreementScore.MaterialDiscrepancyCount,
                    unavailableReason = report.AgreementScore.UnavailableReason
                }
            };
        }

        private static void WriteCoverage(Utf8JsonWriter writer, ComparisonCoverage coverage)
        {
            writer.WriteStartObject();
            writer.WriteNumber("benchmarkRecordCount", coverage.BenchmarkRecordCount);
            writer.WriteNumber("candidateRecordCount", coverage.CandidateRecordCount);
            writer.WriteNumber("matchedCount", coverage.MatchedCount);
            writer.WriteNumber("missingFromCandidateCount", coverage.MissingFromCandidateCount);
            writer.WriteNumber("extraInCandidateCount", coverage.ExtraInCandidateCount);
            WriteRate(writer, "matchedBenchmarkRate", coverage.MatchedCount, coverage.BenchmarkRecordCount);
            WriteRate(writer, "missingBenchmarkRate", coverage.MissingFromCandidateCount, coverage.BenchmarkRecordCount);
            WriteRate(writer, "matchedCandidateRate", coverage.MatchedCount, coverage.CandidateRecordCount);
            WriteRate(writer, "extraCandidateRate", coverage.ExtraInCandidateCount, coverage.CandidateRecordCount);

            if (coverage.OverlappingRangeStart.HasValue && coverage.OverlappingRangeEnd.HasValue)
            {
                writer.WriteStartObject("overlappingRange");
                writer.WriteString("start", coverage.OverlappingRangeStart.Value);
                writer.WriteString("end", coverage.OverlappingRangeEnd.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        private static void WriteDiscrepancies(Utf8JsonWriter writer, IReadOnlyList<FieldDiscrepancy> discrepancies)
        {
            writer.WriteStartArray();

            foreach (var d in discrepancies)
            {
                writer.WriteStartObject();
                writer.WriteString("timestampUtc", ToUtcZ(d.TimestampUtc));
                writer.WriteString("field", d.Field.ToString());
                writer.WriteString("benchmarkValue", d.BenchmarkValue.ToString(CultureInfo.InvariantCulture));
                writer.WriteString("candidateValue", d.CandidateValue.ToString(CultureInfo.InvariantCulture));
                writer.WriteString("difference", d.Difference.ToString(CultureInfo.InvariantCulture));
                writer.WriteString("directionalDifference", d.DirectionalDifference.ToString(CultureInfo.InvariantCulture));
                writer.WriteString("resolvedAbsoluteTolerance", d.ResolvedAbsoluteTolerance.ToString(CultureInfo.InvariantCulture));
                writer.WriteString("resolvedRelativeTolerance", d.ResolvedRelativeTolerance.ToString(CultureInfo.InvariantCulture));
                writer.WriteString("toleranceDecision", d.ToleranceDecision.GetType().Name);
                if (d.CandidateSourceLine.HasValue)
                    writer.WriteNumber("candidateSourceLine", d.CandidateSourceLine.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        private static void WriteToleratedSummary(Utf8JsonWriter writer, IReadOnlyList<ToleratedDifferenceAggregate> summary)
        {
            writer.WriteStartArray();

            foreach (var s in summary)
            {
                writer.WriteStartObject();
                writer.WriteString("field", s.Field.ToString());
                writer.WriteNumber("totalCompared", s.TotalCompared);
                writer.WriteNumber("acceptedCount", s.AcceptedCount);
                writer.WriteNumber("acceptedByAbsoluteCount", s.AcceptedByAbsoluteCount);
                writer.WriteNumber("acceptedByRelativeCount", s.AcceptedByRelativeCount);
                writer.WriteNumber("materialCount", s.MaterialCount);
                WriteRate(writer, "toleratedRate", s.AcceptedCount, s.TotalCompared);
                WriteRate(writer, "materialRate", s.MaterialCount, s.TotalCompared);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        private static void WriteTimestampArray(Utf8JsonWriter writer, IReadOnlyList<DateTimeOffset> timestamps)
        {
            writer.WriteStartArray();
            foreach (var ts in timestamps)
            {
                writer.WriteStringValue(ToUtcZ(ts));
            }
            writer.WriteEndArray();
        }

        private static void WriteAlignmentReferences(
            Utf8JsonWriter writer,
            IReadOnlyList<TimestampAlignmentReference> references)
        {
            writer.WriteStartArray();
            foreach (var reference in references)
            {
                writer.WriteStartObject();
                writer.WriteString("timestampUtc", ToUtcZ(reference.TimestampUtc));
                if (reference.BenchmarkSourceLine.HasValue)
                    writer.WriteNumber("benchmarkSourceLine", reference.BenchmarkSourceLine.Value);
                if (reference.CandidateSourceLine.HasValue)
                    writer.WriteNumber("candidateSourceLine", reference.CandidateSourceLine.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        private static void WriteRate(Utf8JsonWriter writer, string propertyName, long numerator, long denominator)
        {
            if (denominator == 0)
            {
                writer.WriteNull(propertyName);
                return;
            }

            writer.WriteString(propertyName, (numerator / (decimal)denominator).ToString(CultureInfo.InvariantCulture));
        }

        private static void WriteAgreementScore(Utf8JsonWriter writer, BenchmarkAgreementScore score)
        {
            writer.WriteStartObject();

            if (score.Score.HasValue)
            {
                writer.WriteStartObject("score");
                writer.WriteString("exact", score.Score.Value.Exact.ToString());
                writer.WriteString("rounded", score.Score.Value.Format());
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNull("score");
            }

            writer.WriteString("formula", score.Formula);
            writer.WriteNumber("matchedPopulation", score.MatchedPopulation);
            writer.WriteNumber("materialDiscrepancyTimestamps", score.MaterialDiscrepancyCount);

            if (score.UnavailableReason is not null)
                writer.WriteString("unavailableReason", score.UnavailableReason);

            writer.WriteEndObject();
        }

        /// <summary>
        /// Formats a DateTimeOffset as deterministic UTC Z-suffix text (FR-031, FR-032).
        /// Ensures byte-identical output for the same input value.
        /// </summary>
        private static string ToUtcZ(DateTimeOffset value) =>
            value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }
}
