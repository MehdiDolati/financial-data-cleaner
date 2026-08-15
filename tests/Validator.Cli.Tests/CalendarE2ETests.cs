using System.Text.Json;
using Validator.Cli.Commands;

namespace Validator.Cli.Tests;

public sealed class CalendarE2ETests
{
    [Fact]
    public async Task CustomCalendar_UsesHalfOpenSessionBoundaries()
    {
        using var fixture = CalendarFixture.Create(validCalendar: true);

        var exitCode = await ValidateCommand.RunAsync(
        [
            fixture.InputPath,
            "--market", "custom",
            "--calendar", fixture.CalendarPath,
            "--timeframe", "H1",
            "--tz-offset", "+00:00",
            "--format", "json",
            "--output", fixture.OutputPath
        ]);

        Assert.Equal(1, exitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(fixture.OutputPath));
        Assert.Equal(1, report.RootElement.GetProperty("summary").GetProperty("closedMarketRecords").GetInt32());
    }

    [Fact]
    public async Task CustomMarket_WithoutCalendar_FailsBeforeCsvParsing()
    {
        var missingInput = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.csv");

        var exitCode = await ValidateCommand.RunAsync([missingInput, "--market", "custom"]);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task InvalidCalendar_FailsBeforeCsvParsing()
    {
        using var fixture = CalendarFixture.Create(validCalendar: false, createInput: false);

        var exitCode = await ValidateCommand.RunAsync(
            [fixture.InputPath, "--market", "custom", "--calendar", fixture.CalendarPath]);

        Assert.Equal(2, exitCode);
        Assert.False(File.Exists(fixture.OutputPath));
    }

    private sealed class CalendarFixture : IDisposable
    {
        private CalendarFixture(string directory, string inputPath, string calendarPath, string outputPath)
        {
            Directory = directory;
            InputPath = inputPath;
            CalendarPath = calendarPath;
            OutputPath = outputPath;
        }

        public string Directory { get; }
        public string InputPath { get; }
        public string CalendarPath { get; }
        public string OutputPath { get; }

        public static CalendarFixture Create(bool validCalendar, bool createInput = true)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"validator-cli-calendar-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            var inputPath = Path.Combine(directory, "custom-session.csv");
            var calendarPath = Path.Combine(directory, "custom-market.json");
            var outputPath = Path.Combine(directory, "report.json");

            if (createInput)
            {
                File.WriteAllText(
                    inputPath,
                    "2026.02.04,09:00,1,2,0.5,1.5,10\n" +
                    "2026.02.04,10:00,1,2,0.5,1.5,10\n" +
                    "2026.02.04,17:00,1,2,0.5,1.5,10\n");
            }

            File.WriteAllText(calendarPath, validCalendar
                ? """
                  {
                    "version": 1,
                    "name": "Custom UTC Session",
                    "timeZone": "UTC",
                    "sessions": [
                      { "openDay": "Wednesday", "openTime": "09:00", "closeDay": "Wednesday", "closeTime": "17:00" }
                    ]
                  }
                  """
                : "{\"version\":2}");

            return new CalendarFixture(directory, inputPath, calendarPath, outputPath);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}
