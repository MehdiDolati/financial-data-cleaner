using Validator.Application.Abstractions;
using Validator.Application.Reporting;
using Validator.Application.Tests.Doubles;
using Validator.Application.Validation;
using Validator.Domain.Candles;

namespace Validator.Application.Tests;

public sealed class AlternateFrontEndProofTests
{
    [Fact]
    public async Task NonCliFrontEnd_DrivesIdenticalUseCaseThroughApplicationPorts()
    {
        var source = new InMemoryCandleSource(
        [
            Candle(9, high: 2m, low: 1m),
            Candle(10, high: 0.5m, low: 1m)
        ]);
        var writer = new CapturingReportWriter();
        IValidateMarketDataUseCase useCase = new ValidateMarketDataUseCase(source, writer);
        var request = new ValidationRequest("memory.csv", "H1", MarketCalendar: new AlwaysOpenCalendar());

        var exitCode = await useCase.ExecuteAsync(request);

        Assert.Equal(1, exitCode);
        Assert.NotNull(writer.Report);
        Assert.Equal("memory.csv", writer.Report.SourceFile);
        Assert.Equal(2, writer.Report.Summary.ValidRows);
        Assert.Equal(1, writer.Report.Summary.InvalidOhlc);
        Assert.Equal(0, writer.Report.Summary.ClosedMarketRecords);
    }

    [Fact]
    public void ApplicationTestAssembly_DoesNotReferenceInfrastructure()
    {
        var references = typeof(AlternateFrontEndProofTests).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference => reference.Name == "Validator.Infrastructure");
    }

    private static PriceCandle Candle(int hour, decimal high, decimal low) =>
        new(
            new DateTimeOffset(2026, 2, 4, hour, 0, 0, TimeSpan.Zero),
            1m,
            high,
            low,
            1m,
            10m);

    private sealed class CapturingReportWriter : IReportWriter
    {
        public ValidationReport? Report { get; private set; }

        public Task WriteReportAsync(object report)
        {
            Report = Assert.IsType<ValidationReport>(report);
            return Task.CompletedTask;
        }
    }

    private sealed class AlwaysOpenCalendar : IMarketCalendar
    {
        public Validator.Domain.Calendars.MarketProfile Profile =>
            Validator.Domain.Calendars.MarketProfile.Crypto;

        public bool IsOpen(DateTimeOffset timestamp) => true;
    }
}