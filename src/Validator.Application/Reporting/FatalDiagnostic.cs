using System;
using System.Collections.Generic;
using System.Linq;
using Validator.Application.Ingestion;
using Validator.Domain.Findings;

namespace Validator.Application.Reporting
{
    public enum FailureClass
    {
        Dataset = 0,
        Configuration = 1,
        Operational = 2
    }

    public enum FailureStage
    {
        ArgumentValidation = 0,
        SourceIdentity = 1,
        Ingestion = 2,
        TimeframeResolution = 3,
        Validation = 4,
        Reconciliation = 5,
        ReportRendering = 6,
        ReportCommit = 7
    }

    // Only trustworthy source fields already established at failure time.
    public sealed record PartialSourceIdentity
    {
        public string FileName { get; }
        public long? ByteSize { get; }
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