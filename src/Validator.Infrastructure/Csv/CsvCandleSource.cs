using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Validator.Application.Abstractions;
using Validator.Application.Ingestion;
using Validator.Domain.Candles;
using Validator.Domain.Findings;

namespace Validator.Infrastructure.Csv;

public sealed class CsvCandleSource : ICandleSource, IMalformedRowSource
{
    private readonly string _path;
    private readonly CsvInputOptions _options;
    private readonly List<MalformedRow> _malformedRows = [];

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

    public IReadOnlyList<MalformedRow> MalformedRows => _malformedRows;

    // Resolved interpretation facts captured during the last read. These are
    // reported as validation context so a consumer can reproduce the run.
    public char? ResolvedDelimiter { get; private set; }

    public bool ResolvedHasHeader => _options.HasHeader;

    // True when a single combined timestamp column was resolved, false when a
    // separate date and time pair was resolved.
    public bool ResolvedCombinedTimestamp { get; private set; }

    public string? ResolvedDateFormat { get; private set; }

    public string? ResolvedTimeFormat { get; private set; }

    public string? ResolvedTimestampFormat { get; private set; }

    // Resolved header name or one-based column index of a combined timestamp.
    public string? ResolvedTimestampColumn { get; private set; }

    // Every physical data record examined, excluding an optional header row.
    public long PhysicalRowsExamined { get; private set; }

    public async IAsyncEnumerable<PriceCandle> ReadAllAsync()
    {
        _malformedRows.Clear();
        PhysicalRowsExamined = 0;
        ResolvedDateFormat = null;
        ResolvedTimeFormat = null;
        ResolvedTimestampFormat = null;
        ResolvedTimestampColumn = null;

        if (!File.Exists(_path))
        {
            throw new FileNotFoundException($"CSV input file not found: {_path}", _path);
        }

        var delimiter = await ResolveDelimiterAsync().ConfigureAwait(false);
        ResolvedDelimiter = delimiter;
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter.ToString(),
            HasHeaderRecord = false,
            BadDataFound = args => throw new InvalidDataException(
                $"Invalid CSV data near row {args.Context.Parser.Row}."),
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
            DetectColumnCountChanges = false
        };

        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var textReader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(textReader, configuration);

        Layout? layout = null;
        while (await csv.ReadAsync().ConfigureAwait(false))
        {
            var row = csv.Parser.Record ?? Array.Empty<string>();
            var sourceLine = csv.Parser.RawRow;
            if (layout is null && _options.HasHeader)
            {
                layout = ResolveHeaderLayout(row);
                continue;
            }

            layout ??= ResolveHeaderlessLayout(row.Length);
            PhysicalRowsExamined++;
            EnsureRequiredColumns(row, layout, sourceLine);

            DateTimeOffset? timestamp = null;
            PriceCandle? candle = null;
            try
            {
                timestamp = ParseTimestamp(row, layout);
                candle = new PriceCandle(
                    timestamp.Value,
                    ParseDecimal(row[layout.Open], "Open"),
                    ParseDecimal(row[layout.High], "High"),
                    ParseDecimal(row[layout.Low], "Low"),
                    ParseDecimal(row[layout.Close], "Close"),
                    ParseDecimal(row[layout.Volume], "Volume"),
                    sourceLine);
            }
            catch (Exception exception) when (
                exception is FormatException or OverflowException or ArgumentException)
            {
                _malformedRows.Add(new MalformedRow(
                    sourceLine,
                    string.Empty,
                    exception.Message,
                    timestamp));
            }

            if (candle is not null)
            {
                yield return candle;
            }
        }

