using System.Text.Json;

namespace Validator.Cli.Tests;

public sealed class DeterminismTests
{
    [Fact]
    public async Task ReorderedInputs_ProduceEqualCountsAndCanonicalFindingOrder()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"validator-determinism-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var firstPath = Path.Combine(directory, "first.csv");
        var secondPath = Path.Combine(directory, "second.csv");
        var rows = new[]
        {
            "2026.01.01,00:00,1,2,0.5,1.5,10",
            "2026.01.01,00:00,1,2,0.5,1.4,10",
            "2026.01.01,02:00,1,2,0.5,1.5,10",
            "2026.01.01,04:00,1,0.4,0.5,1.5,10"
        };
        File.WriteAllLines(firstPath, rows);
        File.WriteAllLines(secondPath, rows.Reverse());

        var first = await RunJsonAsync(firstPath);
        var second = await RunJsonAsync(secondPath);

        Assert.Equal(first.Summary, second.Summary);
        Assert.Equal(first.Keys, first.Keys.OrderBy(key => CategoryOrder[key.Category])
            .ThenBy(key => key.Timestamp, StringComparer.Ordinal)
            .ThenBy(key => key.Line));
        Assert.Equal(second.Keys, second.Keys.OrderBy(key => CategoryOrder[key.Category])
            .ThenBy(key => key.Timestamp, StringComparer.Ordinal)
            .ThenBy(key => key.Line));
    }

    private static async Task<ReportProjection> RunJsonAsync(string path)
    {
        var result = await CoreValidationE2ETests.InvokeAsync(
            [path, "--timeframe", "H1", "--format", "json", "--tz-offset", "+00:00"]);
        Assert.Equal(1, result.ExitCode);

        using var document = JsonDocument.Parse(result.StdOut);
        var summary = document.RootElement.GetProperty("summary");
        var counts = summary.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetInt32());
        var keys = document.RootElement.GetProperty("findings")
            .EnumerateArray()
            .Select(finding => new FindingKey(
                finding.GetProperty("category").GetString()!,
                finding.GetProperty("timestamp").ValueKind == JsonValueKind.Null
                    ? string.Empty
                    : finding.GetProperty("timestamp").GetString()!,
                finding.GetProperty("line").ValueKind == JsonValueKind.Null
                    ? int.MaxValue
                    : finding.GetProperty("line").GetInt32()))
            .ToArray();
        return new ReportProjection(counts, keys);
    }

    private sealed record FindingKey(string Category, string Timestamp, int Line);

    private static readonly IReadOnlyDictionary<string, int> CategoryOrder =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["MissingCandle"] = 0,
            ["DuplicateRecord"] = 1,
            ["InvalidOhlc"] = 2,
            ["ClosedMarketRecord"] = 3,
            ["TimeGap"] = 4,
            ["MalformedRow"] = 5
        };

    private sealed record ReportProjection(
        IReadOnlyDictionary<string, int> Summary,
        IReadOnlyList<FindingKey> Keys);
}