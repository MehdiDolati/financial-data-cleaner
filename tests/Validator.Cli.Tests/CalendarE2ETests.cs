using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Validator.Cli.Commands;

namespace Validator.Cli.Tests
{
    public class CalendarE2ETests
    {
        [Fact]
        public async Task MarketFlag_Equities_MarksSunday23AsClosed()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"validator-cli-cal-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);

            var input = Path.Combine(dir, "input.csv");
            var output = Path.Combine(dir, "report.json");

            // Legacy date+time columns (no header) - Sunday 2026-02-08 23:00 UTC
            File.WriteAllText(input, "2026-02-08,23:00,1,1,1,1,100\n");

            var exit = await ValidateCommand.RunAsync(new[] { input, "--format", "json", "--output", output, "--market", "equities", "--date-format", "yyyy-MM-dd", "--time-format", "HH:mm" });
            Assert.True(exit == 0 || exit == 1);
            Assert.True(File.Exists(output));

            using var doc = JsonDocument.Parse(File.ReadAllText(output));
            var root = doc.RootElement;
            var closed = root.GetProperty("summary").GetProperty("closedMarketRecords").GetInt32();
            Assert.Equal(1, closed);
        }

        [Fact]
        public async Task MarketFlag_Forex_DoesNotMarkSunday23AsClosed()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"validator-cli-cal-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);

            var input = Path.Combine(dir, "input.csv");
            var output = Path.Combine(dir, "report.json");

            // Legacy date+time columns (no header) - Sunday 2026-02-08 23:00 UTC
            File.WriteAllText(input, "2026-02-08,23:00,1,1,1,1,100\n");

            var exit = await ValidateCommand.RunAsync(new[] { input, "--format", "json", "--output", output, "--market", "forex", "--date-format", "yyyy-MM-dd", "--time-format", "HH:mm" });
            Assert.True(exit == 0 || exit == 1);
            Assert.True(File.Exists(output));

            var content = File.ReadAllText(output);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var closed = root.GetProperty("summary").GetProperty("closedMarketRecords").GetInt32();
            Assert.True(closed == 0, content);
        }

        [Fact]
        public async Task CalendarFlag_CustomWeeklyCalendar_PathWorksLikeEquities()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"validator-cli-cal-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);

            var input = Path.Combine(dir, "input.csv");
            var output = Path.Combine(dir, "report.json");
            var cal = Path.Combine(dir, "weekly.json");

            File.WriteAllText(input, "2026-02-08,23:00,1,1,1,1,100\n");
            File.WriteAllText(cal, "{\n  \"type\": \"weekly\",\n  \"openDays\": [\"Monday\",\"Tuesday\",\"Wednesday\",\"Thursday\",\"Friday\"],\n  \"openHour\": 9,\n  \"closeHour\": 17,\n  \"timezone\": \"UTC\"\n}\n");

            var exit = await ValidateCommand.RunAsync(new[] { input, "--format", "json", "--output", output, "--calendar", cal, "--date-format", "yyyy-MM-dd", "--time-format", "HH:mm" });
            Assert.True(exit == 0 || exit == 1);
            Assert.True(File.Exists(output));

            using var doc = JsonDocument.Parse(File.ReadAllText(output));
            var root = doc.RootElement;
            var closed = root.GetProperty("summary").GetProperty("closedMarketRecords").GetInt32();
            Assert.Equal(1, closed);
        }
    }
}
