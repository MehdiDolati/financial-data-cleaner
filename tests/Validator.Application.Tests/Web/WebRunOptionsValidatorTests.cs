using System;
using Validator.Application.Ingestion;
using Validator.Application.Web;
using DomainTimeframes = Validator.Domain.Timeframes;

namespace Validator.Application.Tests.Web;

// Pre-read option validation tests. Every rule in the integration contract's
// option table is exercised; each rejection carries INVALID_ARGUMENT with the
// specific correction required, and validation completes before any content
// byte is interpreted (FR-007, SC-003).
public class WebRunOptionsValidatorTests
{
    private static WebRunOptions Valid() => new(
        Timeframe: null,
        Market: Domain.Calendars.MarketProfile.Forex,
        CalendarReference: null,
        Csv: new CsvInputOptions(),
        ReportVersion: 2,
        Score: false,
        ScoreWeights: null,
        Instrument: null,
        BenchmarkName: null,
        ToleranceOverrides: null);

    private static FatalDiagnostic Validate(WebRunOptions options, WebRunOperation operation) =>
        WebRunOptionsValidator.Validate(operation, options);

    [Fact]
    public void A_minimal_valid_configuration_passes()
    {
        var result = Validate(Valid(), WebRunOperation.Validate);

        result.Should().BeNull();
    }

    [Fact]
    public void ReportVersion_must_be_1_or_2()
    {
        Validate(Valid() with { ReportVersion = 0 }, WebRunOperation.Validate)
            .Should().NotBeNull()
            .And.Match<FatalDiagnostic>(d => d.Code == "INVALID_ARGUMENT");
        Validate(Valid() with { ReportVersion = 3 }, WebRunOperation.Validate)
            .Should().NotBeNull()
            .And.Match<FatalDiagnostic>(d => d.Code == "INVALID_ARGUMENT");
        Validate(Valid() with { ReportVersion = 1 }, WebRunOperation.Validate)
            .Should().BeNull();
    }

    [Fact]
    public void Score_weights_require_scoring_enabled()
    {
        var diagnostic = Validate(
            Valid() with { Score = false, ScoreWeights = "missingCandles=1" },
            WebRunOperation.Validate);

        diagnostic.Should().NotBeNull();
        diagnostic!.Code.Should().Be("INVALID_ARGUMENT");
        diagnostic.Guidance.Should().Contain("Score");
    }

    [Fact]
    public void Scoring_is_unavailable_under_the_frozen_v1_json_contract()
    {
        // Scoring requires the v2 report; Score + v1 must be rejected up front.
        var diagnostic = Validate(
            Valid() with { ReportVersion = 1, Score = true },
            WebRunOperation.Validate);

        diagnostic.Should().NotBeNull();
        diagnostic!.Code.Should().Be("INVALID_ARGUMENT");
        diagnostic.Guidance.Should().Contain("v2").And.Contain("score");
    }

    [Fact]
    public void Scoring_under_v2_is_accepted()
    {
        Validate(Valid() with { ReportVersion = 2, Score = true }, WebRunOperation.Validate)
            .Should().BeNull();
    }

    [Fact]
    public void Benchmark_and_compare_require_scoring_and_v2()
    {
        // Establish with scoring + v2 passes.
        Validate(
            Valid() with { ReportVersion = 2, Score = true, Instrument = "AUDUSD", BenchmarkName = "audusd-d1" },
            WebRunOperation.EstablishBenchmark)
            .Should().BeNull();

        // Establish without scoring fails.
        Validate(
            Valid() with { ReportVersion = 2, Score = false, Instrument = "AUDUSD", BenchmarkName = "audusd-d1" },
            WebRunOperation.EstablishBenchmark)
            .Should().NotBeNull()
            .And.Match<FatalDiagnostic>(d => d.Code == "INVALID_ARGUMENT");

        // Establish with v1 fails.
        Validate(
            Valid() with { ReportVersion = 1, Score = true, Instrument = "AUDUSD", BenchmarkName = "audusd-d1" },
            WebRunOperation.EstablishBenchmark)
            .Should().NotBeNull()
            .And.Match<FatalDiagnostic>(d => d.Code == "INVALID_ARGUMENT");

        // Compare mirrors the same rules.
        Validate(
            Valid() with { ReportVersion = 2, Score = true, Instrument = "AUDUSD", BenchmarkName = "audusd-d1" },
            WebRunOperation.Compare)
            .Should().BeNull();
        Validate(
            Valid() with { ReportVersion = 2, Score = false, Instrument = "AUDUSD", BenchmarkName = "audusd-d1" },
            WebRunOperation.Compare)
            .Should().NotBeNull()
            .And.Match<FatalDiagnostic>(d => d.Code == "INVALID_ARGUMENT");
    }

