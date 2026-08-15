using Validator.Application.Ingestion;
using Validator.Application.Validation;

namespace Validator.Application.Tests.Options;

public sealed class OptionBoundaryTests
{
    [Fact]
    public void CsvOptions_ValidateAcceptsSupportedModesAndOffsetBounds()
    {
        new CsvInputOptions().Validate();
        new CsvInputOptions { Delimiter = "," }.Validate();
        new CsvInputOptions
        {
            TimestampFormat = "yyyy-MM-dd HH:mm:ss",
            TimestampColumn = "1",
            TzOffset = TimeSpan.FromHours(-14)
        }.Validate();
        new CsvInputOptions
        {
            HasHeader = true,
            TimestampFormat = "yyyy-MM-dd HH:mm:ss",
            TimestampColumn = "Timestamp",
            TzOffset = TimeSpan.FromHours(14)
        }.Validate();
        new CsvInputOptions
        {
            DateFormat = "yyyy-MM-dd",
            TimeFormat = "HH:mm"
        }.Validate();
    }

    [Fact]
    public void CsvOptions_ValidateRejectsEmptyDelimiter()
    {
        Assert.Throws<ArgumentException>(() => new CsvInputOptions { Delimiter = string.Empty }.Validate());
    }

    [Fact]
    public void CsvOptions_ValidateRejectsTimestampFormatWithoutColumn()
    {
        Assert.Throws<ArgumentException>(() => new CsvInputOptions
        {
            TimestampFormat = "O"
        }.Validate());
    }

    [Theory]
    [InlineData("Timestamp")]
    [InlineData("0")]
    public void CsvOptions_ValidateRejectsInvalidHeaderlessTimestampColumn(string column)
    {
        Assert.Throws<ArgumentException>(() => new CsvInputOptions
        {
            TimestampFormat = "O",
            TimestampColumn = column
        }.Validate());
    }

    [Fact]
    public void CsvOptions_ValidateRejectsTimeFormatWithoutDateFormat()
    {
        Assert.Throws<ArgumentException>(() => new CsvInputOptions { TimeFormat = "HH:mm" }.Validate());
    }

    [Fact]
    public void CsvOptions_ValidateRejectsCombinedAndSeparateTimestampFormats()
    {
        Assert.Throws<ArgumentException>(() => new CsvInputOptions
        {
            TimestampFormat = "O",
            TimestampColumn = "1",
            DateFormat = "yyyy-MM-dd",
            TimeFormat = "HH:mm"
        }.Validate());
    }

    [Theory]
    [InlineData(-15)]
    [InlineData(15)]
    public void CsvOptions_ValidateRejectsOffsetOutsideFourteenHours(int hours)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CsvInputOptions
        {
            TzOffset = TimeSpan.FromHours(hours)
        }.Validate());
    }

    [Fact]
    public void ValidationOptions_ParsesAbsentAndRejectsInvalidOverrides()
    {
        Assert.Null(new ValidationOptions().GetParsedTimeframe());
        Assert.Null(new ValidationOptions { TimeframeOverride = " " }.GetParsedTimeframe());
        Assert.Throws<FormatException>(() => new ValidationOptions { TimeframeOverride = "weekly" }
            .GetParsedTimeframe());
        Assert.True(new ValidationOptions { Verbose = true }.Verbose);
    }
}