using Validator.Application.Validation;

namespace Validator.Application.Tests.Validation;

public sealed class ExpectedSequenceGeneratorTests
{
    [Fact]
    public void Generate_IncludesBothBoundsAtTheRequestedInterval()
    {
        var start = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

        var timestamps = new ExpectedSequenceGenerator()
            .Generate(start, start.AddHours(2), TimeSpan.FromHours(1))
            .ToArray();

        Assert.Equal([start, start.AddHours(1), start.AddHours(2)], timestamps);
    }

    [Fact]
    public void Generate_ReturnsEmptySequenceWhenStartIsAfterEnd()
    {
        var start = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

        var timestamps = new ExpectedSequenceGenerator()
            .Generate(start, start.AddTicks(-1), TimeSpan.FromHours(1))
            .ToArray();

        Assert.Empty(timestamps);
    }
}