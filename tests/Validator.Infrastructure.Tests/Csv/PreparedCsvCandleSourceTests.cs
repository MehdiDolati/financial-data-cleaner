using System.Text;
using Validator.Application.Abstractions;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Infrastructure.Csv;

namespace Validator.Infrastructure.Tests.Csv;

// The prepared source is the single Infrastructure adapter that turns raw
// source bytes into everything a detailed report must state about its input:
// a SHA-256 identity for the exact bytes, the resolved CSV interpretation, the
// row-level scan coverage, and replayable candle data. Expected input failures
// are returned as classified fatal diagnostics rather than thrown.
public sealed class PreparedCsvCandleSourceTests : IDisposable
{
    private readonly string _directory;

    public PreparedCsvCandleSourceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"validator-prepared-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string WriteFixture(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    [Fact]
    public async Task PrepareAsync_CleanSource_ReportsIdentityContextAndCoverage()
    {
        var path = WriteFixture(
            "prices.csv",
            """
            2026.01.05,00:00,1.1000,1.1200,1.0900,1.1100,1000
            2026.01.05,01:00,1.1100,1.1300,1.1000,1.1200,1000

            """);
        var source = new PreparedCsvCandleSource(path, new CsvInputOptions { TzOffset = TimeSpan.Zero });

        var result = await source.PrepareAsync(new CsvInputOptions { TzOffset = TimeSpan.Zero });

        var succeeded = Assert.IsType<PreparedCandleDataResult.Succeeded>(result);

        // Identity names the file safely and fingerprints the exact bytes.
        Assert.Equal("prices.csv", succeeded.Source.FileName);
        Assert.Equal(new FileInfo(path).Length, succeeded.Source.ByteSize);
        Assert.Equal(64, succeeded.Source.Sha256.Length);

        // Coverage reconciles: examined == accepted + malformed.
        Assert.Equal(2, succeeded.Coverage.PhysicalRowsExamined);
        Assert.Equal(2, succeeded.Coverage.AcceptedRows);
        Assert.Equal(0, succeeded.Coverage.MalformedRows);
        Assert.True(succeeded.Coverage.IsReconciled);

        // Resolved interpretation is reported exactly as it was applied.
        Assert.Equal(',', succeeded.Csv.Delimiter);
        Assert.False(succeeded.Csv.HasHeader);
        Assert.Equal(TimestampMode.SeparateDateTime, succeeded.Csv.Timestamp.Mode);
        Assert.Equal("yyyy.MM.dd", succeeded.Csv.Timestamp.DateFormat);
        Assert.Equal("HH:mm", succeeded.Csv.Timestamp.TimeFormat);
        Assert.Equal("+00:00", succeeded.Csv.Timestamp.SourceOffset);

        Assert.NotNull(succeeded.Csv.DateRange);
        Assert.Equal(
            new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero),
            succeeded.Csv.DateRange!.Start);
        Assert.Equal(
            new DateTimeOffset(2026, 1, 5, 1, 0, 0, TimeSpan.Zero),
            succeeded.Csv.DateRange.End);
    }

    [Fact]
    public async Task PrepareAsync_ProducesReplayableDataThatCanBeReadMoreThanOnce()
    {
        var path = WriteFixture(
            "replay.csv",
            """
            2026.01.05,00:00,1.1000,1.1200,1.0900,1.1100,1000
            2026.01.05,01:00,1.1100,1.1300,1.1000,1.1200,1000

            """);
        var source = new PreparedCsvCandleSource(path, new CsvInputOptions { TzOffset = TimeSpan.Zero });

        var result = await source.PrepareAsync(new CsvInputOptions { TzOffset = TimeSpan.Zero });
        var succeeded = Assert.IsType<PreparedCandleDataResult.Succeeded>(result);

        var first = new List<decimal>();
        await foreach (var candle in succeeded.Data.ReplayAsync())
        {
            first.Add(candle.Open);
        }

        var second = new List<decimal>();
        await foreach (var candle in succeeded.Data.ReplayAsync())
        {
            second.Add(candle.Open);
        }

        Assert.Equal(new[] { 1.1000m, 1.1100m }, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task PrepareAsync_MalformedRow_IsCountedAsExaminedButNotAccepted()
    {
        var path = WriteFixture(
            "malformed.csv",
            """
            2026.01.05,00:00,1.1000,1.1200,1.0900,1.1100,1000
            2026.01.05,01:00,not-a-number,1.1300,1.1000,1.1200,1000

            """);
        var source = new PreparedCsvCandleSource(path, new CsvInputOptions { TzOffset = TimeSpan.Zero });

        var result = await source.PrepareAsync(new CsvInputOptions { TzOffset = TimeSpan.Zero });

        var succeeded = Assert.IsType<PreparedCandleDataResult.Succeeded>(result);
        Assert.Equal(2, succeeded.Coverage.PhysicalRowsExamined);
        Assert.Equal(1, succeeded.Coverage.AcceptedRows);
        Assert.Equal(1, succeeded.Coverage.MalformedRows);
        Assert.True(succeeded.Coverage.IsReconciled);
        Assert.Single(source.MalformedRows);
        Assert.Equal(2, source.MalformedRows[0].LineNumber);
    }

    [Fact]
    public async Task PrepareAsync_MissingFile_FailsWithSourceUnavailable()
    {
        var source = new PreparedCsvCandleSource(
            Path.Combine(_directory, "absent.csv"),
            new CsvInputOptions());

        var result = await source.PrepareAsync(new CsvInputOptions());

        var failed = Assert.IsType<PreparedCandleDataResult.Failed>(result);
        Assert.Equal("SOURCE_UNAVAILABLE", failed.Diagnostic.Code);
        Assert.Equal(FailureClass.Operational, failed.Diagnostic.FailureClass);
        Assert.Equal(FailureStage.SourceIdentity, failed.Diagnostic.Stage);
        Assert.Equal("absent.csv", failed.Diagnostic.Source?.FileName);
    }

    [Fact]
    public async Task PrepareAsync_TooFewColumns_FailsWithInvalidStructure()
    {
        var path = WriteFixture(
            "short.csv",
            """
            2026.01.05,00:00,1.1000

            """);
        var source = new PreparedCsvCandleSource(path, new CsvInputOptions());

        var result = await source.PrepareAsync(new CsvInputOptions());

        var failed = Assert.IsType<PreparedCandleDataResult.Failed>(result);
        Assert.Equal("INVALID_STRUCTURE", failed.Diagnostic.Code);
        Assert.Equal(FailureClass.Dataset, failed.Diagnostic.FailureClass);
        Assert.Equal(FailureStage.Ingestion, failed.Diagnostic.Stage);
    }

    [Fact]
    public async Task PrepareAsync_InvalidEncoding_FailsWithInvalidEncoding()
    {
        var path = Path.Combine(_directory, "encoding.csv");
        // 0xFF is not valid UTF-8 and cannot be decoded as source text.
        File.WriteAllBytes(path, [0x32, 0x30, 0xFF, 0xFE, 0x2C, 0x31]);
        var source = new PreparedCsvCandleSource(path, new CsvInputOptions());

        var result = await source.PrepareAsync(new CsvInputOptions());

        var failed = Assert.IsType<PreparedCandleDataResult.Failed>(result);
        Assert.Equal("INVALID_ENCODING", failed.Diagnostic.Code);
        Assert.Equal(FailureStage.Ingestion, failed.Diagnostic.Stage);
    }

    [Fact]
    public async Task PrepareAsync_CombinedTimestamp_ReportsCombinedInterpretation()
    {
        var path = WriteFixture(
            "combined.csv",
            """
            2026-01-05 00:00:00,1.1000,1.1200,1.0900,1.1100,1000

            """);
        var options = new CsvInputOptions
        {
            TimestampFormat = "yyyy-MM-dd HH:mm:ss",
            TimestampColumn = "1",
            TzOffset = TimeSpan.FromHours(-3)
        };
        var source = new PreparedCsvCandleSource(path, options);

        var result = await source.PrepareAsync(options);

        var succeeded = Assert.IsType<PreparedCandleDataResult.Succeeded>(result);
        Assert.Equal(TimestampMode.CombinedTimestamp, succeeded.Csv.Timestamp.Mode);
        Assert.Equal("yyyy-MM-dd HH:mm:ss", succeeded.Csv.Timestamp.TimestampFormat);
        Assert.Equal("1", succeeded.Csv.Timestamp.TimestampColumn);
        Assert.Equal("-03:00", succeeded.Csv.Timestamp.SourceOffset);
    }

    [Fact]
    public async Task PrepareAsync_IdenticalBytes_ProduceIdenticalFingerprints()
    {
        const string content = """
            2026.01.05,00:00,1.1000,1.1200,1.0900,1.1100,1000

            """;
        var first = WriteFixture("a.csv", content);
        var second = WriteFixture("b.csv", content);

        var firstResult = Assert.IsType<PreparedCandleDataResult.Succeeded>(
            await new PreparedCsvCandleSource(first, new CsvInputOptions()).PrepareAsync(new CsvInputOptions()));
        var secondResult = Assert.IsType<PreparedCandleDataResult.Succeeded>(
            await new PreparedCsvCandleSource(second, new CsvInputOptions()).PrepareAsync(new CsvInputOptions()));

        Assert.Equal(firstResult.Source.Sha256, secondResult.Source.Sha256);
        Assert.NotEqual(firstResult.Source.FileName, secondResult.Source.FileName);
    }

    [Fact]
    public async Task PrepareAsync_EmptySource_ReportsZeroCoverageAndNoDateRange()
    {
        var path = WriteFixture("empty.csv", string.Empty);
        var source = new PreparedCsvCandleSource(path, new CsvInputOptions());

        var result = await source.PrepareAsync(new CsvInputOptions());

        var succeeded = Assert.IsType<PreparedCandleDataResult.Succeeded>(result);
        Assert.Equal(0, succeeded.Coverage.PhysicalRowsExamined);
        Assert.Equal(0, succeeded.Coverage.AcceptedRows);
        Assert.Null(succeeded.Csv.DateRange);
    }
}
