using Validator.Application.Abstractions;
using Validator.Domain.Calendars;
using Validator.Domain.Candles;

namespace Validator.Application.Tests.Abstractions;

public sealed class ApplicationContractTests
{
    [Fact]
    public void PublicContractRecords_PreserveTheirValues()
    {
        var timestamp = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);
        var candle = new PriceCandle(timestamp, 1m, 2m, 0.5m, 1.5m, 10m);
        var statistics = new CandleDataStatistics(3, 2, 1);
        var prepared = new PreparedCandleData([candle]);
        var request = new LocalCalendarRequest(MarketProfile.Custom, "calendar.json");
        var session = new UtcSession(timestamp, timestamp.AddHours(1));
        var expansion = new ScheduleExpansion([session]);
        var writeOptions = new ReportWriteOptions(ReportFormat.Json, "report.json", true);
        var context = new ValidationContext();
        var occurredAt = timestamp.AddDays(1);
        var fatal = new FatalValidationError(FatalErrorKind.Configuration, "Invalid calendar")
        {
            OccurredAt = occurredAt
        };
        var execution = new ValidationExecution(false, fatal);

        Assert.Equal((3, 2, 1), (statistics.TotalRows, statistics.ValidRows, statistics.MalformedRows));
        Assert.Same(candle, Assert.Single(prepared.Candles));
        Assert.Equal((MarketProfile.Custom, "calendar.json"), (request.Profile, request.CalendarPath));
        Assert.Same(session, Assert.Single(expansion.Sessions));
        Assert.Equal((ReportFormat.Json, "report.json", true),
            (writeOptions.Format, writeOptions.OutputPath, writeOptions.Verbose));
        Assert.NotNull(context);
        Assert.Equal((FatalErrorKind.Configuration, "Invalid calendar", occurredAt),
            (fatal.Kind, fatal.Message, fatal.OccurredAt));
        Assert.False(execution.Succeeded);
        Assert.Same(fatal, execution.FatalError);
    }
}