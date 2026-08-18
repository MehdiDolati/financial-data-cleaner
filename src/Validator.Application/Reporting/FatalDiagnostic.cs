using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Application.Ingestion;
using Validator.Domain.Findings;

namespace Validator.Application.Reporting
{
    /// <summary>
    /// Who or what a fatal failure is attributable to, so a reader knows whether
    /// to fix the data, the invocation, or the environment.
    /// </summary>
    public enum FailureClass
    {
        /// <summary>The source data itself could not be validated as given.</summary>
        Dataset = 0,

        /// <summary>The requested options or configuration were not usable.</summary>
        Configuration = 1,

        /// <summary>The environment failed, for example an unreadable or unwritable path.</summary>
        Operational = 2
    }

    /// <summary>
    /// The point in the run at which processing stopped.
    /// </summary>
    /// <remarks>
    /// Declared in the order the stages execute, so a reader can see how much of
    /// the pipeline had completed before the failure.
    /// </remarks>
    public enum FailureStage
    {
        /// <summary>While checking the supplied options.</summary>
        ArgumentValidation = 0,

        /// <summary>While establishing the source's identity, including its hash.</summary>
        SourceIdentity = 1,

        /// <summary>While reading the source data.</summary>
        Ingestion = 2,

        /// <summary>While determining the timeframe to validate against.</summary>
        TimeframeResolution = 3,

        /// <summary>While running the validation checks.</summary>
        Validation = 4,

        /// <summary>While proving the report's totals agree with its findings.</summary>
        Reconciliation = 5,

        /// <summary>While rendering the report.</summary>
        ReportRendering = 6,

        /// <summary>While publishing the rendered report to its destination.</summary>
        ReportCommit = 7
    }

    /// <summary>
    /// The source facts that were already established when a run failed.
    /// </summary>
    /// <remarks>
    /// Every field beyond the file name is optional because a failure can happen
    /// before the value is known. Absent values are reported as absent rather
    /// than guessed, so a diagnostic never implies knowledge the run never had.
    /// </remarks>
    public sealed record PartialSourceIdentity
    {
        /// <summary>The name of the source that was being validated.</summary>
        public string FileName { get; }

        /// <summary>The source's size, if it had been determined.</summary>
        public long? ByteSize { get; }

        /// <summary>The source's SHA-256 hash, if it had been computed.</summary>
        public string? Sha256 { get; }

        public PartialSourceIdentity(string fileName, long? byteSize = null, string? sha256 = null)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("File name must be a non-empty value.", nameof(fileName));
            }

            if (fileName.IndexOfAny(['/', '\\', ':']) >= 0)
            {
                throw new ArgumentException("File name must be a safe base name without path components.", nameof(fileName));
            }