        if (_options.HasHeader && layout is null)
        {
            throw new InvalidDataException("Header mode requires a physical CSV header row.");
        }
    }

    private async Task<char> ResolveDelimiterAsync()
    {
        if (!string.IsNullOrWhiteSpace(_options.Delimiter))
        {
            return ParseDelimiter(_options.Delimiter);
        }

        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true);
        var sample = await reader.ReadLineAsync().ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(sample) ? ',' : DelimiterDetector.Detect(sample);
    }

    private Layout ResolveHeaderLayout(IReadOnlyList<string> headers)
    {
        var hasTimestampHeader = headers.Any(header =>
            string.Equals(header?.Trim(), "Timestamp", StringComparison.OrdinalIgnoreCase));
        if (IsCombinedTimestampMode || hasTimestampHeader)
        {
            var timestamp = IsCombinedTimestampMode
                ? ResolveTimestampColumn(headers)
                : HeaderLayoutResolver.Resolve(headers, "timestamp")["timestamp"];
            var indexes = HeaderLayoutResolver.Resolve(
                headers,
                "open",
                "high",
                "low",
                "close",
                "volume");
            return new Layout(
                Timestamp: timestamp,
                Date: null,
                Time: null,
                Open: indexes["open"],
                High: indexes["high"],
                Low: indexes["low"],
                Close: indexes["close"],
                Volume: indexes["volume"]);
        }

        var separate = HeaderLayoutResolver.Resolve(
            headers,
            "date",
            "time",
            "open",
            "high",
            "low",
            "close",
            "volume");
        return new Layout(
            Timestamp: null,
            Date: separate["date"],
            Time: separate["time"],
            Open: separate["open"],
            High: separate["high"],
            Low: separate["low"],
            Close: separate["close"],
            Volume: separate["volume"]);
    }

    private Layout ResolveHeaderlessLayout(int columnCount)
    {
        if (IsCombinedTimestampMode)
        {
            var timestamp = int.Parse(_options.TimestampColumn!, CultureInfo.InvariantCulture) - 1;
            if (columnCount < timestamp + 6)
            {
                throw new InvalidDataException(
                    "Combined-timestamp rows require the timestamp plus five following OHLCV columns.");
            }

            return new Layout(
                Timestamp: timestamp,
                Date: null,
                Time: null,
                Open: timestamp + 1,
                High: timestamp + 2,
                Low: timestamp + 3,
                Close: timestamp + 4,
                Volume: timestamp + 5);
        }

        if (columnCount < 7)
        {
            throw new InvalidDataException(
                "Default MT4 rows require Date, Time, Open, High, Low, Close, and Volume columns.");
        }

        return new Layout(null, 0, 1, 2, 3, 4, 5, 6);
    }

    private DateTimeOffset ParseTimestamp(IReadOnlyList<string> row, Layout layout)
    {
        DateTime local;
        if (layout.Timestamp is int timestampIndex)
        {
            var timestampText = row[timestampIndex];
            ResolvedCombinedTimestamp = true;
            ResolvedTimestampColumn = _options.TimestampColumn ??
                (timestampIndex + 1).ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(_options.TimestampFormat))
            {
                ResolvedTimestampFormat = _options.TimestampFormat;
                local = DateTime.ParseExact(
                    timestampText,
                    _options.TimestampFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None);
            }
            else
            {
                local = ParseFirstMatchingFormat(
                    timestampText,
                    ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss'Z'", "O"],
                    out var matchedFormat);
                ResolvedTimestampFormat ??= matchedFormat;
            }
        }
        else
        {
            var dateFormat = _options.DateFormat ?? "yyyy.MM.dd";
            var timeText = row[layout.Time!.Value];
            var timeFormat = _options.TimeFormat ??
                (timeText.Count(character => character == ':') == 1 ? "HH:mm" : "HH:mm:ss");
            ResolvedCombinedTimestamp = false;
            ResolvedDateFormat ??= dateFormat;
            ResolvedTimeFormat ??= timeFormat;
            local = DateTime.ParseExact(
                $"{row[layout.Date!.Value]} {timeText}",
                $"{dateFormat} {timeFormat}",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None);
        }

        return SourceOffsetConverter.NormalizeToUtc(local, _options.TzOffset);
    }

    private int ResolveTimestampColumn(IReadOnlyList<string> headers)
    {
        if (int.TryParse(_options.TimestampColumn, out var oneBasedIndex))
        {
            var zeroBasedIndex = oneBasedIndex - 1;
            if (zeroBasedIndex < 0 || zeroBasedIndex >= headers.Count)
            {
                throw new InvalidDataException("Timestamp column index is outside the CSV header.");
            }

            return zeroBasedIndex;
        }

        return HeaderLayoutResolver.Resolve(headers, _options.TimestampColumn!)[_options.TimestampColumn!];
    }

    // Parses with the first candidate format that matches so the resolved
    // format can be reported exactly as it was applied.
    private static DateTime ParseFirstMatchingFormat(
        string text,
        string[] formats,
        out string matchedFormat)
    {
        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(
                    text,
                    format,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed))
            {
                matchedFormat = format;
                return parsed;
            }
        }

        throw new FormatException(
            $"Timestamp value '{text}' does not match any supported combined format.");
    }

    private static decimal ParseDecimal(string value, string fieldName)
    {
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new FormatException($"{fieldName} value '{value}' is not an invariant decimal.");
        }

        return parsed;
    }

    private static void EnsureRequiredColumns(IReadOnlyList<string> row, Layout layout, long sourceLine)
    {
        var highestRequired = new int?[]
        {
            layout.Timestamp,
            layout.Date,
            layout.Time,
            layout.Open,
            layout.High,
            layout.Low,
            layout.Close,
            layout.Volume
        }.Max()!.Value;

        if (row.Count <= highestRequired)
        {
            throw new InvalidDataException(
                $"CSV row {sourceLine} has too few columns for the active layout.");
        }
    }

    private static char ParseDelimiter(string delimiter) => delimiter.Trim().ToLowerInvariant() switch
    {
        "," or "comma" => ',',
        ";" or "semicolon" => ';',
        "\\t" or "tab" or "\t" => '\t',
        { Length: 1 } value => value[0],
        _ => throw new ArgumentException($"Unsupported delimiter '{delimiter}'.")
    };

    private bool IsCombinedTimestampMode =>
        !string.IsNullOrWhiteSpace(_options.TimestampFormat) &&
        !string.IsNullOrWhiteSpace(_options.TimestampColumn);

    private sealed record Layout(
        int? Timestamp,
        int? Date,
        int? Time,
        int Open,
        int High,
        int Low,
        int Close,
        int Volume);
}