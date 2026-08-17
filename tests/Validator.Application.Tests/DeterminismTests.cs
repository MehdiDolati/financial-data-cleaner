using Validator.Application.Reporting;
using Validator.Application.Validation;
using Validator.Domain.Findings;
using Validator.Domain.Findings.Evidence;

namespace Validator.Application.Tests;

// Two runs over the same bytes must agree exactly. References are derived only
// from canonical source facts, and findings are ordered by the established
// category order, then applicable UTC timestamp, then applicable source line,
// with the deterministic reference as the documented final tie-breaker.
public sealed class DeterminismTests
{
    private static DateTimeOffset Utc(int day, int hour = 10, int minute = 0) =>
        new(2024, 8, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void ReferenceBuilders_AreStableAcrossInvocations()
    {
        Assert.Equal(
            FindingReferenceFactory.MissingCandle(Utc(1)).Value,
            FindingReferenceFactory.MissingCandle(Utc(1)).Value);
        Assert.Equal(
            FindingReferenceFactory.TimeGap(Utc(1, 11), Utc(1, 13)).Value,
            FindingReferenceFactory.TimeGap(Utc(1, 11), Utc(1, 13)).Value);
        Assert.Equal(
            FindingReferenceFactory.DuplicateRecord(Utc(1), 7).Value,
            FindingReferenceFactory.DuplicateRecord(Utc(1), 7).Value);
        Assert.Equal(
            FindingReferenceFactory.PhysicalRecord(FindingCategory.InvalidOhlc, 5).Value,
            FindingReferenceFactory.PhysicalRecord(FindingCategory.InvalidOhlc, 5).Value);
    }

    [Fact]
    public void ReferenceBuilders_AreInvariantAsciiAndCarryTheirCategory()
    {
        var references = new[]
        {
            FindingReferenceFactory.MissingCandle(Utc(1)).Value,
            FindingReferenceFactory.TimeGap(Utc(1, 11), Utc(1, 13)).Value,
            FindingReferenceFactory.DuplicateRecord(Utc(1), 7).Value,
            FindingReferenceFactory.PhysicalRecord(FindingCategory.InvalidOhlc, 5).Value,
            FindingReferenceFactory.PhysicalRecord(FindingCategory.ClosedMarketRecord, 6).Value,
            FindingReferenceFactory.PhysicalRecord(FindingCategory.MalformedRow, 8).Value
        };

        Assert.All(references, reference => Assert.All(reference, character => Assert.True(character < 128)));
        Assert.Equal(references.Length, references.Distinct(StringComparer.Ordinal).Count());
        Assert.StartsWith("missing-candle:", references[0], StringComparison.Ordinal);
        Assert.StartsWith("time-gap:", references[1], StringComparison.Ordinal);
        Assert.StartsWith("duplicate-record:", references[2], StringComparison.Ordinal);
        Assert.StartsWith("invalid-ohlc:", references[3], StringComparison.Ordinal);
        Assert.StartsWith("closed-market-record:", references[4], StringComparison.Ordinal);
        Assert.StartsWith("malformed-row:", references[5], StringComparison.Ordinal);
    }

    // The same instant written with different source offsets is the same fact,
    // so it must produce one reference rather than two.
    [Fact]
    public void ReferenceBuilders_DependOnTheInstantAndNotOnTheSourceOffset()
    {
        var utc = new DateTimeOffset(2024, 8, 1, 10, 0, 0, TimeSpan.Zero);
        var shifted = new DateTimeOffset(2024, 8, 1, 12, 0, 0, TimeSpan.FromHours(2));

        Assert.Equal(
            FindingReferenceFactory.MissingCandle(utc).Value,
            FindingReferenceFactory.MissingCandle(shifted).Value);
        Assert.Equal(
            FindingReferenceFactory.DuplicateRecord(utc, 3).Value,
            FindingReferenceFactory.DuplicateRecord(shifted, 3).Value);
    }

    [Fact]
    public void ReferenceBuilders_DistinguishDistinctFacts()
    {
        Assert.NotEqual(
            FindingReferenceFactory.MissingCandle(Utc(1)).Value,
            FindingReferenceFactory.MissingCandle(Utc(2)).Value);
        Assert.NotEqual(
            FindingReferenceFactory.MissingCandle(Utc(1, 10)).Value,
            FindingReferenceFactory.MissingCandle(Utc(1, 10, 1)).Value);
        Assert.NotEqual(
            FindingReferenceFactory.DuplicateRecord(Utc(1), 7).Value,
            FindingReferenceFactory.DuplicateRecord(Utc(1), 8).Value);
        Assert.NotEqual(
            FindingReferenceFactory.PhysicalRecord(FindingCategory.InvalidOhlc, 5).Value,
            FindingReferenceFactory.PhysicalRecord(FindingCategory.ClosedMarketRecord, 5).Value);
        Assert.NotEqual(
            FindingReferenceFactory.TimeGap(Utc(1, 11), Utc(1, 13)).Value,
            FindingReferenceFactory.TimeGap(Utc(1, 11), Utc(1, 14)).Value);
    }

    // Sub-second facts must remain distinguishable, so a reference key cannot
    // silently truncate precision.
    [Fact]
    public void ReferenceBuilders_PreserveSubSecondPrecision()
    {
        var first = new DateTimeOffset(2024, 8, 1, 10, 0, 0, TimeSpan.Zero).AddTicks(1);
        var second = new DateTimeOffset(2024, 8, 1, 10, 0, 0, TimeSpan.Zero).AddTicks(2);

        Assert.NotEqual(
            FindingReferenceFactory.MissingCandle(first).Value,
            FindingReferenceFactory.MissingCandle(second).Value);
    }

    // Established category order comes first, even where it contradicts the
    // alphabetical order of the references themselves.
    [Fact]
    public void CanonicalOrder_PlacesEstablishedCategoriesBeforeReferenceText()
    {
        var ordered = Sorted(
            Header(FindingCategory.MalformedRow, Utc(1), 8),
            Header(FindingCategory.TimeGap, Utc(1), 7),
            Header(FindingCategory.ClosedMarketRecord, Utc(1), 6),
            Header(FindingCategory.InvalidOhlc, Utc(1), 5),
            Header(FindingCategory.DuplicateRecord, Utc(1), 4),
            Header(FindingCategory.MissingCandle, Utc(1), null));

        Assert.Equal(
            [
                FindingCategory.MissingCandle,
                FindingCategory.DuplicateRecord,
                FindingCategory.InvalidOhlc,
                FindingCategory.ClosedMarketRecord,
                FindingCategory.TimeGap,
                FindingCategory.MalformedRow
            ],
            ordered.Select(header => header.Category));
    }

    [Fact]
    public void CanonicalOrder_SortsByUtcInstantWithinACategory()
    {
        var ordered = Sorted(
            Header(FindingCategory.InvalidOhlc, Utc(3), 5),
            Header(FindingCategory.InvalidOhlc, Utc(1), 6),
            Header(FindingCategory.InvalidOhlc, Utc(2), 7));

        Assert.Equal(
            [Utc(1), Utc(2), Utc(3)],
            ordered.Select(header => header.Location.TimestampUtc));
    }

    // An instant is compared as an instant, so a finding recorded with a shifted
    // offset still sorts by the moment it describes.
    [Fact]
    public void CanonicalOrder_ComparesInstantsAndNotWallClockText()
    {
        var early = new DateTimeOffset(2024, 8, 1, 23, 0, 0, TimeSpan.Zero);
        var late = new DateTimeOffset(2024, 8, 2, 0, 0, 0, TimeSpan.Zero);

        var ordered = Sorted(
            Header(FindingCategory.InvalidOhlc, late, 5),
            Header(FindingCategory.InvalidOhlc, early, 9));

        Assert.Equal([early, late], ordered.Select(header => header.Location.TimestampUtc));
    }

    [Fact]
    public void CanonicalOrder_PlacesFindingsWithoutATimestampLast()
    {
        var ordered = Sorted(
            Header(FindingCategory.MalformedRow, null, 4),
            Header(FindingCategory.MalformedRow, Utc(2), 5),
            Header(FindingCategory.MalformedRow, Utc(1), 6));

        Assert.Equal(
            [Utc(1), Utc(2), null],
            ordered.Select(header => header.Location.TimestampUtc));
    }

    [Fact]
    public void CanonicalOrder_BreaksEqualTimestampsByFirstSourceLine()
    {
        var ordered = Sorted(
            Header(FindingCategory.DuplicateRecord, Utc(1), 30),
            Header(FindingCategory.DuplicateRecord, Utc(1), 4),
            Header(FindingCategory.DuplicateRecord, Utc(1), 9));

        Assert.Equal([4L, 9L, 30L], ordered.Select(header => header.Location.SourceLines[0]));
    }

    // An expected-but-absent record has no line to compare, so it sorts after
    // the physical rows that share its instant instead of inventing a position.
    [Fact]
    public void CanonicalOrder_PlacesFindingsWithoutASourceLineLast()
    {
        var ordered = Sorted(
            Header(FindingCategory.MissingCandle, Utc(1), null),
            Header(FindingCategory.MissingCandle, Utc(1), 7));

        Assert.Equal([7L], [.. ordered[0].Location.SourceLines]);
        Assert.Empty(ordered[1].Location.SourceLines);
    }

    [Fact]
    public void CanonicalOrder_UsesTheReferenceAsTheFinalTieBreaker()
    {
        var baseReference = FindingReferenceFactory.PhysicalRecord(FindingCategory.InvalidOhlc, 5);
        var collision = new FindingReference($"{baseReference.Value}:2");

        var ordered = Sorted(
            Header(FindingCategory.InvalidOhlc, Utc(1), 5, collision),
            Header(FindingCategory.InvalidOhlc, Utc(1), 5, baseReference));

        Assert.Equal(
            [baseReference.Value, collision.Value],
            ordered.Select(header => header.Reference.Value));
    }

    [Fact]
    public void CanonicalOrder_IsIndependentOfTheOrderFindingsWereProduced()
    {
        var headers = new[]
        {
            Header(FindingCategory.MissingCandle, Utc(2), null),
            Header(FindingCategory.MissingCandle, Utc(1), null),
            Header(FindingCategory.DuplicateRecord, Utc(1), 4),
            Header(FindingCategory.InvalidOhlc, Utc(3), 5),
            Header(FindingCategory.ClosedMarketRecord, Utc(1), 6),
            Header(FindingCategory.TimeGap, Utc(1), null),
            Header(FindingCategory.MalformedRow, null, 8)
        };

        var expected = Sorted(headers).Select(header => header.Reference.Value).ToArray();

        foreach (var permutation in new[] { headers.Reverse().ToArray(), Rotate(headers), Interleave(headers) })
        {
            Assert.Equal(expected, Sorted(permutation).Select(header => header.Reference.Value));
        }
    }

    [Fact]
    public void CanonicalOrder_IsAConsistentTotalOrder()
    {
        var headers = new[]
        {
            Header(FindingCategory.MissingCandle, Utc(1), null),
            Header(FindingCategory.DuplicateRecord, Utc(1), 4),
            Header(FindingCategory.InvalidOhlc, Utc(1), 5),
            Header(FindingCategory.MalformedRow, null, 8)
        };

        foreach (var left in headers)
        {
            Assert.Equal(0, CanonicalFindingOrder.Instance.Compare(left, left));

            foreach (var right in headers.Where(candidate => !ReferenceEquals(candidate, left)))
            {
                var forward = Math.Sign(CanonicalFindingOrder.Instance.Compare(left, right));
                var backward = Math.Sign(CanonicalFindingOrder.Instance.Compare(right, left));
                Assert.NotEqual(0, forward);
                Assert.Equal(-forward, backward);
            }
        }
    }

    // The spool sorts findings as text before the comparer ever sees them, so
    // the two orders must agree; if they drifted, a spooled run and an in-memory
    // run would publish the same findings in different orders.
    [Fact]
    public void SortKey_OrdersFindingsIdenticallyToTheComparer()
    {
        var headers = new[]
        {
            Header(FindingCategory.MalformedRow, null, 8),
            Header(FindingCategory.MissingCandle, Utc(2), null),
            Header(FindingCategory.MissingCandle, Utc(1), null),
            Header(FindingCategory.DuplicateRecord, Utc(1), 7),
            Header(FindingCategory.DuplicateRecord, Utc(1), 3),
            Header(FindingCategory.InvalidOhlc, Utc(1), 5),
            Header(FindingCategory.TimeGap, Utc(1, 11), null),
            Header(FindingCategory.ClosedMarketRecord, Utc(3), 9)
        };

        var byComparer = headers.OrderBy(header => header, CanonicalFindingOrder.Instance)
            .Select(header => header.Reference.Value);
        var bySortKey = headers.OrderBy(CanonicalFindingOrder.SortKey, StringComparer.Ordinal)
            .Select(header => header.Reference.Value);

        Assert.Equal(byComparer, bySortKey);
    }

    [Fact]
    public void SortKey_PlacesAFindingWithoutATimestampOrLineAfterOneThatHasThem()
    {
        var located = CanonicalFindingOrder.SortKey(Header(FindingCategory.MalformedRow, Utc(1), 4));
        var unlocated = CanonicalFindingOrder.SortKey(Header(
            FindingCategory.MalformedRow,
            null,
            null,
            FindingReferenceFactory.PhysicalRecord(FindingCategory.MalformedRow, 4)));

        Assert.True(string.CompareOrdinal(located, unlocated) < 0);
        Assert.Equal(CanonicalFindingOrder.SortKey(Header(FindingCategory.MalformedRow, Utc(1), 4)), located);
        Assert.Throws<ArgumentNullException>(() => CanonicalFindingOrder.SortKey(null!));
    }

    [Fact]
    public void Comparer_TreatsAnAbsentFindingAsSortingBeforeAPresentOne()
    {
        var present = Header(FindingCategory.MissingCandle, Utc(1), null);

        Assert.Equal(0, CanonicalFindingOrder.Instance.Compare(null, null));
        Assert.True(CanonicalFindingOrder.Instance.Compare(null, present) < 0);
        Assert.True(CanonicalFindingOrder.Instance.Compare(present, null) > 0);
    }

    // The ordering relies on the six established categories being declared in
    // canonical order, so that assumption is asserted rather than trusted.
    [Fact]
    public void EstablishedCategoriesAreDeclaredInCanonicalReportOrder()
    {
        Assert.Equal(
            [0, 1, 2, 3, 4, 5],
            new[]
            {
                FindingCategory.MissingCandle,
                FindingCategory.DuplicateRecord,
                FindingCategory.InvalidOhlc,
                FindingCategory.ClosedMarketRecord,
                FindingCategory.TimeGap,
                FindingCategory.MalformedRow
            }.Select(category => (int)category));
    }

    private static DetailedFindingHeader[] Sorted(params DetailedFindingHeader[] headers) =>
        [.. headers.OrderBy(header => header, CanonicalFindingOrder.Instance)];

    private static DetailedFindingHeader[] Rotate(DetailedFindingHeader[] headers) =>
        [.. headers.Skip(3), .. headers.Take(3)];

    private static DetailedFindingHeader[] Interleave(DetailedFindingHeader[] headers) =>
    [
        .. headers.Where((_, index) => index % 2 == 1),
        .. headers.Where((_, index) => index % 2 == 0)
    ];

    private static DetailedFindingHeader Header(
        FindingCategory category,
        DateTimeOffset? timestampUtc,
        long? sourceLine,
        FindingReference? reference = null) =>
        new(
            reference ?? ReferenceFor(category, timestampUtc, sourceLine),
            category,
            "Finding",
            "A finding was detected.",
            1,
            new FindingLocation(sourceLine.HasValue ? [sourceLine.Value] : null, timestampUtc),
            DetailedFindingHeader.EvidenceKindOf(category),
            "Review the source rows.");

    private static FindingReference ReferenceFor(
        FindingCategory category,
        DateTimeOffset? timestampUtc,
        long? sourceLine) => category switch
    {
        FindingCategory.MissingCandle => FindingReferenceFactory.MissingCandle(timestampUtc!.Value),
        FindingCategory.TimeGap => FindingReferenceFactory.TimeGap(timestampUtc!.Value, timestampUtc.Value),
        FindingCategory.DuplicateRecord => FindingReferenceFactory.DuplicateRecord(timestampUtc!.Value, sourceLine!.Value),
        _ => FindingReferenceFactory.PhysicalRecord(category, sourceLine!.Value)
    };
}
