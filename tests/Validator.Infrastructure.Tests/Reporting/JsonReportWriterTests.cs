using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Reporting;
using Validator.Domain.Findings;
using Validator.Infrastructure.Reporting;

namespace Validator.Infrastructure.Tests.Reporting
{
    public class JsonReportWriterTests
    {
        [Fact]
        public async Task WriteReportAsync_EmitsSchemaCompliantPayload()
        {
            var report = new ValidationReport(
                new ValidationSummary(1, 0, 2)
                {
                    MissingCandles = 1,
                    DuplicateRecords = 0,
                    InvalidOhlc = 0,
                    ClosedMarketRecords = 0,
                    TimeGaps = 0
                },
                new DateRange(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.Zero)),
                "known-defects.csv")
            {
                DetectedTimeframe = "H1",
                TotalRecords = 3,
                Findings = new List<ValidationFinding>
                {
                    new ValidationFinding(FindingCategory.MissingCandle, 1, true, "Missing candle from 01:00Z")
                    {
                        Timestamp = new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero),
                        Line = 2
                    }
                }
            };

            var writer = new JsonReportWriter();
            await writer.WriteReportAsync(report);

            var json = writer.LastWrittenText;
            Assert.False(string.IsNullOrWhiteSpace(json));

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            Assert.Equal("known-defects.csv", root.GetProperty("sourceFile").GetString());
            Assert.Equal("H1", root.GetProperty("detectedTimeframe").GetString());
            Assert.Equal(3, root.GetProperty("totalRecords").GetInt32());
            Assert.True(root.GetProperty("isClean").GetBoolean() == false);
            Assert.Equal(1, root.GetProperty("summary").GetProperty("missingCandles").GetInt32());
            Assert.Equal("MissingCandle", root.GetProperty("findings")[0].GetProperty("category").GetString());
        }
    }
}