    [Fact]
    public void Benchmark_and_compare_require_an_instrument_identity()
    {
        Validate(
            Valid() with { ReportVersion = 2, Score = true, Instrument = null, BenchmarkName = "audusd-d1" },
            WebRunOperation.EstablishBenchmark)
            .Should().NotBeNull()
            .And.Match<FatalDiagnostic>(d =>
                d.Code == "INVALID_ARGUMENT" && d.Guidance.Contains("instrument", StringComparison.OrdinalIgnoreCase));

        Validate(
            Valid() with { ReportVersion = 2, Score = true, Instrument = "  ", BenchmarkName = "audusd-d1" },
            WebRunOperation.Compare)
            .Should().NotBeNull()
            .And.Match<FatalDiagnostic>(d =>
                d.Code == "INVALID_ARGUMENT" && d.Guidance.Contains("instrument", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Benchmark_and_compare_require_a_benchmark_name()
    {
        Validate(
            Valid() with { ReportVersion = 2, Score = true, Instrument = "AUDUSD", BenchmarkName = null },
            WebRunOperation.EstablishBenchmark)
            .Should().NotBeNull()
            .And.Match<FatalDiagnostic>(d => d.Code == "INVALID_ARGUMENT");

        Validate(
            Valid() with { ReportVersion = 2, Score = true, Instrument = "AUDUSD", BenchmarkName = null },
            WebRunOperation.Compare)
            .Should().NotBeNull()
            .And.Match<FatalDiagnostic>(d => d.Code == "INVALID_ARGUMENT");
    }

    [Fact]
    public void Validate_operation_rejects_benchmark_only_options()
    {
        // A plain validation run has no business carrying an instrument or a
        // benchmark name; rejecting early prevents a silent mismatch later.
        Validate(Valid() with { Instrument = "AUDUSD" }, WebRunOperation.Validate)
            .Should().NotBeNull()
            .And.Match<FatalDiagnostic>(d => d.Code == "INVALID_ARGUMENT");

        Validate(Valid() with { BenchmarkName = "audusd-d1" }, WebRunOperation.Validate)
            .Should().NotBeNull()
            .And.Match<FatalDiagnostic>(d => d.Code == "INVALID_ARGUMENT");
    }

    [Fact]
    public void Tolerance_overrides_require_a_comparison()
    {
        var diagnostic = Validate(Valid() with { ToleranceOverrides = "{}" }, WebRunOperation.Validate);

        diagnostic.Should().NotBeNull();
        diagnostic!.Code.Should().Be("INVALID_ARGUMENT");
        diagnostic.Guidance.Should().Contain("compar", StringComparison.OrdinalIgnoreCase);

        // Under Compare the same override is syntactically acceptable.
        Validate(
            Valid() with
            {
                ReportVersion = 2,
                Score = true,
                Instrument = "AUDUSD",
                BenchmarkName = "audusd-d1",
                ToleranceOverrides = "{}"
            },
            WebRunOperation.Compare)
            .Should().BeNull();
    }

    [Fact]
    public void Timeframe_override_must_be_a_canonical_code()
    {
        Validate(Valid() with { Timeframe = "H1" }, WebRunOperation.Validate)
            .Should().BeNull();
        Validate(Valid() with { Timeframe = "M15" }, WebRunOperation.Validate)
            .Should().BeNull();
        Validate(Valid() with { Timeframe = "D1" }, WebRunOperation.Validate)
            .Should().BeNull();

        Validate(Valid() with { Timeframe = "hourly" }, WebRunOperation.Validate)
            .Should().NotBeNull()
            .And.Match<FatalDiagnostic>(d => d.Code == "INVALID_ARGUMENT");
        Validate(Valid() with { Timeframe = "D" }, WebRunOperation.Validate)
            .Should().NotBeNull()
            .And.Match<FatalDiagnostic>(d => d.Code == "INVALID_ARGUMENT");
        Validate(Valid() with { Timeframe = "H0" }, WebRunOperation.Validate)
            .Should().NotBeNull()
            .And.Match<FatalDiagnostic>(d => d.Code == "INVALID_ARGUMENT");
    }

    [Fact]
    public void Csv_option_combinations_are_validated()
    {
        // TimestampFormat without TimestampColumn is rejected by
        // CsvInputOptions.Validate() and surfaced as INVALID_ARGUMENT.
        Validate(
            Valid() with { Csv = new CsvInputOptions { TimestampFormat = "yyyy-MM-dd HH:mm:ss" } },
            WebRunOperation.Validate)
            .Should().NotBeNull()
            .And.Match<FatalDiagnostic>(d => d.Code == "INVALID_ARGUMENT");

        // TzOffset outside +/-14h is rejected.
        Validate(
            Valid() with { Csv = new CsvInputOptions { TzOffset = TimeSpan.FromHours(15) } },
            WebRunOperation.Validate)
            .Should().NotBeNull()
            .And.Match<FatalDiagnostic>(d => d.Code == "INVALID_ARGUMENT");

        // A consistent combination passes.
        Validate(
            Valid() with
            {
                Csv = new CsvInputOptions
                {
                    HasHeader = true,
                    Delimiter = "semicolon",
                    TimestampFormat = "yyyy-MM-dd HH:mm:ss",
                    TimestampColumn = "1"
                }
            },
            WebRunOperation.Validate)
            .Should().BeNull();
    }

    [Fact]
    public void Score_weights_must_cover_all_six_metrics()
    {
        // Five of six metrics: rejected by ScoreWeightParser and surfaced as
        // INVALID_ARGUMENT before dataset processing.
        var fiveSixths = "missingCandles=1,duplicateRecords=1,invalidOhlc=1,closedMarketRecords=1,timeGaps=1";
        var diagnostic = Validate(
            Valid() with { ReportVersion = 2, Score = true, ScoreWeights = fiveSixths },
            WebRunOperation.Validate);

        diagnostic.Should().NotBeNull();
        diagnostic!.Code.Should().Be("INVALID_ARGUMENT");
        diagnostic.Guidance.Should().Contain("metric", StringComparison.OrdinalIgnoreCase);

        // All six with one zero weight is valid (non-zero total).
        var allSix = fiveSixths + ",malformedRows=0";
        Validate(
            Valid() with { ReportVersion = 2, Score = true, ScoreWeights = allSix },
            WebRunOperation.Validate)
            .Should().BeNull();

        // All-zero weights are rejected.
        var allZero = "missingCandles=0,duplicateRecords=0,invalidOhlc=0,closedMarketRecords=0,timeGaps=0,malformedRows=0";
        Validate(
            Valid() with { ReportVersion = 2, Score = true, ScoreWeights = allZero },
            WebRunOperation.Validate)
            .Should().NotBeNull()
            .And.Match<FatalDiagnostic>(d => d.Code == "INVALID_ARGUMENT");
    }

    [Fact]
    public void Unknown_metric_name_in_weights_is_rejected()
    {
        var diagnostic = Validate(
            Valid() with { ReportVersion = 2, Score = true, ScoreWeights = "notAMetric=1" },
            WebRunOperation.Validate);

        diagnostic.Should().NotBeNull();
        diagnostic!.Code.Should().Be("INVALID_ARGUMENT");
    }

    [Fact]
    public void CalendarReference_requires_custom_market()
    {
        Validate(
            Valid() with { Market = Domain.Calendars.MarketProfile.Custom, CalendarReference = "weekly.json" },
            WebRunOperation.Validate)
            .Should().BeNull();

        Validate(Valid() with { CalendarReference = "weekly.json" }, WebRunOperation.Validate)
            .Should().NotBeNull()
            .And.Match<FatalDiagnostic>(d => d.Code == "INVALID_ARGUMENT");
    }

    [Fact]
    public void Instrument_rejects_path_separators()
    {
        Validate(
            Valid() with
            {
                ReportVersion = 2,
                Score = true,
                Instrument = "AUD/USD",
                BenchmarkName = "audusd-d1"
            },
            WebRunOperation.EstablishBenchmark)
            .Should().NotBeNull()
            .And.Match<FatalDiagnostic>(d => d.Code == "INVALID_ARGUMENT");
    }
}