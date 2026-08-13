using System;
using System.IO;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Ingestion;
using Validator.Application.Validation;
using Validator.Infrastructure.Csv;
using Validator.Infrastructure.Reporting;
using Validator.Infrastructure.Calendars;

namespace Validator.Cli.Commands
{
    public static class ValidateCommand
    {
        public static async Task<int> RunAsync(string[] args)
        {
            if (args.Length == 0 || args[0] is "--help" or "-h")
            {
                Console.WriteLine("Usage: validator <input-file> [--timeframe M1|H1|D1] [--format text|json] [--output <path>] [--verbose]");
                return 0;
            }

            var inputPath = args[0];
            string? timeframe = null;
            var verbose = false;
            ReportFormat format = ReportFormat.Text;
            string? outputPath = null;
            var options = new CsvInputOptions();
            string? marketName = null;
            string? calendarPath = null;

            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--timeframe" when i + 1 < args.Length:
                        timeframe = args[++i];
                        break;
                    case "--format" when i + 1 < args.Length:
                        format = ParseFormat(args[++i]);
                        break;
                    case "--output" when i + 1 < args.Length:
                        outputPath = args[++i];
                        break;
                    case "--verbose":
                    case "-v":
                        verbose = true;
                        break;
                    case "--market" when i + 1 < args.Length:
                        marketName = args[++i];
                        break;
                    case "--calendar" when i + 1 < args.Length:
                        calendarPath = args[++i];
                        break;
                    case "--header":
                        options = options with { HasHeader = true };
                        break;
                    case "--delimiter" when i + 1 < args.Length:
                        options = options with { Delimiter = ParseDelimiter(args[++i]) };
                        break;
                    case "--date-format" when i + 1 < args.Length:
                        options = options with { DateFormat = args[++i] };
                        break;
                    case "--time-format" when i + 1 < args.Length:
                        options = options with { TimeFormat = args[++i] };
                        break;
                    case "--timestamp-format" when i + 1 < args.Length:
                        options = options with { TimestampFormat = args[++i] };
                        break;
                    case "--timestamp-column" when i + 1 < args.Length:
                        options = options with { TimestampColumn = args[++i] };
                        break;
                    case "--tz-offset" when i + 1 < args.Length:
                        options = options with { TzOffset = ParseOffset(args[++i]) };
                        break;
                }
            }

            if (!File.Exists(inputPath))
            {
                Console.Error.WriteLine($"Input file not found: {inputPath}");
                return 2;
            }

            options.Validate();

            // Resolve calendar if provided (log selection). Create IMarketCalendar instance to pass into the request.
            Validator.Application.Abstractions.IMarketCalendar? resolvedCalendar = null;
            if (!string.IsNullOrWhiteSpace(calendarPath) || !string.IsNullOrWhiteSpace(marketName))
            {
                var spec = calendarPath ?? marketName!;
                try
                {
                    var factory = new MarketCalendarFactory();
                    resolvedCalendar = factory.Create(spec);
                    Console.WriteLine($"Using market calendar: {spec}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to resolve market calendar '{spec}': {ex.Message}");
                    return 3;
                }
            }

            var request = new ValidationRequest(inputPath, timeframe, format, outputPath, verbose, resolvedCalendar);
            var source = new CsvCandleSource(inputPath, options);

            IReportWriter writer = format == ReportFormat.Json
                ? new JsonReportWriter()
                : new TextReportWriter(verbose);

            var useCase = new ValidateMarketDataUseCase(source, writer);
            var exitCode = await useCase.ExecuteAsync(request);

            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                if (writer is JsonReportWriter jsonWriter && !string.IsNullOrWhiteSpace(jsonWriter.LastWrittenText))
                {
                    File.WriteAllText(outputPath, jsonWriter.LastWrittenText);
                }
                else if (writer is TextReportWriter textWriter && !string.IsNullOrWhiteSpace(textWriter.LastWrittenText))
                {
                    File.WriteAllText(outputPath, textWriter.LastWrittenText);
                }

                Console.WriteLine($"Report written to {outputPath}");
            }

            return exitCode;
        }

        private static ReportFormat ParseFormat(string value)
        {
            return value.Trim().ToLowerInvariant() switch
            {
                "json" => ReportFormat.Json,
                _ => ReportFormat.Text
            };
        }

        private static string ParseDelimiter(string value)
        {
            return value.Trim().ToLowerInvariant() switch
            {
                "comma" => ",",
                "semicolon" => ";",
                "tab" => "\t",
                _ => value
            };
        }

        private static TimeSpan ParseOffset(string value)
        {
            if (TimeSpan.TryParse(value, out var offset))
            {
                return offset;
            }

            throw new FormatException($"Invalid timezone offset '{value}'. Use a value like +02:00 or -05:00.");
        }
    }
}