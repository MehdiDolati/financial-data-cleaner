using System.Reflection;
using Validator.Application.Abstractions;
using Validator.Application.Reporting;
using Validator.Application.Tests.Doubles;
using Validator.Application.Validation;
using Validator.Domain.Calendars;
using Validator.Domain.Candles;
using Validator.Domain.Findings;

namespace Validator.Application.Tests.UseCases;

public sealed class ValidateMarketDataUseCaseCoverageTests
{
    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        var writer = new CapturingWriter();
        var source = new InMemoryCandleSource([]);

        Assert.Throws<ArgumentNullException>(() => new ValidateMarketDataUseCase(null!, writer));
        Assert.Throws<ArgumentNullException>(() => new ValidateMarketDataUseCase(source, null!));
    }

    [Fact]
    public async Task ExecuteAsync_RejectsAnUnexpectedRequestType()
    {
        var useCase = new ValidateMarketDataUseCase(new InMemoryCandleSource([]), new CapturingWriter());

        await Assert.ThrowsAsync<ArgumentException>(() => useCase.ExecuteAsync(new object()));
    }

    [Fact]
    public async Task ExecuteAsync_AllowsEmptyInputWithAnOverride()
    {
        var writer = new CapturingWriter();
        var useCase = new ValidateMarketDataUseCase(new InMemoryCandleSource([]), writer);

        var exitCode = await useCase.ExecuteAsync(
            new ValidationRequest("empty.csv", "H1", MarketCalendar: new HourCalendar()));

        Assert.Equal(0, exitCode);
        Assert.NotNull(writer.Report);
        Assert.Null(writer.Report.Range);
        Assert.True(writer.Report.IsClean);
    }

    [Fact]
    public async Task ExecuteAsync_InfersTimeframeWithTheDefaultForexCalendar()
    {
        var writer = new CapturingWriter();
        var useCase = new ValidateMarketDataUseCase(
            new InMemoryCandleSource([Candle(0, 1), Candle(1, 2)]),
            writer);

        var exitCode = await useCase.ExecuteAsync(new ValidationRequest("inferred.csv"));

        Assert.Equal(0, exitCode);
        Assert.Equal("H1", writer.Report?.DetectedTimeframe);
        Assert.NotNull(writer.Report?.Range);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsWhenTimeframeCannotBeInferred()
    {
        var useCase = new ValidateMarketDataUseCase(new InMemoryCandleSource([]), new CapturingWriter());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            useCase.ExecuteAsync(new ValidationRequest("empty.csv", MarketCalendar: new HourCalendar())));

        Assert.Contains("--timeframe", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ReservesParsedMalformedSlotsAndRestartsGapsAfterClosures()
    {
        var source = new MalformedSource(
            [Candle(0, 1), Candle(5, 2)],
            [
                new MalformedRow(7, "", "Bad close", Utc(2)),
                new MalformedRow(8, "", "Bad timestamp")
            ]);
        var writer = new CapturingWriter();
        var useCase = new ValidateMarketDataUseCase(source, writer);

        var exitCode = await useCase.ExecuteAsync(
            new ValidationRequest("malformed.csv", "H1", MarketCalendar: new HourCalendar(closedHour: 1)));

        Assert.Equal(1, exitCode);
        Assert.NotNull(writer.Report);
        Assert.Equal(2, writer.Report.Summary.MissingCandles);
        Assert.Equal(1, writer.Report.Summary.TimeGaps);
        Assert.Equal(2, writer.Report.Summary.MalformedRows);
        Assert.Equal(
            writer.Report.Findings.OrderBy(finding => finding.Category)
                .ThenBy(finding => finding.Timestamp ?? DateTimeOffset.MaxValue)
                .ThenBy(finding => finding.Line ?? int.MaxValue)
                .ThenBy(finding => finding.Message, StringComparer.Ordinal),
            writer.Report.Findings);
    }

    [Fact]
    public void DefaultForexCalendar_ExposesItsProfileAndSessionRule()
    {
        var calendarType = typeof(ValidateMarketDataUseCase)
            .GetNestedType("DefaultForexCalendar", BindingFlags.NonPublic);
        Assert.NotNull(calendarType);
        var calendar = Assert.IsAssignableFrom<IMarketCalendar>(
            Activator.CreateInstance(calendarType, nonPublic: true));

        Assert.Equal(MarketProfile.Forex, calendar.Profile);
        Assert.True(calendar.IsOpen(Utc(0)));
    }

    private static PriceCandle Candle(int hour, long sourceLine) =>
        new(Utc(hour), 1m, 2m, 0.5m, 1.5m, 10m, sourceLine);

    private static DateTimeOffset Utc(int hour) =>
        new(2026, 1, 5, hour, 0, 0, TimeSpan.Zero);

    private sealed class CapturingWriter : IReportWriter
    {
        public ValidationReport? Report { get; private set; }

        public Task WriteReportAsync(object report)
        {
            Report = Assert.IsType<ValidationReport>(report);
            return Task.CompletedTask;
        }
    }

    private sealed class HourCalendar(int? closedHour = null) : IMarketCalendar
    {
        public MarketProfile Profile => MarketProfile.Custom;

        public bool IsOpen(DateTimeOffset timestamp) => timestamp.Hour != closedHour;
    }

    private sealed class MalformedSource(
        IReadOnlyList<PriceCandle> candles,
        IReadOnlyList<MalformedRow> malformedRows) : ICandleSource, IMalformedRowSource
    {
        public IReadOnlyList<MalformedRow> MalformedRows { get; } = malformedRows;

        public async IAsyncEnumerable<PriceCandle> ReadAllAsync()
        {
            foreach (var candle in candles)
            {
                yield return candle;
                await Task.Yield();
            }
        }
    }
}