            if (byteSize < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteSize), "Byte size must be non-negative.");
            }

            if (sha256 is not null && (sha256.Length != 64 || sha256.Any(c => !SourceIdentity.IsLowerHex(c))))
            {
                throw new ArgumentException("SHA-256 must be exactly 64 lower-case hexadecimal characters.", nameof(sha256));
            }

            FileName = fileName;
            ByteSize = byteSize;
            Sha256 = sha256;
        }
    }

    // Optional failure location: line, UTC timestamp, or field name; at least
    // one value must be present.
    public sealed record FailureLocation
    {
        public long? SourceLine { get; }
        public DateTimeOffset? TimestampUtc { get; }
        public string? Field { get; }

        public FailureLocation(long? sourceLine = null, DateTimeOffset? timestampUtc = null, string? field = null)
        {
            if (sourceLine is null && timestampUtc is null && field is null)
            {
                throw new ArgumentException("At least one location value is required.");
            }

            if (sourceLine <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceLine), "Source line must be positive.");
            }

            if (timestampUtc.HasValue && timestampUtc.Value.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("Location timestamp must be UTC.", nameof(timestampUtc));
            }

            SourceLine = sourceLine;
            TimestampUtc = timestampUtc;
            Field = field;
        }
    }

    // Stable fatal codes with their fixed failure class and stage.
    public static class FatalCodeRegistry
    {
        private static readonly Dictionary<string, (FailureClass Class, FailureStage Stage)> Registry = new()
        {
            ["INVALID_ARGUMENT"] = (FailureClass.Configuration, FailureStage.ArgumentValidation),
            ["SOURCE_UNAVAILABLE"] = (FailureClass.Operational, FailureStage.SourceIdentity),
            ["SOURCE_CHANGED"] = (FailureClass.Operational, FailureStage.Ingestion),
            ["INVALID_ENCODING"] = (FailureClass.Dataset, FailureStage.Ingestion),
            ["INVALID_CSV"] = (FailureClass.Dataset, FailureStage.Ingestion),
            ["INVALID_STRUCTURE"] = (FailureClass.Dataset, FailureStage.Ingestion),
            ["AMBIGUOUS_DELIMITER"] = (FailureClass.Configuration, FailureStage.Ingestion),
            ["AMBIGUOUS_TIMEFRAME"] = (FailureClass.Configuration, FailureStage.TimeframeResolution),
            ["INVALID_CALENDAR"] = (FailureClass.Configuration, FailureStage.ArgumentValidation),
            ["VALIDATION_INCOMPLETE"] = (FailureClass.Operational, FailureStage.Validation),
            ["REPORT_RECONCILIATION_FAILED"] = (FailureClass.Operational, FailureStage.Reconciliation),
            ["REPORT_RENDER_FAILED"] = (FailureClass.Operational, FailureStage.ReportRendering),
            ["REPORT_COMMIT_FAILED"] = (FailureClass.Operational, FailureStage.ReportCommit)
        };

        public static bool IsKnown(string code) => Registry.ContainsKey(code);

        public static FailureClass ClassOf(string code) =>
            Registry.TryGetValue(code, out var entry) ? entry.Class : throw new ArgumentException($"Unknown fatal code '{code}'.", nameof(code));

        public static FailureStage StageOf(string code) =>
            Registry.TryGetValue(code, out var entry) ? entry.Stage : throw new ArgumentException($"Unknown fatal code '{code}'.", nameof(code));
    }

    // Non-success outcome of an incomplete validation. Fatal diagnostics
    // contain no final summary, reconciliation, isClean, or complete findings
    // claim, so they can never be mistaken for a successful report.
    public sealed record FatalDiagnostic
    {
        private static readonly CheckName[] AllChecks =
        [
            CheckName.MissingCandles,
            CheckName.DuplicateRecords,
            CheckName.InvalidOhlc,
            CheckName.ClosedMarketRecords,
            CheckName.TimeGaps,
            CheckName.MalformedRows
        ];

        public int ContractVersion { get; init; } = 2;
        public string Status { get; init; } = "Fatal";
        public bool FindingSetComplete { get; init; } = false;
        public string Code { get; }
        public FailureClass FailureClass { get; }
        public FailureStage Stage { get; }
        public string Reason { get; }
        public string Guidance { get; }
        public PartialSourceIdentity? Source { get; }
        public FailureLocation? Location { get; }
        public IReadOnlyList<CheckExecution> Checks { get; }

        public FatalDiagnostic(
            string code,
            string reason,
            string guidance,
            PartialSourceIdentity? source = null,
            FailureLocation? location = null,
            IReadOnlyList<CheckExecution>? checks = null)
        {
            if (!FatalCodeRegistry.IsKnown(code))
            {
                throw new ArgumentException($"Unknown fatal code '{code}'.", nameof(code));
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("Reason must be a non-empty value.", nameof(reason));
            }

            if (string.IsNullOrWhiteSpace(guidance))
            {
                throw new ArgumentException("Guidance must be a non-empty value.", nameof(guidance));
            }

            if (checks is null)
            {
                Checks = AllChecks
                    .Select(check => new CheckExecution(check, CheckStatus.NotCompleted, "Validation did not run."))
                    .ToArray();
            }
            else
            {
                if (checks.Count != 6)
                {
                    throw new ArgumentException("Exactly six check executions are required.", nameof(checks));
                }

                for (var index = 0; index < 6; index++)
                {
                    if (checks[index].Check != AllChecks[index])
                    {
                        throw new ArgumentException(
                            "Checks must appear exactly once in canonical order.",
                            nameof(checks));
                    }
                }

                Checks = checks;
            }

            Code = code;
            FailureClass = FatalCodeRegistry.ClassOf(code);
            Stage = FatalCodeRegistry.StageOf(code);
            Reason = reason;
            Guidance = guidance;
            Source = source;
            Location = location;
        }
    }
}