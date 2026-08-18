using System.Text.Json;
using System.Text.Json.Serialization;
using Validator.Domain.Findings;
using Validator.Domain.Findings.Evidence;
using Validator.Domain.Timeframes;

namespace Validator.Domain.Tests.Findings;

public sealed class EvidenceModelTests
{
    private static readonly FindingReference GapReference =
        new("time-gap:20240801T1000000000000Z:20240801T1200000000000Z");
    private static readonly FindingReference CandleReference =
        new("missing-candle:20240801T1000000000000Z");
    private static readonly Timeframe H1 = Timeframe.Parse("H1");

    private static DateTimeOffset Ts(byte hour) =>
        new(2024, 8, 1, hour, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void MissingCandleEvidence_RejectsNonUtcTimestamps()
    {
        var local = new DateTimeOffset(2024, 8, 1, 10, 0, 0, TimeSpan.FromHours(2));

        Assert.Throws<ArgumentException>(() => new MissingCandleEvidence(local, H1, GapReference));
        Assert.Throws<ArgumentException>(() =>
            new MissingCandleEvidence(Ts(10), H1, GapReference, previousObservedTimestampUtc: local));
        Assert.Throws<ArgumentException>(() =>
            new MissingCandleEvidence(Ts(10), H1, GapReference, nextObservedTimestampUtc: local));
    }

    [Fact]
    public void MissingCandleEvidence_RejectsMissingArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new MissingCandleEvidence(Ts(10), null!, GapReference));
        Assert.Throws<ArgumentNullException>(() => new MissingCandleEvidence(Ts(10), H1, null!));
    }

    [Fact]
    public void MissingCandleEvidence_ExposesStableFields()
    {
        var evidence = new MissingCandleEvidence(
            Ts(10), H1, GapReference,
            previousObservedTimestampUtc: Ts(9),
            nextObservedTimestampUtc: Ts(13));

        Assert.Equal(Ts(10), evidence.ExpectedTimestampUtc);
        Assert.Equal(H1, evidence.ExpectedTimeframe);
        Assert.Equal(GapReference, evidence.TimeGapReference);
        Assert.Equal(Ts(9), evidence.PreviousObservedTimestampUtc);
        Assert.Equal(Ts(13), evidence.NextObservedTimestampUtc);
    }

    [Fact]
    public void TimeGapEvidence_RejectsInvalidOrdering()
    {
        Assert.Throws<ArgumentException>(() => new TimeGapEvidence(Ts(12), Ts(10), H1, 2, 7200));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeGapEvidence(Ts(10), Ts(12), H1, 0, 7200));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeGapEvidence(Ts(10), Ts(12), H1, 2, 0));
        Assert.Throws<ArgumentNullException>(() => new TimeGapEvidence(Ts(10), Ts(12), null!, 2, 7200));
        Assert.Throws<ArgumentException>(() =>
            new TimeGapEvidence(Ts(10), Ts(12), H1, 2, 7200, previousObservedTimestampUtc: new DateTimeOffset(2024, 8, 1, 12, 0, 0, TimeSpan.FromHours(3))));
    }

    [Fact]
    public void TimeGapEvidence_ExposesStableFields()
    {
        var evidence = new TimeGapEvidence(
            Ts(10), Ts(12), H1, 2, 7200,
            previousObservedTimestampUtc: Ts(9),
            nextObservedTimestampUtc: Ts(13));

        Assert.Equal(Ts(10), evidence.FirstMissingTimestampUtc);
        Assert.Equal(Ts(12), evidence.LastMissingTimestampUtc);
        Assert.Equal(H1, evidence.ExpectedTimeframe);
        Assert.Equal(2, evidence.MissingCandleCount);
        Assert.Equal(7200, evidence.ElapsedSeconds);
        Assert.Equal(Ts(9), evidence.PreviousObservedTimestampUtc);
        Assert.Equal(Ts(13), evidence.NextObservedTimestampUtc);
    }

    [Fact]
    public void DuplicateRecordEvidence_RejectsLocalTimestamp()
    {
        var local = new DateTimeOffset(2024, 8, 1, 10, 0, 0, TimeSpan.FromHours(2));

        Assert.Throws<ArgumentException>(() =>
            new DuplicateRecordEvidence(local, DuplicateClassification.Exact));
    }

    [Fact]
    public void DuplicateRecordEvidence_EnforcesClassificationFieldRules()
    {
        Assert.Throws<ArgumentException>(() =>
            new DuplicateRecordEvidence(Ts(10), DuplicateClassification.Exact, ["Open"]));
        Assert.Throws<ArgumentException>(() =>
            new DuplicateRecordEvidence(Ts(10), DuplicateClassification.Conflicting, []));
        Assert.Throws<ArgumentException>(() =>
            new DuplicateRecordEvidence(Ts(10), DuplicateClassification.Conflicting, ["Unknown"]));
    }

    [Fact]
    public void DuplicateRecordEvidence_ExposesStableFields()
    {
        var evidence = new DuplicateRecordEvidence(
            Ts(10), DuplicateClassification.Conflicting, ["Close", "Volume"]);

        Assert.Equal(Ts(10), evidence.SharedTimestampUtc);
        Assert.Equal(DuplicateClassification.Conflicting, evidence.Classification);
        Assert.Equal(["Close", "Volume"], evidence.DifferingFields);
    }

    [Fact]
    public void DuplicateRowEvidence_RejectsNonPositiveSourceLine()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DuplicateRowEvidence(0, "2024.08.01 10:00", 1, 2, 3, 4, 5));
    }

    [Fact]
    public void DuplicateRowEvidence_ExposesObservedValues()
    {
        var row = new DuplicateRowEvidence(42, "2024.08.01 10:00", 1.1m, 2.2m, 0.9m, 2.1m, 100m);

        Assert.Equal(42, row.SourceLine);
        Assert.Equal("2024.08.01 10:00", row.OriginalTimestampText);
        Assert.Equal(1.1m, row.Open);
        Assert.Equal(2.2m, row.High);
        Assert.Equal(0.9m, row.Low);
        Assert.Equal(2.1m, row.Close);
        Assert.Equal(100m, row.Volume);
    }

    [Fact]
    public void InvalidOhlcEvidence_RejectsMissingArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new InvalidOhlcEvidence(null!, [OhlcViolationCode.LOW_ABOVE_HIGH]));
        Assert.Throws<ArgumentException>(() => new InvalidOhlcEvidence(new OhlcValues(1, 2, 0.5m, 1.5m, 10), []));
        Assert.Throws<ArgumentException>(() =>
            new InvalidOhlcEvidence(
                new OhlcValues(1, 2, 0.5m, 1.5m, 10),
                [OhlcViolationCode.HIGH_BELOW_OPEN, OhlcViolationCode.HIGH_BELOW_OPEN]));
    }

    [Fact]
    public void InvalidOhlcEvidence_ExposesStableCodes()
    {
        var evidence = new InvalidOhlcEvidence(
            new OhlcValues(1, 0.5m, 0.4m, 1.5m, -3),
            [OhlcViolationCode.HIGH_BELOW_OPEN, OhlcViolationCode.NEGATIVE_VOLUME]);

        Assert.Equal(0.5m, evidence.Observed.High);
        Assert.Equal(2, evidence.Violations.Count);
        Assert.Contains(OhlcViolationCode.HIGH_BELOW_OPEN, evidence.Violations);
        Assert.Contains(OhlcViolationCode.NEGATIVE_VOLUME, evidence.Violations);
    }

    [Fact]
    public void ClosedMarketRecordEvidence_RejectsEmptyArguments()
    {
        Assert.Throws<ArgumentException>(() =>
            new ClosedMarketRecordEvidence(" ", "Forex", "Weekend closed"));
        Assert.Throws<ArgumentException>(() =>
            new ClosedMarketRecordEvidence("forex", " ", "Weekend closed"));
        Assert.Throws<ArgumentException>(() =>
            new ClosedMarketRecordEvidence("forex", "Forex", " "));
    }

    [Fact]
    public void ClosedMarketRecordEvidence_ExposesStableFields()
    {
        var evidence = new ClosedMarketRecordEvidence(
            "forex", "Forex 24-5", "Weekend closed",
            calendarTimeZone: null,
            boundary: new UtcBoundary(Ts(21), Ts(22)));

        Assert.Equal("forex", evidence.MarketProfile);
        Assert.Equal("Forex 24-5", evidence.CalendarName);
        Assert.Equal("Weekend closed", evidence.ClosedRule);
        Assert.Null(evidence.CalendarTimeZone);
        Assert.NotNull(evidence.Boundary);
    }

    [Fact]
    public void UtcBoundary_ValidatesOrderingAndUtc()
    {
        Assert.Throws<ArgumentException>(() => new UtcBoundary(Ts(10), Ts(10)));
        Assert.Throws<ArgumentException>(() => new UtcBoundary(Ts(11), Ts(10)));
        Assert.Throws<ArgumentException>(() =>
            new UtcBoundary(new DateTimeOffset(2024, 8, 1, 10, 0, 0, TimeSpan.FromHours(1)), Ts(12)));
    }

    [Fact]
    public void MalformedRowEvidence_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MalformedRowEvidence(0));
        Assert.Throws<ArgumentException>(() =>
            new MalformedRowEvidence(5, new DateTimeOffset(2024, 8, 1, 10, 0, 0, TimeSpan.FromHours(2))));
    }

    [Fact]
    public void MalformedRowEvidence_ExposesStableFields()
    {
        var evidence = new MalformedRowEvidence(
            108, Ts(10), "2024.08.01 10:00", expectedSlotReserved: true);

        Assert.Equal(108, evidence.SourceLine);
        Assert.Equal(Ts(10), evidence.ParsedTimestampUtc);
        Assert.Equal("2024.08.01 10:00", evidence.OriginalTimestampText);
        Assert.True(evidence.ExpectedSlotReserved);
    }

    [Fact]
    public void MalformedFieldError_RejectsEmptyArguments()
    {
        Assert.Throws<ArgumentException>(() =>
            new MalformedFieldError(" ", "abc", MalformedReasonCode.INVALID_DECIMAL, "Not a decimal."));
        Assert.Throws<ArgumentException>(() =>
            new MalformedFieldError("Open", "abc", MalformedReasonCode.INVALID_DECIMAL, " "));
    }

    [Fact]
    public void MalformedFieldError_ExposesStableFields()
    {
        var error = new MalformedFieldError(
            "Open", "abc", MalformedReasonCode.INVALID_DECIMAL, "Not an invariant decimal.");

        Assert.Equal("Open", error.Field);
        Assert.Equal("abc", error.OriginalValue);
        Assert.Equal(MalformedReasonCode.INVALID_DECIMAL, error.ReasonCode);
        Assert.Equal("Not an invariant decimal.", error.Reason);
    }

    [Fact]
    public void EvidenceUnion_ExposesStableKindDiscriminants()
    {
        Assert.Equal("MissingCandle", new FindingEvidenceRecord.MissingCandle(CandleReference, new MissingCandleEvidence(Ts(10), H1, GapReference)).Kind);
        Assert.Equal("TimeGap", new FindingEvidenceRecord.TimeGapHeader(GapReference, new TimeGapEvidence(Ts(10), Ts(12), H1, 2, 7200)).Kind);
        Assert.Equal("TimeGapMissingReference", new FindingEvidenceRecord.TimeGapMissingReference(GapReference, CandleReference, 0).Kind);
        Assert.Equal("DuplicateRecord", new FindingEvidenceRecord.DuplicateHeader(CandleReference, new DuplicateRecordEvidence(Ts(10), DuplicateClassification.Exact)).Kind);
        Assert.Equal("DuplicateDifferingField", new FindingEvidenceRecord.DuplicateDifferingField(CandleReference, "Close", 0).Kind);
        Assert.Equal("DuplicateRow", new FindingEvidenceRecord.DuplicateRow(CandleReference, new DuplicateRowEvidence(42, null, 1, 2, 3, 4, 5), 0).Kind);
        Assert.Equal("InvalidOhlc", new FindingEvidenceRecord.InvalidOhlcValues(CandleReference, new OhlcValues(1, 2, 3, 4, 5)).Kind);
        Assert.Equal("InvalidOhlcViolation", new FindingEvidenceRecord.InvalidOhlcViolation(CandleReference, OhlcViolationCode.HIGH_BELOW_LOW, 0).Kind);
        Assert.Equal("ClosedMarketRecord", new FindingEvidenceRecord.ClosedMarket(CandleReference, new ClosedMarketRecordEvidence("forex", "Forex 24-5", "Weekend closed")).Kind);
        Assert.Equal("MalformedRow", new FindingEvidenceRecord.MalformedHeader(CandleReference, new MalformedRowEvidence(42)).Kind);
        Assert.Equal("MalformedFieldError", new FindingEvidenceRecord.MalformedFieldErrorRecord(CandleReference, new MalformedFieldError("Open", "x", MalformedReasonCode.INVALID_DECIMAL, "r"), 0).Kind);
        Assert.Equal("MalformedSkippedCheck", new FindingEvidenceRecord.MalformedSkippedCheck(CandleReference, CheckName.InvalidOhlc, 0).Kind);
    }

    [Fact]
    public void EvidenceUnion_HeaderVariantsCarryZeroChildOrderByDefault()
    {
        var header = new FindingEvidenceRecord.MissingCandle(CandleReference, new MissingCandleEvidence(Ts(10), H1, GapReference));

        Assert.Equal(0, header.ChildOrder);
        Assert.Equal(CandleReference, header.Finding);
    }

    [Fact]
    public void MissingCandleEvidence_SerializesWithDocumentedFields()
    {
        var evidence = new MissingCandleEvidence(Ts(10), H1, GapReference);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(evidence, SerializerOptions));
        var root = document.RootElement;

        Assert.Equal(Ts(10), root.GetProperty("ExpectedTimestampUtc").GetDateTimeOffset());
        Assert.Equal("H1", root.GetProperty("ExpectedTimeframe").GetProperty("Unit").GetString() + root.GetProperty("ExpectedTimeframe").GetProperty("Value").GetInt32());
        Assert.Equal(GapReference.Value, root.GetProperty("TimeGapReference").GetProperty("Value").GetString());
    }

    [Fact]
    public void DuplicateRecordEvidence_SerializesWithCodeStrings()
    {
        var evidence = new DuplicateRecordEvidence(Ts(10), DuplicateClassification.Conflicting, ["Close"]);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(evidence, SerializerOptions));
        var root = document.RootElement;

        Assert.Equal("Conflicting", root.GetProperty("Classification").GetString());
        Assert.Equal("Close", root.GetProperty("DifferingFields")[0].GetString());
    }

    [Fact]
    public void MalformedFieldError_SerializesWithReasonCodeString()
    {
        var error = new MalformedFieldError("Open", "abc", MalformedReasonCode.INVALID_DECIMAL, "Not a decimal.");

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(error, SerializerOptions));
        var root = document.RootElement;

        Assert.Equal("INVALID_DECIMAL", root.GetProperty("ReasonCode").GetString());
        Assert.Equal("abc", root.GetProperty("OriginalValue").GetString());
    }

    [Fact]
    public void TimeGapEvidence_SerializesAllDocumentedFields()
    {
        var evidence = new TimeGapEvidence(Ts(10), Ts(12), H1, 2, 7200);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(evidence, SerializerOptions));
        var root = document.RootElement;

        Assert.Equal(Ts(10), root.GetProperty("FirstMissingTimestampUtc").GetDateTimeOffset());
        Assert.Equal(Ts(12), root.GetProperty("LastMissingTimestampUtc").GetDateTimeOffset());
        Assert.Equal(2, root.GetProperty("MissingCandleCount").GetInt64());
        Assert.Equal(7200, root.GetProperty("ElapsedSeconds").GetInt64());
    }
}