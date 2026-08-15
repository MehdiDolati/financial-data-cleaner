using System.Globalization;
using Validator.Domain.Candles;

namespace Validator.Infrastructure.Tests.Fixtures;

internal sealed class LargeFixtureGenerator : IDisposable
{
    private static readonly DateTimeOffset Start =
        new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private LargeFixtureGenerator(string directory, string path, int rowCount)
    {
        Directory = directory;
        Path = path;
        RowCount = rowCount;
    }

    public string Directory { get; }
    public string Path { get; }
    public int RowCount { get; }

    public static LargeFixtureGenerator Create(int rowCount)
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"validator-large-fixture-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "unsorted-m1.csv");

        using (var writer = new StreamWriter(path))
        {
            for (var index = rowCount - 1; index >= 0; index--)
            {
                var timestamp = Start.AddMinutes(index);
                writer.WriteLine(string.Join(
                    ',',
                    timestamp.ToString("O", CultureInfo.InvariantCulture),
                    "1.0",
                    "2.0",
                    "0.5",
                    "1.5",
                    index.ToString(CultureInfo.InvariantCulture)));
            }
        }

        return new LargeFixtureGenerator(directory, path, rowCount);
    }

    public IEnumerable<PriceCandle> ReadCandles()
    {
        foreach (var line in File.ReadLines(Path))
        {
            var fields = line.Split(',');
            yield return new PriceCandle(
                DateTimeOffset.ParseExact(
                    fields[0],
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None),
                decimal.Parse(fields[1], CultureInfo.InvariantCulture),
                decimal.Parse(fields[2], CultureInfo.InvariantCulture),
                decimal.Parse(fields[3], CultureInfo.InvariantCulture),
                decimal.Parse(fields[4], CultureInfo.InvariantCulture),
                decimal.Parse(fields[5], CultureInfo.InvariantCulture));
        }
    }

    public DateTimeOffset ExpectedTimestamp(int index) => Start.AddMinutes(index);

    public void Dispose()
    {
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}