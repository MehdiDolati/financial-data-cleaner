using System;
using System.IO;
using Validator.Application.Ingestion;

namespace Validator.Application.Web
{
    /// <summary>
    /// The three operations reachable through the web boundary. They mirror
    /// the three established workflows exactly (FR-001, FR-004).
    /// </summary>
    public enum WebRunOperation
    {
        /// <summary>Run the detailed validation (optionally with scoring).</summary>
        Validate = 0,

        /// <summary>Establish a validated dataset as a named immutable benchmark.</summary>
        EstablishBenchmark = 1,

        /// <summary>Compare a candidate dataset against a named benchmark.</summary>
        Compare = 2
    }

    /// <summary>
    /// Every option that materially affects a result — the same surface the
    /// CLI documents (FR-003). All values are resolved inputs; nothing is
    /// inferred from the host or the environment.
    /// </summary>
    public sealed record WebRunOptions(
        string? Timeframe,
        Domain.Calendars.MarketProfile Market,
        string? CalendarReference,
        CsvInputOptions Csv,
        int ReportVersion,
        bool Score,
        string? ScoreWeights,
        string? Instrument,
        string? BenchmarkName,
        string? ToleranceOverrides)
    {
        /// <summary>
        /// The canonical, culture-invariant, field-ordered serialization of
        /// every material option. This exact string feeds the deterministic
        /// <see cref="WebRunId"/> derivation (data-model.md). The operation
        /// kind participates in identity, so it is passed in by the caller
        /// that knows it.
        /// </summary>
        public string ToCanonicalOptionsString(WebRunOperation operation)
        {
            var csv = Csv ?? new CsvInputOptions();
            return string.Join('\u001F',
                "v1",
                "op=" + Ordinal(OperationCodeOf(operation)),
                "tf=" + Ordinal(Timeframe),
                "mk=" + Ordinal(Market.ToString()),
                "cal=" + Ordinal(CalendarReference),
                "delim=" + Ordinal(csv.Delimiter),
                "hdr=" + Ordinal(csv.HasHeader ? "1" : "0"),
                "dfmt=" + Ordinal(csv.DateFormat),
                "tfmt=" + Ordinal(csv.TimeFormat),
                "tsfmt=" + Ordinal(csv.TimestampFormat),
                "tscol=" + Ordinal(csv.TimestampColumn),
                "tz=" + Ordinal(csv.TzOffset.ToString("c")),
                "rv=" + Ordinal(ReportVersion.ToString()),
                "score=" + Ordinal(Score ? "1" : "0"),
                "weights=" + Ordinal(ScoreWeights),
                "instr=" + Ordinal(Instrument),
                "bench=" + Ordinal(BenchmarkName),
                "tol=" + Ordinal(ToleranceOverrides));

            static string Ordinal(string? value) => value is null ? "\u0000" : "\u0001" + value;
        }

        private static string OperationCodeOf(WebRunOperation operation) => operation switch
        {
            WebRunOperation.Validate => "validate",
            WebRunOperation.EstablishBenchmark => "establish",
            WebRunOperation.Compare => "compare",
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    /// <summary>
    /// The transport-neutral request the façade accepts. Content is a readable
    /// stream of the uploaded bytes — never a filesystem path — which keeps
    /// the boundary transport-neutral and makes upload handling a host
    /// concern (contracts/web-integration-contract.md).
    /// </summary>
    public sealed record WebRunRequest
    {
        public WebRunOperation Operation { get; }

        /// <summary>Untrusted display name; normalized through SourceIdentity (FR-030).</summary>
        public string SubmittedFileName { get; }

        /// <summary>Readable stream of the uploaded bytes.</summary>
        public Stream Content { get; }

        public WebRunOptions Options { get; }

        /// <summary>Opaque host correlation only; never interpreted, never authorization (research R6).</summary>
        public string? SubmittedBy { get; }

        public WebRunRequest(
            WebRunOperation operation,
            string submittedFileName,
            Stream content,
            WebRunOptions options,
            string? submittedBy = null)
        {
            if (string.IsNullOrWhiteSpace(submittedFileName))
            {
                throw new ArgumentException(
                    "Submitted file name must be a non-empty value.", nameof(submittedFileName));
            }

            Operation = operation;
            SubmittedFileName = submittedFileName;
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Options = options ?? throw new ArgumentNullException(nameof(options));
            SubmittedBy = submittedBy;
        }
    }
}