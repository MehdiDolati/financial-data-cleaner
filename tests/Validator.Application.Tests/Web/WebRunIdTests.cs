using System;
using Validator.Application.Ingestion;
using Validator.Application.Web;
using static Validator.Application.Web.WebRunRequest;

namespace Validator.Application.Tests.Web;

// Deterministic identity tests. WebRunId is SHA-256 over the source
// fingerprint and the canonical resolved-options string, so wall clock,
// sequence numbers, randomness, upload name, and progress never contribute
// (SC-004, Principle IV, FR-010, FR-012).
public class WebRunIdTests
{
    private static SourceIdentity Source(long size = 100, string? sha = null) =>
        new("dataset.csv", size, sha ?? new string('a', 64));

    private static WebRunOptions Options() => new(
        Timeframe: "D1",
        Market: Domain.Calendars.MarketProfile.Forex,
        CalendarReference: null,
        Csv: new CsvInputOptions { HasHeader = true, Delimiter = "comma" },
        ReportVersion: 2,
        Score: false,
        ScoreWeights: null,
        Instrument: null,
        BenchmarkName: null,
        ToleranceOverrides: null);

    [Fact]
    public void Id_is_exactly_64_lower_case_hex_characters()
    {
        var id = WebRunId.Derive(Source(), Options());

        id.Value.Should().HaveLength(64);
        id.Value.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Identical_source_and_equivalent_options_produce_equal_ids()
    {
        var first = WebRunId.Derive(Source(), Options());
        var second = WebRunId.Derive(Source(), Options());

        first.Should().Be(second);
        first.Value.Should().Be(second.Value);
    }

    [Fact]
    public void One_changed_material_option_produces_a_different_id()
    {
        var baseline = WebRunId.Derive(Source(), Options());
        var changedTimeframe = WebRunId.Derive(Source(), Options() with { Timeframe = "H1" });
        var changedDelimiter = WebRunId.Derive(
            Source(),
            Options() with { Csv = new CsvInputOptions { HasHeader = true, Delimiter = "semicolon" } });
        var changedHeader = WebRunId.Derive(
            Source(),
            Options() with { Csv = new CsvInputOptions { HasHeader = false, Delimiter = "comma" } });
        var changedVersion = WebRunId.Derive(Source(), Options() with { ReportVersion = 1 });
        var changedScore = WebRunId.Derive(Source(), Options() with { Score = true });
        var changedBenchmark = WebRunId.Derive(Source(), Options() with { BenchmarkName = "audusd-d1" });
        var changedTolerance = WebRunId.Derive(Source(), Options() with { ToleranceOverrides = "{}" });
        var changedMarket = WebRunId.Derive(
            Source(),
            Options() with { Market = Domain.Calendars.MarketProfile.Equities });

        changedTimeframe.Should().NotBe(baseline);
        changedDelimiter.Should().NotBe(baseline);
        changedHeader.Should().NotBe(baseline);
        changedVersion.Should().NotBe(baseline);
        changedScore.Should().NotBe(baseline);
        changedBenchmark.Should().NotBe(baseline);
        changedTolerance.Should().NotBe(baseline);
        changedMarket.Should().NotBe(baseline);
    }

    [Fact]
    public void Different_source_bytes_produce_a_different_id()
    {
        var first = WebRunId.Derive(Source(sha: new string('a', 64)), Options());
        var second = WebRunId.Derive(Source(sha: new string('b', 64)), Options());

        first.Should().NotBe(second);
    }

    [Fact]
    public void Upload_name_and_user_correlation_never_contribute()
    {
        // SourceIdentity carries the upload name, but the id derivation uses
        // only its SHA-256; a rename leaves the id untouched.
        var named = WebRunId.Derive(new SourceIdentity("upload-1.csv", 100, new string('a', 64)), Options());
        var renamed = WebRunId.Derive(new SourceIdentity("upload-2.csv", 100, new string('a', 64)), Options());

        named.Should().Be(renamed);
    }

    [Fact]
    public void ByteSize_alone_does_not_change_the_id()
    {
        var first = WebRunId.Derive(new SourceIdentity("a.csv", 100, new string('a', 64)), Options());
        var second = WebRunId.Derive(new SourceIdentity("b.csv", 999, new string('a', 64)), Options());

        first.Should().Be(second);
    }

    [Fact]
    public void Null_option_values_are_canonicalized_consistently()
    {
        var first = WebRunId.Derive(Source(), Options() with { Timeframe = null });
        var second = WebRunId.Derive(Source(), Options() with { Timeframe = null });

        first.Should().Be(second);
        first.Should().NotBe(WebRunId.Derive(Source(), Options()));
    }

    [Fact]
    public void Options_field_ordering_is_stable_regardless_of_construction()
    {
        // Equivalent option sets built through different construction paths
        // (e.g. differing CsvInputOptions property order) serialize identically.
        var first = WebRunId.Derive(Source(), Options());
        var second = WebRunId.Derive(
            Source(),
            Options() with
            {
                Csv = new CsvInputOptions
                {
                    Delimiter = "comma",
                    HasHeader = true,
                    DateFormat = null,
                    TimeFormat = null,
                    TimestampFormat = null,
                    TimestampColumn = null
                }
            });

        first.Should().Be(second);
    }

    [Fact]
    public void Parse_round_trips_a_valid_value()
    {
        var id = WebRunId.Derive(Source(), Options());

        var parsed = WebRunId.Parse(id.Value);

        parsed.Should().Be(id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde")]
    public void Parse_rejects_malformed_values(string candidate)
    {
        var action = () => WebRunId.Parse(candidate);

        action.Should().Throw<ArgumentException>();
    }
}