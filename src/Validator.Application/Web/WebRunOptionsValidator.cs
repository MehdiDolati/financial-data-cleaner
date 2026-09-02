using System;
using Validator.Application.Reporting;
using Validator.Application.Scoring;
using Validator.Domain.Timeframes;

namespace Validator.Application.Web
{
    /// <summary>
    /// Pre-read option validation (FR-007, SC-003). Every rule reuses the
    /// CLI's established rules and codes - the table in
    /// contracts/web-integration-contract.md - and completes before any
    /// content byte is interpreted, so a rejected configuration produces no
    /// report and no partial work (Principle V).
    /// </summary>
    public static class WebRunOptionsValidator
    {
        /// <summary>
        /// Validates the resolved options for one operation. Returns null
        /// when the configuration is usable, or the INVALID_ARGUMENT fatal
        /// diagnostic naming the specific correction required.
        /// </summary>
        public static FatalDiagnostic? Validate(WebRunOperation operation, WebRunOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            string? problem = null;
            string? guidance = null;

            if (options.ReportVersion is not (1 or 2))
            {
                problem = "The requested report version is not 1 or 2.";
                guidance = "Select report version 1 or 2.";
            }
            else if (options.ScoreWeights is not null && !options.Score)
            {
                problem = "Score weights were supplied without enabling scoring.";
                guidance = "Enable Score to apply score weights.";
            }
            else if (options.Score && options.ReportVersion != 2)
            {
                problem = "Scoring is not available under the version 1 report contract.";
                guidance = "Use report version 2 to obtain scores.";
            }
            else if (operation is WebRunOperation.EstablishBenchmark or WebRunOperation.Compare)
            {
                if (!options.Score)
                {
                    problem = operation == WebRunOperation.EstablishBenchmark
                        ? "Benchmark establishment requires scoring."
                        : "Benchmark comparison requires scoring.";
                    guidance = "Enable Score with report version 2 for benchmark operations.";
                }
                else if (options.ReportVersion != 2)
                {
                    problem = "Benchmark operations require the version 2 report contract.";
                    guidance = "Use report version 2 for benchmark operations.";
                }
                else if (string.IsNullOrWhiteSpace(options.Instrument))
                {
                    problem = "Benchmark operations require an instrument identity.";
                    guidance = "Supply a non-empty instrument identity so dataset identity is unambiguous.";
                }
                else if (options.Instrument.Contains('/') || options.Instrument.Contains('\\'))
                {
                    problem = "The instrument identity must not contain path separators.";
                    guidance = "Supply an instrument identity without '/' or '\\'.";
                }
                else if (string.IsNullOrWhiteSpace(options.BenchmarkName))
                {
                    problem = "Benchmark operations require a benchmark name.";
                    guidance = "Supply the benchmark name to establish or compare against.";
                }
            }
            else
            {
                // A plain validation run has no business carrying benchmark
                // options; rejecting them early prevents a silent mismatch.
                if (!string.IsNullOrWhiteSpace(options.Instrument))
                {
                    problem = "An instrument identity is only meaningful for benchmark operations.";
                    guidance = "Remove the instrument, or select the EstablishBenchmark or Compare operation.";
                }
                else if (!string.IsNullOrWhiteSpace(options.BenchmarkName))
                {
                    problem = "A benchmark name is only meaningful for benchmark operations.";
                    guidance = "Remove the benchmark name, or select the EstablishBenchmark or Compare operation.";
                }
            }

            if (problem is null && options.ToleranceOverrides is not null && operation != WebRunOperation.Compare)
            {
                problem = "Tolerance overrides were supplied without a comparison.";
                guidance = "Tolerance overrides require the Compare operation.";
            }

            if (problem is null && options.Timeframe is not null && !IsCanonicalTimeframe(options.Timeframe))
            {
                problem = "The timeframe override is not a canonical code.";
                guidance = "Use a canonical M<n>, H<n>, or D<n> timeframe code such as M15, H1, or D1.";
            }

            if (problem is null && options.CalendarReference is not null &&
                options.Market != Domain.Calendars.MarketProfile.Custom)
            {
                problem = "A calendar reference requires the custom market profile.";
                guidance = "Select the custom market profile to supply a calendar reference.";
            }

            if (problem is null && options.ScoreWeights is not null)
            {
                // ScoreWeightParser enforces the six-metric coverage, weight
                // grammar, and non-zero total; its message is the correction.
                try
                {
                    ScoreWeightParser.Parse(options.ScoreWeights);
                }
                catch (ScoreWeightFormatException exception)
                {
                    problem = "The supplied score weights are not a valid six-metric weighting.";
                    guidance = exception.Message;
                }
            }

            if (problem is null && options.Csv is not null)
            {
                try
                {
                    options.Csv.Validate();
                }
                catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
                {
                    problem = "The CSV option combination is not self-consistent.";
                    guidance = exception.Message;
                }
            }

            if (problem is null)
            {
                return null;
            }

            return new FatalDiagnostic(
                "INVALID_ARGUMENT",
                problem,
                guidance ?? "Correct the reported option and resubmit.");
        }

        private static bool IsCanonicalTimeframe(string timeframe)
        {
            try
            {
                Timeframe.Parse(timeframe);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }
    }
}