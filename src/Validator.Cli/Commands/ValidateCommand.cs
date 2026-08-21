using System.Globalization;
using System.Text;
using Validator.Application.Abstractions;
using Validator.Application.Benchmark;
using Validator.Application.Comparison;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Scoring;
using Validator.Application.Validation;

using Validator.Domain.Calendars;
using Validator.Domain.Timeframes;
using Validator.Domain.Candles;
using Validator.Domain.Comparison;
using Validator.Infrastructure.Benchmark;
using Validator.Infrastructure.Calendars;
using Validator.Infrastructure.Csv;
using Validator.Infrastructure.Findings;
using Validator.Infrastructure.Reporting;
using Validator.Infrastructure.Sorting;

namespace Validator.Cli.Commands;

public static class ValidateCommand
{
    private const string HelpText = """
        Usage: validator <input-file> [options]

        Options:
          --timeframe <M<n>|H<n>|D<n>>        Override timeframe detection
          --market <forex|equities|crypto|custom>
                                              Select market calendar (default: forex)
          --calendar <path>                   Custom calendar or equities-hours override
          --date-format <format>              Date column format (default: yyyy.MM.dd)
          --time-format <format>              Time column format (default: HH:mm[:ss])
          --timestamp-format <format>         Combined timestamp format
          --timestamp-column <name-or-index>  Combined timestamp column selector
          --tz-offset <+HH:mm|-HH:mm>          Fixed source offset (default: +02:00)
          --delimiter <comma|semicolon|tab|char>
                                              Delimiter override (default: auto-detect)
          --header                            Match columns by header name
          --format <text|json>                Report format (default: text)
          --report-version <1|2>              JSON contract version (default: 1)
          --output <path>                     Write report atomically to a file
          --verbose                           Append finding details in text mode
          --score                             Report per-metric quality scores and one dataset average
          --score-weights <list>              Override the average's weighting (requires --score)
          --benchmark <name>                  Establish a named benchmark from the validated dataset
          --benchmark-dir <path>              Benchmark storage directory (default: ./benchmarks/)
          --benchmark-delete <name>           Delete a stored benchmark
          --yes                               Skip confirmation prompt for benchmark deletion
          --compare <benchmark-name>           Compare candidate against a stored benchmark
          --tolerances <json>                  Custom per-field tolerance overrides (JSON)
          --help                              Show this help

        JSON scoring requires '--report-version 2'; '--score' with the version 1 JSON contract is rejected.
        '--benchmark' requires '--score' and '--report-version 2'.
        '--compare' requires '--score' and '--report-version 2'.
        '--benchmark-delete' does not require an input file.

        Examples:
          validator EURUSD_H1.csv
          validator EURUSD_M15.csv --header --format json
          validator EURUSD_H1.csv --format json --report-version 2
          validator EURUSD_H1.csv --timeframe H1 --score
          validator prices.csv --timestamp-format "yyyy-MM-dd HH:mm:ss" --timestamp-column 1 --tz-offset +00:00
          validator equities.csv --market equities --timeframe M30 --verbose
          validator custom.csv --market custom --calendar market-hours.json --output report.json --format json
          validator EURUSD_H1.csv --benchmark audusd-d1 --score --report-version 2 --format json
          validator --benchmark-delete audusd-d1 --yes
        """;


    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Input file is required. Use --help for usage.");
            return 2;
        }

        if (args[0] is "--help" or "-h")
        {
            Console.Out.WriteLine(HelpText);
            return 0;
        }

        // --benchmark-delete doesn't need an input file, but ParseArguments
        // requires one. Detect it early so we can handle it specially.
        var hasBenchmarkDelete = args.Any(a => a == "--benchmark-delete");

        ParsedArguments parsed;
        try
        {
            parsed = ParseArguments(args);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            // The requested contract version is read straight from the raw
            // arguments, so an option mistake in a v2 run is still answered
            // with one structured document instead of free-form text.
            return PrefersDetailedV2(args)
                ? WriteFatal(new FatalDiagnostic(
                    "INVALID_ARGUMENT",
                    "The supplied options could not be applied to this run.",
                    exception.Message))
                : WriteText(exception.Message);
        }

        // --benchmark-delete is a standalone action that doesn't need an input file
        if (parsed.BenchmarkDelete is not null)
        {
            return await RunBenchmarkDeleteAsync(parsed).ConfigureAwait(false);
        }

        // --benchmark requires --score with a v2 JSON report to establish a benchmark
        if (parsed.BenchmarkName is not null && parsed.Score is null)
        {
            return Fail(parsed, new FatalDiagnostic(
                "INVALID_ARGUMENT",
                "Option '--benchmark' requires '--score'.",
                "Add '--score --report-version 2 --format json' to enable benchmark establishment.",
                null));
        }

        // --compare requires --score
        if (parsed.CompareBenchmark is not null && parsed.Score is null)
        {
            return Fail(parsed, new FatalDiagnostic(
                "INVALID_ARGUMENT",
                "Option '--compare' requires '--score'.",
                "Add '--score --report-version 2 --format json' to enable benchmark comparison.",
                null));
        }

        // Version selection is resolved before unrelated options, so any later
        // source or configuration failure can still be reported as one
        // structured v2 document. Verbose text is the same detailed report in a
        // human-readable shape, so it runs through the same pipeline and keeps
        // the existing actionable text diagnostic on a fatal outcome.
        if (parsed.ReportVersion == 2 ||
            ((parsed.Verbose || parsed.Score is not null) && parsed.Format == ReportFormat.Text))
        {
            // Scored text needs the detailed pipeline's populations and check
            // statuses, so it routes through the same path as verbose text.
            return await RunDetailedAsync(parsed).ConfigureAwait(false);
        }


        try
        {
            var calendar = new MarketCalendarFactory().Create(
                new LocalCalendarRequest(parsed.Market, parsed.CalendarPath));

            if (!File.Exists(parsed.InputPath))
            {
                throw new FileNotFoundException($"Input file not found: {parsed.InputPath}", parsed.InputPath);
            }

            if (parsed.OutputPath is not null &&
                string.Equals(
                    Path.GetFullPath(parsed.InputPath),
                    Path.GetFullPath(parsed.OutputPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Input and output paths must be different files.");
            }

            IReportWriter writer = parsed.Format == ReportFormat.Json
                ? new JsonReportWriter()
                : new TextReportWriter(parsed.Verbose);
            var source = new CsvCandleSource(parsed.InputPath, parsed.CsvOptions);
            var useCase = new ValidateMarketDataUseCase(source, writer);
            var request = new ValidationRequest(
                parsed.InputPath,
                parsed.Timeframe,
                parsed.Format,
                parsed.OutputPath,
                parsed.Verbose,
                calendar);

            var exitCode = await useCase.ExecuteAsync(request).ConfigureAwait(false);
            var reportText = GetReportText(writer);
            if (parsed.OutputPath is null)
            {
                Console.Out.WriteLine(reportText);
            }
            else
            {
                WriteAtomically(parsed.OutputPath, reportText);
                var findings = exitCode == 0 ? 0 : ExtractFindingCount(writer);
                Console.Out.WriteLine(
                    $"Validation complete: findings={findings}; clean={(exitCode == 0).ToString().ToLowerInvariant()}; report={parsed.OutputPath}");
            }

            return exitCode;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            FormatException or
            InvalidOperationException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException or
            DecoderFallbackException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static ParsedArguments ParseArguments(IReadOnlyList<string> args)
    {
        string? inputPath = args.Count > 0 && !args[0].StartsWith('-') ? args[0] : null;
        string? timeframe = null;
        var format = ReportFormat.Text;
        string? outputPath = null;
        var verbose = false;
        var market = MarketProfile.Forex;
        string? calendarPath = null;
        var csv = new CsvInputOptions();
        var reportVersion = 1;
        var score = false;
        string? scoreWeights = null;
        string? benchmarkName = null;
        string? benchmarkDir = null;
        string? benchmarkDelete = null;
        var yes = false;
        string? compareBenchmark = null;
        string? tolerances = null;

        var startIndex = inputPath is not null ? 1 : 0;
        for (var index = startIndex; index < args.Count; index++)
        {
            var option = args[index];
            switch (option)
            {
                case "--score":
                    score = true;
                    break;
                case "--score-weights":
                    scoreWeights = RequireValue(args, ref index, option);
                    break;

                case "--timeframe":
                    timeframe = RequireValue(args, ref index, option);
                    Timeframe.Parse(timeframe);
                    break;
                case "--market":
                    market = ParseMarket(RequireValue(args, ref index, option));
                    break;
                case "--calendar":
                    calendarPath = RequireValue(args, ref index, option);
                    break;
                case "--date-format":
                    csv = csv with { DateFormat = RequireValue(args, ref index, option) };
                    break;
                case "--time-format":
                    csv = csv with { TimeFormat = RequireValue(args, ref index, option) };
                    break;
                case "--timestamp-format":
                    csv = csv with { TimestampFormat = RequireValue(args, ref index, option) };
                    break;
                case "--timestamp-column":
                    csv = csv with { TimestampColumn = RequireValue(args, ref index, option) };
                    break;
                case "--tz-offset":
                    csv = csv with { TzOffset = ParseOffset(RequireValue(args, ref index, option)) };
                    break;
                case "--delimiter":
                    csv = csv with { Delimiter = RequireValue(args, ref index, option) };
                    break;
                case "--header":
                    csv = csv with { HasHeader = true };
                    break;
                case "--format":
                    format = ParseFormat(RequireValue(args, ref index, option));
                    break;
                case "--report-version":
                    reportVersion = ParseReportVersion(RequireValue(args, ref index, option));
                    break;
                case "--output":
                    outputPath = RequireValue(args, ref index, option);
                    break;
                case "--verbose":
                case "-v":
                    verbose = true;
                    break;
                case "--benchmark":
                    benchmarkName = RequireValue(args, ref index, option);
                    break;
                case "--benchmark-dir":
                    benchmarkDir = RequireValue(args, ref index, option);
                    break;
                case "--benchmark-delete":
                    benchmarkDelete = RequireValue(args, ref index, option);
                    break;
                case "--compare":
                    compareBenchmark = RequireValue(args, ref index, option);
                    break;
                case "--tolerances":
                    tolerances = RequireValue(args, ref index, option);
                    break;
                case "--yes":
                case "-y":
                    yes = true;
                    break;
                case "--help":
                case "-h":
                    // Handled earlier in RunAsync
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{option}'. Use --help for usage.");
            }
        }

        // --benchmark-delete is a standalone action — skip remaining validation
        if (benchmarkDelete is not null)
        {
            return new ParsedArguments(
                inputPath ?? string.Empty,
                timeframe, format, outputPath, verbose, market, calendarPath,
                csv, reportVersion, Score: null,
                BenchmarkName: null,
                BenchmarkDir: benchmarkDir ?? "./benchmarks/",
                BenchmarkDelete: benchmarkDelete,
                Yes: yes,
                CompareBenchmark: null,
                Tolerances: null);
        }

        if (inputPath is null)
        {
            throw new ArgumentException("Input file is required. Use --help for usage.");
        }

        // A contract version is only meaningful for JSON, so a text request that
        // also selects v2 is contradictory rather than silently ignored.
        if (reportVersion == 2 && format != ReportFormat.Json)
        {
            throw new ArgumentException(
                "Option '--report-version 2' is valid only with '--format json'.");
        }

        // Weights are meaningless without opting into scoring, so supplying them
        // alone is a configuration error rather than a silent no-op.
        if (scoreWeights is not null && !score)
        {
            throw new ArgumentException(
                "Option '--score-weights' requires '--score'.");
        }

        // Scores are only available under the v2 JSON contract, so requesting
        // them with the frozen v1 contract fails fast and names the option
        // combination needed to obtain scores. This is checked before the
        // source is opened.
        if (score && format == ReportFormat.Json && reportVersion != 2)
        {
            throw new ArgumentException(
                "Option '--score' is not available with the version 1 JSON contract. Use '--format json --report-version 2' to obtain scores.");
        }

        // Weight parsing and full validation are a pure function of the request,
        // so they run here, before any dataset content is read. A rejected
        // weighting therefore produces no report.
        ScoreRequest? scoreRequest = null;
        if (score)
        {
            scoreRequest = scoreWeights is null
                ? ScoreRequest.Default()
                : new ScoreRequest(ScoreWeightParser.Parse(scoreWeights));
        }

        csv.Validate();
        return new ParsedArguments(
            inputPath,
            timeframe,
            format,
            outputPath,
            verbose,
            market,
            calendarPath,
            csv,
            reportVersion,
            Score: scoreRequest,
            BenchmarkName: benchmarkName,
            BenchmarkDir: benchmarkDir ?? "./benchmarks/",
            BenchmarkDelete: null,
            Yes: yes,
            CompareBenchmark: compareBenchmark,
            Tolerances: tolerances);
    }


    // Runs one detailed validation. A successful report is staged and committed
    // to the destination (or stdout) as v2 JSON or as detailed text; a fatal
    // outcome produces exactly one diagnostic on stderr in the shape the
    // selected representation requires, and leaves stdout empty.
    private static async Task<int> RunDetailedAsync(ParsedArguments parsed)
    {
        IMarketCalendar calendar;
        try
        {
            calendar = new MarketCalendarFactory().Create(
                new LocalCalendarRequest(parsed.Market, parsed.CalendarPath));
        }
        catch (Exception exception) when (exception is
            ArgumentException or FormatException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return Fail(parsed, new FatalDiagnostic(
                "INVALID_CALENDAR",
                "The requested market calendar could not be resolved.",
                exception.Message));
        }

        if (!File.Exists(parsed.InputPath))
        {
            return Fail(parsed, new FatalDiagnostic(
                "SOURCE_UNAVAILABLE",
                "The validated source could not be opened for reading.",
                $"Verify that the input file exists and is readable: {Path.GetFileName(parsed.InputPath)}",
                new PartialSourceIdentity(Path.GetFileName(parsed.InputPath))));
        }

        if (parsed.OutputPath is not null &&
            string.Equals(
                Path.GetFullPath(parsed.InputPath),
                Path.GetFullPath(parsed.OutputPath),
                StringComparison.OrdinalIgnoreCase))
        {
            return Fail(parsed, new FatalDiagnostic(
                "INVALID_ARGUMENT",
                "The report destination is the same file as the validated input.",
                "Choose a report destination that differs from the input file.",
                new PartialSourceIdentity(Path.GetFileName(parsed.InputPath))));
        }

        using var tempStorage = new TempStorage();
        var source = new PreparedCsvCandleSource(parsed.InputPath, parsed.CsvOptions);
        var useCase = new DetailedValidationOrchestrator(() => new FindingCatalog(
            () => new SpoolWriter(tempStorage),
            path => new SpoolReader(path, path + ".complete"),
            // Child records are canonicalized through bounded external merge runs
            // so peak memory follows the configured chunk size rather than the
            // number of findings, gap length, or duplicate-group size.
            new ExternalMergeSpool(tempStorage)));

        DetailedValidationOutcome outcome;
        try
        {
            outcome = await useCase.ExecuteAsync(new DetailedValidationRequest(
                Path.GetFileName(parsed.InputPath),
                source,
                new ValidationOptions { TimeframeOverride = parsed.Timeframe, Verbose = parsed.Verbose, Score = parsed.Score },

                calendar,
                parsed.CsvOptions)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            InvalidDataException or
            FormatException or
            DecoderFallbackException or
            ArgumentException or
            IOException or
            UnauthorizedAccessException)
        {
            // An unusable dataset is still described in the selected shape, so a
            // consumer never has to parse free-form text off standard error.
            return Fail(parsed, ToFatalDiagnostic(exception, Path.GetFileName(parsed.InputPath)));
        }

        if (outcome is DetailedValidationOutcome.Failed failed)
        {
            return Fail(parsed, failed.Diagnostic);
        }

        var report = ((DetailedValidationOutcome.Succeeded)outcome).Report;
        try
        {
            // Run comparison early if --compare is specified, so the result can be
            // included in the staged v2 report (FR-029, T062).
            ComparisonReport? comparisonReport = null;
            if (parsed.CompareBenchmark is not null)
            {
                comparisonReport = await RunComparisonAsync(parsed, report).ConfigureAwait(false);
                if (comparisonReport is null)
                    return 2; // fatal comparison error
            }

            // Both representations render the same completed catalog through the
            // same staged commit, so neither can publish a partial report.
            var commit = await new StageAndCommitWriter(parsed.OutputPath, parsed.InputPath)
                .PublishAsync(
                    (staged, token) => parsed.ReportVersion == 2
                        ? new DetailedReportV2Writer().WriteAsync(report, comparisonReport, staged, token)
                        : new VerboseReportWriter().WriteAsync(report, staged, token),
                    Console.Out)
                .ConfigureAwait(false);

            if (commit is ReportCommitResult.Failed commitFailed)
            {
                return Fail(parsed, commitFailed.Diagnostic);
            }

            if (parsed.OutputPath is not null)
            {
                Console.Out.WriteLine(
                    $"Validation complete: findings={report.Summary.TotalFindings}; clean={report.Summary.IsClean.ToString().ToLowerInvariant()}; report={parsed.OutputPath}");
            }

            // After successful validation, establish benchmark if requested (FR-001)
            if (parsed.BenchmarkName is not null)
            {
                var benchmarkExitCode = await EstablishBenchmarkAsync(parsed, report).ConfigureAwait(false);
                if (benchmarkExitCode != 0)
                    return benchmarkExitCode;
            }

            // Comparison output was already staged into the v2 report above.
            // Print the comparison summary to stdout.
            if (comparisonReport is not null)
            {
                var comparisonText = parsed.Format == ReportFormat.Json
                    ? new ComparisonJsonReportWriter().Write(comparisonReport)
                    : new ComparisonTextReportWriter().Write(comparisonReport);

                Console.Out.WriteLine();
                Console.Out.WriteLine(comparisonText);

                Console.Out.WriteLine(
                    $"Comparison complete: {comparisonReport.MaterialDiscrepancies.Count} material discrepancies; " +
                    $"agreement={comparisonReport.AgreementScore.Score?.Format() ?? "UNAVAILABLE"}");
            }

            // Advisory comparison: return 0 on success regardless of discrepancy findings.
            // Exit code 2 is reserved for fatal comparison failures only (Q6, FR-026).
            return report.Summary.IsClean ? 0 : 1;
        }
        finally
        {
            await report.Findings.DisposeAsync().ConfigureAwait(false);
        }
    }

    // Establishes a benchmark from the validated report.
    private static async Task<int> EstablishBenchmarkAsync(ParsedArguments parsed, DetailedValidationReport report)
    {
        try
        {
            var benchmarkDir = Path.GetFullPath(parsed.BenchmarkDir!);
            Directory.CreateDirectory(benchmarkDir);
            var store = new FileBenchmarkStore(benchmarkDir);
            var useCase = new EstablishBenchmarkUseCase(store);

            var snapshot = await useCase.ExecuteAsync(
                report,
                parsed.BenchmarkName!,
                Path.GetFullPath(parsed.InputPath)).ConfigureAwait(false);

            Console.Out.WriteLine($"Benchmark established: {snapshot.Name} at {benchmarkDir}/{snapshot.Name}");
            return 0;
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            FileNotFoundException or
            IOException or
            UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Failed to establish benchmark: {exception.Message}");
            return 2;
        }
    }

    // Deletes a stored benchmark by name.
    private static async Task<int> RunBenchmarkDeleteAsync(ParsedArguments parsed)
    {
        var benchmarkDir = Path.GetFullPath(parsed.BenchmarkDir!);
        var name = new BenchmarkName(parsed.BenchmarkDelete!);
        var store = new FileBenchmarkStore(benchmarkDir);

        // Check if the benchmark exists
        if (!await store.ExistsAsync(parsed.BenchmarkDelete!).ConfigureAwait(false))
        {
            Console.Error.WriteLine($"Benchmark '{parsed.BenchmarkDelete}' not found in {benchmarkDir}.");
            return 2;
        }

        // Prompt for confirmation unless --yes
        if (!parsed.Yes)
        {
            Console.Out.Write($"Delete benchmark '{parsed.BenchmarkDelete}'? [y/N] ");
            var input = Console.In.ReadLine()?.Trim().ToLowerInvariant();
            if (input is not ("y" or "yes"))
            {
                Console.Out.WriteLine("Deletion cancelled.");
                return 0;
            }
        }

        var deleted = await store.DeleteAsync(parsed.BenchmarkDelete!).ConfigureAwait(false);
        if (deleted)
        {
            Console.Out.WriteLine($"Benchmark '{parsed.BenchmarkDelete}' deleted.");
            return 0;
        }

        Console.Error.WriteLine($"Benchmark '{parsed.BenchmarkDelete}' could not be deleted.");
        return 2;
    }

    // Classifies an ingestion failure that stopped the run before any report
    // could be produced. The stage and failure class follow from the code.
    private static FatalDiagnostic ToFatalDiagnostic(Exception exception, string fileName)
    {
        var (code, reason, guidance) = exception switch
        {
            DecoderFallbackException => (
                "INVALID_ENCODING",
                "The source bytes are not valid text in the expected encoding.",
                "Re-export the file as UTF-8 or ASCII without invalid byte sequences."),
            InvalidDataException data when data.Message.Contains("Invalid CSV", StringComparison.OrdinalIgnoreCase) => (
                "INVALID_CSV",
                "The source is not parsable as delimited text.",
                data.Message),
            InvalidDataException data => (
                "INVALID_STRUCTURE",
                "The source does not expose the columns the active layout requires.",
                data.Message),
            InvalidOperationException structure => (
                "INVALID_STRUCTURE",
                "The source does not expose the columns the active layout requires.",
                structure.Message),
            IOException or UnauthorizedAccessException => (
                "SOURCE_UNAVAILABLE",
                "The validated source could not be read to completion.",
                exception.Message),
            _ => (
                "INVALID_ARGUMENT",
                "The supplied options cannot be applied to this source.",
                exception.Message)
        };

        return new FatalDiagnostic(code, reason, guidance, new PartialSourceIdentity(fileName));
    }

    // Routes one fatal diagnostic to the shape the selected representation
    // promised: a v2 request receives one structured document, and a text
    // request receives the enriched actionable text diagnostic.
    private static int Fail(ParsedArguments parsed, FatalDiagnostic diagnostic) =>
        parsed.ReportVersion == 2
            ? WriteFatal(diagnostic)
            : WriteText(DescribeFatal(diagnostic));

    private static int WriteFatal(FatalDiagnostic diagnostic)
    {
        Console.Error.Write(new FatalDiagnosticV2Writer().Render(diagnostic));
        return 2;
    }

    // Enriches the existing text diagnostic with the stable code, class, stage,
    // guidance, known source location, and the checks that did not finish.
    private static string DescribeFatal(FatalDiagnostic diagnostic)
    {
        var lines = new List<string>
        {
            diagnostic.Reason,
            $"code={diagnostic.Code}; class={diagnostic.FailureClass}; stage={diagnostic.Stage}",
            $"guidance: {diagnostic.Guidance}"
        };

        if (diagnostic.Source is not null)
        {
            lines.Add($"source: fileName={diagnostic.Source.FileName}");
        }

        if (diagnostic.Location is not null)
        {
            var location = new List<string>();
            if (diagnostic.Location.SourceLine.HasValue)
            {
                location.Add($"line={diagnostic.Location.SourceLine.Value}");
            }

            if (diagnostic.Location.TimestampUtc.HasValue)
            {
                location.Add($"timestampUtc={diagnostic.Location.TimestampUtc.Value.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss'Z'}");
            }

            if (diagnostic.Location.Field is not null)
            {
                location.Add($"field={diagnostic.Location.Field}");
            }

            lines.Add($"location: {string.Join("; ", location)}");
        }

        var unfinished = diagnostic.Checks
            .Where(check => check.Status != CheckStatus.Completed)
            .Select(check => check.Check.ToString())
            .ToArray();
        if (unfinished.Length > 0)
        {
            lines.Add($"checks not completed: {string.Join(", ", unfinished)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static int WriteText(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }

    // Reads the requested contract version from the raw arguments so an option
    // mistake in a v2 run is still answered in the v2 shape. Only an explicit
    // '--report-version 2' opts in; anything else keeps the v1 text behavior.
    private static bool PrefersDetailedV2(IReadOnlyList<string> args)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (args[index] == "--report-version" && args[index + 1].Trim() == "2")
            {
                return true;
            }
        }

        return false;
    }

    private static string RequireValue(IReadOnlyList<string> args, ref int index, string option)
    {
        if (index + 1 >= args.Count)
        {
            throw new ArgumentException($"Option '{option}' requires a value.");
        }

        return args[++index];
    }

    private static MarketProfile ParseMarket(string value) => value.Trim().ToLowerInvariant() switch
    {
        "forex" => MarketProfile.Forex,
        "equities" => MarketProfile.Equities,
        "crypto" => MarketProfile.Crypto,
        "custom" => MarketProfile.Custom,
        _ => throw new ArgumentException(
            $"Unknown market profile '{value}'. Use forex, equities, crypto, or custom.")
    };

    private static ReportFormat ParseFormat(string value) => value.Trim().ToLowerInvariant() switch
    {
        "text" => ReportFormat.Text,
        "json" => ReportFormat.Json,
        _ => throw new ArgumentException($"Unknown report format '{value}'. Use text or json.")
    };

    private static int ParseReportVersion(string value) => value.Trim() switch
    {
        "1" => 1,
        "2" => 2,
        _ => throw new ArgumentException($"Unknown report-version '{value}'. Use 1 or 2.")
    };

    private static TimeSpan ParseOffset(string value)
    {
        if (value.Length == 6 &&
            (value[0] is '+' or '-') &&
            value[3] == ':' &&
            int.TryParse(value.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hours) &&
            int.TryParse(value.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) &&
            hours <= 14 &&
            minutes < 60 &&
            (hours < 14 || minutes == 0))
        {
            var offset = new TimeSpan(hours, minutes, 0);
            return value[0] == '-' ? -offset : offset;
        }

        throw new FormatException($"Invalid timezone offset '{value}'. Use +HH:mm or -HH:mm within +/-14:00.");
    }

    private static string GetReportText(IReportWriter writer) => writer switch
    {
        TextReportWriter text when text.LastWrittenText is not null => text.LastWrittenText,
        JsonReportWriter json when json.LastWrittenText is not null => json.LastWrittenText,
        _ => throw new InvalidOperationException("The report writer produced no output.")
    };

    private static int ExtractFindingCount(IReportWriter writer)
    {
        if (writer is JsonReportWriter json && json.LastWrittenText is not null)
        {
            using var document = System.Text.Json.JsonDocument.Parse(json.LastWrittenText);
            var summary = document.RootElement.GetProperty("summary");
            return summary.EnumerateObject().Sum(property => property.Value.GetInt32());
        }

        if (writer is TextReportWriter text && text.LastWrittenText is not null)
        {
            return text.LastWrittenText
                .Split(Environment.NewLine)
                .Take(6)
                .Sum(line => int.Parse(line[(line.LastIndexOf(':') + 1)..], CultureInfo.InvariantCulture));
        }

        throw new InvalidOperationException("The report writer produced no output.");
    }

    private static void WriteAtomically(string outputPath, string content)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new IOException("Output path must have a writable parent directory.");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine($".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, content, new System.Text.UTF8Encoding(false));
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    // Runs comparison against a stored benchmark and returns the ComparisonReport.
    // Returns null on fatal error (after printing the error to stderr).
    private static async Task<ComparisonReport?> RunComparisonAsync(ParsedArguments parsed, DetailedValidationReport report)
    {
        try
        {
            // Parse tolerances before loading data (FR-019)
            IReadOnlyList<ComparedField>? toleranceOverrides = null;
            if (!string.IsNullOrWhiteSpace(parsed.Tolerances))
            {
                toleranceOverrides = ToleranceResolver.ParseOverrides(parsed.Tolerances);
            }

            var benchmarkDir = Path.GetFullPath(parsed.BenchmarkDir!);
            var store = new FileBenchmarkStore(benchmarkDir);

            // Load benchmark snapshot
            if (!await store.ExistsAsync(parsed.CompareBenchmark!).ConfigureAwait(false))
            {
                Console.Error.WriteLine(
                    $"Benchmark '{parsed.CompareBenchmark}' not found in {benchmarkDir}. " +
                    $"Establish it first with '--benchmark {parsed.CompareBenchmark}'.");
                return null;
            }

            var benchmark = await store.LoadAsync(parsed.CompareBenchmark!).ConfigureAwait(false);

            // Load benchmark source candles
            var benchmarkSourcePath = Path.Combine(benchmarkDir, new BenchmarkName(parsed.CompareBenchmark!).Safe, "source.csv");
            var benchmarkSource = new CsvCandleSource(benchmarkSourcePath);
            var benchmarkCandles = new List<PriceCandle>();
            await foreach (var candle in benchmarkSource.ReadAllAsync().ConfigureAwait(false))
            {
                benchmarkCandles.Add(candle);
            }
            benchmarkCandles.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

            // Load candidate source candles
            var candidateSource = new CsvCandleSource(parsed.InputPath, parsed.CsvOptions);
            var candidateCandles = new List<PriceCandle>();
            await foreach (var candle in candidateSource.ReadAllAsync().ConfigureAwait(false))
            {
                candidateCandles.Add(candle);
            }
            candidateCandles.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

            // Build candidate identity from the validation context
            var candidateIdentity = new CandidateIdentity(
                report.Source,
                report.Context);

            // Run comparison
            var useCase = new CompareDatasetsUseCase();
            return useCase.Compare(
                benchmark,
                benchmarkCandles,
                candidateCandles,
                candidateIdentity,
                toleranceOverrides);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            FileNotFoundException or
            FormatException or
            IOException or
            UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Comparison failed: {exception.Message}");
            return null;
        }
    }

    private sealed record ParsedArguments(
        string InputPath,
        string? Timeframe,
        ReportFormat Format,
        string? OutputPath,
        bool Verbose,
        MarketProfile Market,
        string? CalendarPath,
        CsvInputOptions CsvOptions,
        int ReportVersion,
        ScoreRequest? Score,
        string? BenchmarkName = null,
        string? BenchmarkDir = "./benchmarks/",
        string? BenchmarkDelete = null,
        bool Yes = false,
        string? CompareBenchmark = null,
        string? Tolerances = null);
}
