using System.Globalization;
using System.Text;
using Validator.Application.Abstractions;
using Validator.Application.Ingestion;
using Validator.Application.Validation;
using Validator.Domain.Calendars;
using Validator.Domain.Timeframes;
using Validator.Infrastructure.Calendars;
using Validator.Infrastructure.Csv;
using Validator.Infrastructure.Reporting;

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
          --output <path>                     Write report atomically to a file
          --verbose                           Append finding details in text mode
          --help                              Show this help

        Examples:
          validator EURUSD_H1.csv
          validator EURUSD_M15.csv --header --format json
          validator prices.csv --timestamp-format "yyyy-MM-dd HH:mm:ss" --timestamp-column 1 --tz-offset +00:00
          validator equities.csv --market equities --timeframe M30 --verbose
          validator custom.csv --market custom --calendar market-hours.json --output report.json --format json
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

        try
        {
            var parsed = ParseArguments(args);
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
        var inputPath = args[0];
        string? timeframe = null;
        var format = ReportFormat.Text;
        string? outputPath = null;
        var verbose = false;
        var market = MarketProfile.Forex;
        string? calendarPath = null;
        var csv = new CsvInputOptions();

        for (var index = 1; index < args.Count; index++)
        {
            var option = args[index];
            switch (option)
            {
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
                case "--output":
                    outputPath = RequireValue(args, ref index, option);
                    break;
                case "--verbose":
                case "-v":
                    verbose = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{option}'. Use --help for usage.");
            }
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
            csv);
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
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
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

    private sealed record ParsedArguments(
        string InputPath,
        string? Timeframe,
        ReportFormat Format,
        string? OutputPath,
        bool Verbose,
        MarketProfile Market,
        string? CalendarPath,
        CsvInputOptions CsvOptions);
}