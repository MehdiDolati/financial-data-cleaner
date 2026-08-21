using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Validator.Application.Benchmark;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Scoring;
using Validator.Domain.Calendars;
using Validator.Domain.Findings;
using Validator.Domain.Scoring;

namespace Validator.Infrastructure.Benchmark;

/// <summary>
/// Strict adapter for version 1 benchmark snapshots. The DTO is intentionally
/// separate from the Application model so persisted enum and score shapes do not
/// drift when implementation types evolve.
/// </summary>
public static class BenchmarkSnapshotJsonSerializer
{
    private const int SupportedContractVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(BenchmarkSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(ToDto(snapshot), Options);
    }

    public static BenchmarkSnapshot Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON must not be empty.", nameof(json));

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("contractVersion", out var versionElement) ||
            versionElement.ValueKind != JsonValueKind.Number ||
            !versionElement.TryGetInt32(out var version))
        {
            throw new InvalidDataException("Missing required numeric 'contractVersion' field in benchmark snapshot.");
        }

        if (version != SupportedContractVersion)
        {
            throw new InvalidDataException(
                $"Incompatible benchmark contract version {version}. This application supports version {SupportedContractVersion}.");
        }

        var dto = JsonSerializer.Deserialize<SnapshotDto>(json, Options)
            ?? throw new InvalidDataException("Benchmark snapshot JSON produced no document.");

        try
        {
            return FromDto(dto);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            throw new InvalidDataException($"Benchmark snapshot contract is invalid: {exception.Message}", exception);
        }
    }

    public static async Task WriteToFileAsync(
        string filePath,
        BenchmarkSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, Serialize(snapshot), new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);
            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public static async Task<BenchmarkSnapshot> ReadFromFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Benchmark file not found: {filePath}", filePath);

        return Deserialize(await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false));
    }

    private static SnapshotDto ToDto(BenchmarkSnapshot snapshot) => new()
    {
        ContractVersion = SupportedContractVersion,
        Name = snapshot.Name,
        Instrument = snapshot.Instrument,
        EstablishedAtUtc = ToUtcText(snapshot.EstablishedAtUtc),
        Source = new SourceDto
        {
            FileName = snapshot.Source.FileName,
            ByteSize = snapshot.Source.ByteSize,
            Sha256 = snapshot.Source.Sha256
        },
        Context = ToDto(snapshot.Context),
        Coverage = new CoverageDto
        {
            PhysicalRowsExamined = snapshot.Coverage.PhysicalRowsExamined,
            AcceptedRows = snapshot.Coverage.AcceptedRows,
            MalformedRows = snapshot.Coverage.MalformedRows
        },
        Checks = snapshot.Checks.Select(check => new CheckDto
        {
            Check = check.Check,
            Status = check.Status,
            Reason = check.Reason
        }).ToArray(),
        Metrics = snapshot.Metrics.Select(ToDto).ToArray(),
        Dataset = ToDto(snapshot.Dataset),
        Weighting = new WeightingDto
        {
            Source = snapshot.Weighting.Source,
            Weights = snapshot.Weighting.Weights.Select(weight => new WeightDto
            {
                Category = weight.Category,
                Weight = weight.Weight,
                NormalisedShare = weight.NormalisedShare
            }).ToArray()
        }
    };

    private static BenchmarkSnapshot FromDto(SnapshotDto dto)
    {
        Require(dto.ContractVersion == SupportedContractVersion, "contractVersion must equal 1");
        RequireText(dto.Name, "name");
        RequireText(dto.Instrument, "instrument");
        RequireText(dto.EstablishedAtUtc, "establishedAtUtc");
        ArgumentNullException.ThrowIfNull(dto.Source);
        ArgumentNullException.ThrowIfNull(dto.Context);
        ArgumentNullException.ThrowIfNull(dto.Coverage);
        ArgumentNullException.ThrowIfNull(dto.Checks);
        ArgumentNullException.ThrowIfNull(dto.Metrics);
        ArgumentNullException.ThrowIfNull(dto.Dataset);
        ArgumentNullException.ThrowIfNull(dto.Weighting);
        ArgumentNullException.ThrowIfNull(dto.Weighting.Weights);

        var context = FromDto(dto.Context);
        var metrics = dto.Metrics.Select(FromDto).ToArray();
        var weights = dto.Weighting.Weights.Select(weight =>
            new MetricWeight(weight.Category, weight.Weight, weight.NormalisedShare)).ToArray();

        return new BenchmarkSnapshot(
            dto.Name!,
            ParseUtc(dto.EstablishedAtUtc!, "establishedAtUtc"),
            new SourceIdentity(
                RequireText(dto.Source.FileName, "source.fileName"),
                dto.Source.ByteSize,
                RequireText(dto.Source.Sha256, "source.sha256")),
            context,
            new ScanCoverage(
                dto.Coverage.PhysicalRowsExamined,
                dto.Coverage.AcceptedRows,
                dto.Coverage.MalformedRows),
            dto.Checks.Select(check => new CheckExecution(check.Check, check.Status, check.Reason)).ToArray(),
            metrics,
            FromDto(dto.Dataset),
            new ScoreWeighting(dto.Weighting.Source, weights),
            dto.Instrument!);
    }

    private static ContextDto ToDto(ValidationContextSnapshot context) => new()
    {
        Timeframe = context.Timeframe,
        Calendar = new CalendarDto
        {
            Profile = context.Calendar.Profile,
            Name = context.Calendar.Name,
            TimeZone = context.Calendar.TimeZone,
            DefinitionSha256 = context.Calendar.DefinitionSha256,
            Sessions = context.Calendar.Sessions.Select(session => new SessionDto
            {
                OpenDay = session.OpenDay,
                OpenTime = FormatTime(session.OpenTime),
                CloseDay = session.CloseDay,
                CloseTime = FormatTime(session.CloseTime)
            }).ToArray()
        },
        Timestamp = new TimestampDto
        {
            Mode = context.Timestamp.Mode,
            DateFormat = context.Timestamp.DateFormat,
            TimeFormat = context.Timestamp.TimeFormat,
            TimestampFormat = context.Timestamp.TimestampFormat,
            TimestampColumn = context.Timestamp.TimestampColumn,
            SourceOffset = context.Timestamp.SourceOffset
        },
        Delimiter = context.Delimiter,
        HasHeader = context.HasHeader,
        DateRange = context.DateRange is null ? null : new DateRangeDto
        {
            Start = ToUtcText(context.DateRange.Start),
            End = ToUtcText(context.DateRange.End)
        }
    };

    private static ValidationContextSnapshot FromDto(ContextDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto.Calendar);
        ArgumentNullException.ThrowIfNull(dto.Timestamp);
        var sessions = (dto.Calendar.Sessions ?? Array.Empty<SessionDto>())
            .Select(session => new WeeklySession(
                session.OpenDay,
                ParseTime(RequireText(session.OpenTime, "context.calendar.sessions.openTime")),
                session.CloseDay,
                ParseTime(RequireText(session.CloseTime, "context.calendar.sessions.closeTime"))))
            .ToArray();

        var calendar = new CalendarContext(
            RequireText(dto.Calendar.Profile, "context.calendar.profile"),
            RequireText(dto.Calendar.Name, "context.calendar.name"),
            sessions,
            dto.Calendar.TimeZone,
            dto.Calendar.DefinitionSha256);

        var timestamp = dto.Timestamp.Mode switch
        {
            TimestampMode.SeparateDateTime => TimestampInterpretation.CreateSeparate(
                RequireText(dto.Timestamp.DateFormat, "context.timestamp.dateFormat"),
                RequireText(dto.Timestamp.TimeFormat, "context.timestamp.timeFormat"),
                RequireText(dto.Timestamp.SourceOffset, "context.timestamp.sourceOffset")),
            TimestampMode.CombinedTimestamp => TimestampInterpretation.CreateCombined(
                RequireText(dto.Timestamp.TimestampFormat, "context.timestamp.timestampFormat"),
                RequireText(dto.Timestamp.TimestampColumn, "context.timestamp.timestampColumn"),
                RequireText(dto.Timestamp.SourceOffset, "context.timestamp.sourceOffset")),
            _ => throw new InvalidDataException($"Unknown timestamp mode '{dto.Timestamp.Mode}'.")
        };

        var dateRange = dto.DateRange is null
            ? null
            : new DateRange(
                ParseUtc(RequireText(dto.DateRange.Start, "context.dateRange.start"), "context.dateRange.start"),
                ParseUtc(RequireText(dto.DateRange.End, "context.dateRange.end"), "context.dateRange.end"));

        return new ValidationContextSnapshot(
            RequireText(dto.Timeframe, "context.timeframe"),
            calendar,
            timestamp,
            RequireText(dto.Delimiter, "context.delimiter"),
            dto.HasHeader,
            dateRange);
    }

    private static MetricDto ToDto(MetricScore metric) => new()
    {
        Category = metric.Category,
        State = metric.State,
        Count = metric.Count,
        Population = metric.Population,
        PopulationKind = metric.PopulationKind,
        Score = metric.Score.HasValue ? ToDto(metric.Score.Value) : null,
        Reason = metric.Reason
    };

    private static MetricScore FromDto(MetricDto dto) => dto.State switch
    {
        MetricScoreState.Scored => MetricScore.Scored(
            dto.Category,
            dto.Count,
            dto.Population ?? throw new InvalidDataException("A scored metric requires population."),
            dto.PopulationKind,
            FromDto(dto.Score ?? throw new InvalidDataException("A scored metric requires score."))),
        MetricScoreState.NotApplicable => MetricScore.NotApplicable(
            dto.Category,
            dto.PopulationKind,
            RequireText(dto.Reason, "metrics.reason")),
        MetricScoreState.NotScored => MetricScore.NotScored(
            dto.Category,
            dto.PopulationKind,
            RequireText(dto.Reason, "metrics.reason"),
            dto.Count),
        _ => throw new InvalidDataException($"Unknown metric state '{dto.State}'.")
    };

    private static DatasetDto ToDto(DatasetScore dataset) => new()
    {
        Average = dataset.Average.HasValue ? ToDto(dataset.Average.Value) : null,
        MetricsCovered = dataset.MetricsCovered,
        CoveredCategories = dataset.CoveredCategories.ToArray(),
        ExcludedCategories = dataset.ExcludedCategories.Select(excluded => new ExcludedDto
        {
            Category = excluded.Category,
            State = excluded.State,
            Reason = excluded.Reason
        }).ToArray(),
        UnavailableReason = dataset.UnavailableReason
    };

    private static DatasetScore FromDto(DatasetDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto.CoveredCategories);
        ArgumentNullException.ThrowIfNull(dto.ExcludedCategories);
        var excluded = dto.ExcludedCategories.Select(item =>
            new ExcludedMetric(item.Category, item.State, RequireText(item.Reason, "dataset.excludedCategories.reason")))
            .ToArray();

        return dto.Average is null
            ? DatasetScore.Unavailable(
                RequireText(dto.UnavailableReason, "dataset.unavailableReason"),
                dto.CoveredCategories,
                excluded)
            : DatasetScore.Available(FromDto(dto.Average), dto.CoveredCategories, excluded);
    }

    private static ScoreDto ToDto(ScoreValue score) => new()
    {
        Exact = score.Exact.ToString(),
        Rounded = score.Format()
    };

    private static ScoreValue FromDto(ScoreDto dto)
    {
        var exactText = RequireText(dto.Exact, "score.exact");
        var parts = exactText.Split('/');
        if (parts.Length != 2 ||
            !BigInteger.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var numerator) ||
            !BigInteger.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var denominator))
        {
            throw new InvalidDataException($"Invalid exact score ratio '{exactText}'.");
        }

        var score = new ScoreValue(new ExactRatio(numerator, denominator));
        if (!string.Equals(score.Format(), RequireText(dto.Rounded, "score.rounded"), StringComparison.Ordinal))
            throw new InvalidDataException("Rounded score does not match its exact ratio.");
        return score;
    }

    private static string ToUtcText(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtc(string value, string field) =>
        DateTimeOffset.TryParseExact(
            value,
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : throw new InvalidDataException($"'{field}' must be a UTC timestamp ending in Z.");

    private static string FormatTime(TimeSpan value) =>
        value.Seconds == 0
            ? value.ToString(@"hh\:mm", CultureInfo.InvariantCulture)
            : value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

    private static TimeSpan ParseTime(string value) =>
        TimeSpan.TryParseExact(value, [@"hh\:mm", @"hh\:mm\:ss"], CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidDataException($"Invalid session time '{value}'.");

    private static string RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Missing required '{field}' field in benchmark snapshot.");
        return value;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidDataException(message);
    }

    private sealed class SnapshotDto
    {
        public int ContractVersion { get; set; }
        public string? Name { get; set; }
        public string? Instrument { get; set; }
        public string? EstablishedAtUtc { get; set; }
        public SourceDto? Source { get; set; }
        public ContextDto? Context { get; set; }
        public CoverageDto? Coverage { get; set; }
        public CheckDto[]? Checks { get; set; }
        public MetricDto[]? Metrics { get; set; }
        public DatasetDto? Dataset { get; set; }
        public WeightingDto? Weighting { get; set; }
    }

    private sealed class SourceDto
    {
        public string? FileName { get; set; }
        public long ByteSize { get; set; }
        public string? Sha256 { get; set; }
    }

    private sealed class ContextDto
    {
        public string? Timeframe { get; set; }
        public CalendarDto? Calendar { get; set; }
        public TimestampDto? Timestamp { get; set; }
        public string? Delimiter { get; set; }
        public bool HasHeader { get; set; }
        public DateRangeDto? DateRange { get; set; }
    }

    private sealed class CalendarDto
    {
        public string? Profile { get; set; }
        public string? Name { get; set; }
        public string? TimeZone { get; set; }
        public string? DefinitionSha256 { get; set; }
        public SessionDto[]? Sessions { get; set; }
    }

    private sealed class SessionDto
    {
        public DayOfWeek OpenDay { get; set; }
        public string? OpenTime { get; set; }
        public DayOfWeek CloseDay { get; set; }
        public string? CloseTime { get; set; }
    }

    private sealed class TimestampDto
    {
        public TimestampMode Mode { get; set; }
        public string? DateFormat { get; set; }
        public string? TimeFormat { get; set; }
        public string? TimestampFormat { get; set; }
        public string? TimestampColumn { get; set; }
        public string? SourceOffset { get; set; }
    }

    private sealed class DateRangeDto
    {
        public string? Start { get; set; }
        public string? End { get; set; }
    }

    private sealed class CoverageDto
    {
        public long PhysicalRowsExamined { get; set; }
        public long AcceptedRows { get; set; }
        public long MalformedRows { get; set; }
    }

    private sealed class CheckDto
    {
        public CheckName Check { get; set; }
        public CheckStatus Status { get; set; }
        public string? Reason { get; set; }
    }

    private sealed class MetricDto
    {
        public FindingCategory Category { get; set; }
        public MetricScoreState State { get; set; }
        public long Count { get; set; }
        public long? Population { get; set; }
        public MetricPopulationKind PopulationKind { get; set; }
        public ScoreDto? Score { get; set; }
        public string? Reason { get; set; }
    }

    private sealed class ScoreDto
    {
        public string? Exact { get; set; }
        public string? Rounded { get; set; }
    }

    private sealed class DatasetDto
    {
        public ScoreDto? Average { get; set; }
        public int MetricsCovered { get; set; }
        public FindingCategory[]? CoveredCategories { get; set; }
        public ExcludedDto[]? ExcludedCategories { get; set; }
        public string? UnavailableReason { get; set; }
    }

    private sealed class ExcludedDto
    {
        public FindingCategory Category { get; set; }
        public MetricScoreState State { get; set; }
        public string? Reason { get; set; }
    }

    private sealed class WeightingDto
    {
        public ScoreWeightingSource Source { get; set; }
        public WeightDto[]? Weights { get; set; }
    }

    private sealed class WeightDto
    {
        public FindingCategory Category { get; set; }
        public decimal Weight { get; set; }
        public decimal? NormalisedShare { get; set; }
    }
}