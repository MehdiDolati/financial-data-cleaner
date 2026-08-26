using System;
using System.Collections.Generic;
using Validator.Application.Validation;
using Xunit;

namespace Validator.Application.Tests.Validation;

// US5 (T052): resolving a bracketing observed timestamp to a physical line.
// When the timestamp occurs on several rows the tightest bracket wins, and an
// unsorted source still yields the temporal neighbours' lines even when those
// lines are non-consecutive or descending.
public sealed class AbsenceAnchorResolutionTests
{
    private static DateTimeOffset Ts(byte hour) =>
        new(2024, 8, 1, hour, 0, 0, TimeSpan.Zero);

    private static AbsenceAnchorResolver Build(params (byte Hour, long Line)[] rows)
    {
        var anchors = new List<ObservedRowAnchor>();
        foreach (var (hour, line) in rows)
        {
            anchors.Add(new ObservedRowAnchor(Ts(hour), line));
        }

        return AbsenceAnchorResolver.Build(anchors);
    }

    [Fact]
    public void DuplicatedBracketingTimestamp_ResolvesToTheTightestBracket()
    {
        // 10:00 appears on lines 2, 3 and 4; 13:00 appears on lines 8, 9 and 10.
        // The absence between them is tightest against the highest preceding line
        // and the lowest following line.
        var resolver = Build(
            (10, 2), (10, 3), (10, 4),
            (13, 8), (13, 9), (13, 10));

        Assert.Equal(4, resolver.PrecedingLine(Ts(10)));
        Assert.Equal(8, resolver.FollowingLine(Ts(13)));
    }

    [Fact]
    public void SingleOccurrence_ResolvesToThatLineOnBothSides()
    {
        var resolver = Build((10, 5), (13, 6));

        Assert.Equal(5, resolver.PrecedingLine(Ts(10)));
        Assert.Equal(6, resolver.FollowingLine(Ts(13)));
    }

    [Fact]
    public void UnsortedSource_YieldsTemporalNeighboursEvenWhenLinesDescend()
    {
        // The file lists 13:00 physically before 10:00. The resolved lines are
        // the temporal neighbours' lines, so previous(50) > next(20): they are
        // neither consecutive nor ascending, and that is correct.
        var resolver = Build((13, 20), (10, 50));

        var previous = resolver.PrecedingLine(Ts(10));
        var next = resolver.FollowingLine(Ts(13));

        Assert.Equal(50, previous);
        Assert.Equal(20, next);
        Assert.True(previous > next, "An unsorted source may yield a descending pair.");
    }

    [Fact]
    public void UnsortedSourceWithDuplicates_StillAppliesTheTightestBracketRule()
    {
        // 10:00 on lines 90 and 40; 13:00 on lines 70 and 15. The rule is about
        // line ordering within one timestamp, not about file order.
        var resolver = Build((10, 90), (13, 70), (10, 40), (13, 15));

        Assert.Equal(90, resolver.PrecedingLine(Ts(10)));
        Assert.Equal(15, resolver.FollowingLine(Ts(13)));
    }

    [Fact]
    public void AbsentOrUnobservedTimestamp_ResolvesToNoLine()
    {
        var resolver = Build((10, 2), (13, 8));

        // A boundary gap has no observed timestamp on the unavailable side.
        Assert.Null(resolver.PrecedingLine(null));
        Assert.Null(resolver.FollowingLine(null));

        // A timestamp with no physical row cannot be given an invented line.
        Assert.Null(resolver.PrecedingLine(Ts(11)));
        Assert.Null(resolver.FollowingLine(Ts(11)));
    }

    [Fact]
    public void LinesAboveInt32MaxValueAreResolvedWithoutTruncation()
    {
        const long huge = (long)int.MaxValue + 10;
        var resolver = AbsenceAnchorResolver.Build(new[]
        {
            new ObservedRowAnchor(Ts(10), huge),
            new ObservedRowAnchor(Ts(10), huge + 5),
            new ObservedRowAnchor(Ts(13), huge + 9)
        });

        Assert.Equal(huge + 5, resolver.PrecedingLine(Ts(10)));
        Assert.Equal(huge + 9, resolver.FollowingLine(Ts(13)));
    }

    [Fact]
    public void Build_RejectsMissingRowsAndNonPositiveLines()
    {
        Assert.Throws<ArgumentNullException>(() => AbsenceAnchorResolver.Build(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ObservedRowAnchor(Ts(10), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ObservedRowAnchor(Ts(10), -4));
    }

    [Fact]
    public void Build_RejectsNonUtcAnchorTimestamp()
    {
        var local = new DateTimeOffset(2024, 8, 1, 10, 0, 0, TimeSpan.FromHours(2));

        Assert.Throws<ArgumentException>(() => new ObservedRowAnchor(local, 5));
    }

    [Fact]
    public void EmptySource_ResolvesToNoLine()
    {
        var resolver = AbsenceAnchorResolver.Build(Array.Empty<ObservedRowAnchor>());

        Assert.Null(resolver.PrecedingLine(Ts(10)));
        Assert.Null(resolver.FollowingLine(Ts(10)));
    }
}