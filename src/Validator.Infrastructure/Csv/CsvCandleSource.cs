using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Domain.Candles;

namespace Validator.Infrastructure.Csv
{
    public sealed class CsvCandleSource : ICandleSource
    {
        private readonly string _path;

        public CsvCandleSource(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public async IAsyncEnumerable<PriceCandle> ReadAllAsync()
        {
            if (!File.Exists(_path))
                throw new FileNotFoundException($"CSV input file not found: {_path}", _path);

            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var firstLine = true;
            while (true)
            {
                var line = await reader.ReadLineAsync();
                if (line is null)
                    break;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (firstLine && (line.Contains("Date", StringComparison.OrdinalIgnoreCase) || line.Contains("time", StringComparison.OrdinalIgnoreCase)))
                {
                    firstLine = false;
                    continue;
                }

                firstLine = false;
                var columns = line.Split(',');
                if (columns.Length < 7)
                    continue;

                var date = columns[0].Trim();
                var time = columns[1].Trim();
                var candle = ParseCandle(date, time, columns[2], columns[3], columns[4], columns[5], columns[6]);
                if (candle is not null)
                    yield return candle;
            }
        }

        private static PriceCandle? ParseCandle(string date, string time, string open, string high, string low, string close, string volume)
        {
            try
            {
                var dateValue = DateTime.ParseExact(date.Trim(), "yyyy.MM.dd", CultureInfo.InvariantCulture);
                var timeValue = DateTime.ParseExact(time.Trim(), "HH:mm", CultureInfo.InvariantCulture);
                var timestamp = new DateTimeOffset(dateValue.Year, dateValue.Month, dateValue.Day, timeValue.Hour, timeValue.Minute, 0, TimeSpan.Zero);

                return new PriceCandle(
                    timestamp,
                    decimal.Parse(open, CultureInfo.InvariantCulture),
                    decimal.Parse(high, CultureInfo.InvariantCulture),
                    decimal.Parse(low, CultureInfo.InvariantCulture),
                    decimal.Parse(close, CultureInfo.InvariantCulture),
                    decimal.Parse(volume, CultureInfo.InvariantCulture));
            }
            catch
            {
                return null;
            }
        }
    }
}