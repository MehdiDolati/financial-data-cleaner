using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Ingestion;
using Validator.Domain.Candles;

namespace Validator.Infrastructure.Csv
{
    public sealed class CsvCandleSource : ICandleSource
    {
        private readonly string _path;
        private readonly CsvInputOptions _options;

        public CsvCandleSource(string path)
            : this(path, new CsvInputOptions())
        {
        }

        public CsvCandleSource(string path, CsvInputOptions? options)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _options = options ?? new CsvInputOptions();
            _options.Validate();
        }

        public async IAsyncEnumerable<PriceCandle> ReadAllAsync()
        {
            if (!File.Exists(_path))
                throw new FileNotFoundException($"CSV input file not found: {_path}", _path);

            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var headerIndices = default(Dictionary<string, int>);
            var delimiter = ParseDelimiter(_options.Delimiter);

            string? line;
            var headerRead = false;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var columns = SplitLine(line, delimiter);
                if (!headerRead && _options.HasHeader)
                {
                    headerIndices = HeaderLayoutResolver.Resolve(columns, "timestamp", "open", "high", "low", "close", "volume");
                    headerRead = true;
                    continue;
                }

                if (!headerRead && !_options.HasHeader)
                {
                    headerRead = true;
                }

                if (columns.Length < 6)
                    continue;

                PriceCandle? candle = null;
                if (_options.HasHeader && headerIndices is not null)
                {
                    candle = ParseHeaderRow(columns, headerIndices, _options);
                }
                else
                {
                    candle = ParseLegacyRow(columns, _options); 
                }

                if (candle is not null)
                    yield return candle;
            }
        }

        private static char ParseDelimiter(string delimiter)
        {
            return delimiter.Trim() switch
            {
                ";" or "semicolon" => ';',
                "\t" or "tab" => '\t',
                "," or "comma" => ',',
                _ => delimiter.Length == 1 ? delimiter[0] : throw new InvalidOperationException($"Unsupported delimiter '{delimiter}'.")
            };
        }

        private static string[] SplitLine(string line, char delimiter)
        {
            return line.Split(delimiter);
        }

        private static PriceCandle? ParseHeaderRow(string[] columns, Dictionary<string, int> headerIndices, CsvInputOptions options)
        {
            try
            {
                var timestampIndex = headerIndices["timestamp"];
                var openIndex = headerIndices["open"];
                var highIndex = headerIndices["high"];
                var lowIndex = headerIndices["low"];
                var closeIndex = headerIndices["close"];
                var volumeIndex = headerIndices["volume"];

                var timestamp = ParseTimestamp(columns[timestampIndex], options);
                return new PriceCandle(
                    timestamp,
                    decimal.Parse(columns[openIndex].Trim(), CultureInfo.InvariantCulture),
                    decimal.Parse(columns[highIndex].Trim(), CultureInfo.InvariantCulture),
                    decimal.Parse(columns[lowIndex].Trim(), CultureInfo.InvariantCulture),
                    decimal.Parse(columns[closeIndex].Trim(), CultureInfo.InvariantCulture),
                    decimal.Parse(columns[volumeIndex].Trim(), CultureInfo.InvariantCulture));
            }
            catch
            {
                return null;
            }
        }

        private static PriceCandle? ParseLegacyRow(string[] columns, CsvInputOptions options)
        {
            if (columns.Length < 7)
            {
                return null;
            }

            try
            {
                var date = columns[0].Trim();
                var time = columns[1].Trim();
                var timestamp = ParseTimestamp(date, time, options);

                return new PriceCandle(
                    timestamp,
                    decimal.Parse(columns[2].Trim(), CultureInfo.InvariantCulture),
                    decimal.Parse(columns[3].Trim(), CultureInfo.InvariantCulture),
                    decimal.Parse(columns[4].Trim(), CultureInfo.InvariantCulture),
                    decimal.Parse(columns[5].Trim(), CultureInfo.InvariantCulture),
                    decimal.Parse(columns[6].Trim(), CultureInfo.InvariantCulture));
            }
            catch
            {
                return null;
            }
        }

        private static DateTimeOffset ParseTimestamp(string value, CsvInputOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.TimestampFormat) && !string.IsNullOrWhiteSpace(options.TimestampColumn))
            {
                var parsed = DateTimeOffset.ParseExact(value.Trim(), options.TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                if (options.TzOffset is not null)
                {
                    return SourceOffsetConverter.NormalizeToUtc(parsed.DateTime, options.TzOffset.Value);
                }

                return parsed;
            }

            if (!string.IsNullOrWhiteSpace(options.TimestampFormat))
            {
                var parsed = DateTimeOffset.ParseExact(value.Trim(), options.TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                return options.TzOffset is null ? parsed : SourceOffsetConverter.NormalizeToUtc(parsed.DateTime, options.TzOffset.Value);
            }

            throw new InvalidOperationException("Timestamp parsing requires a valid combined timestamp specification.");
        }

        private static DateTimeOffset ParseTimestamp(string date, string time, CsvInputOptions options)
        {
            var dateFormat = options.DateFormat ?? "yyyy.MM.dd";
            var timeFormat = options.TimeFormat ?? "HH:mm";
            var offset = options.TzOffset ?? TimeSpan.Zero;

            var dateValue = DateTime.ParseExact(date.Trim(), dateFormat, CultureInfo.InvariantCulture);
            var timeValue = DateTime.ParseExact(time.Trim(), timeFormat, CultureInfo.InvariantCulture);
            var timestamp = new DateTime(dateValue.Year, dateValue.Month, dateValue.Day, timeValue.Hour, timeValue.Minute, 0);
            return SourceOffsetConverter.NormalizeToUtc(timestamp, offset);
        }
    }
}