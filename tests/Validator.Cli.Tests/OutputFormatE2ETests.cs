using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Validator.Cli.Commands;

namespace Validator.Cli.Tests
{
    public class OutputFormatE2ETests
    {
        [Fact]
        public async Task RunAsync_WithJsonOutput_WritesSchemaDocumentToFile()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"validator-cli-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            var inputPath = Path.Combine(directory, "input.csv");
            var outputPath = Path.Combine(directory, "report.json");

            File.WriteAllText(inputPath, "timestamp,open,high,low,close,volume\n2026-01-01T00:00:00Z,1.1,1.2,1.0,1.1,100\n2026-01-01T01:00:00Z,1.1,1.2,1.0,1.1,100\n");

            var exitCode = await ValidateCommand.RunAsync(new[] { inputPath, "--format", "json", "--output", outputPath, "--verbose" });

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputPath));

            using var document = JsonDocument.Parse(File.ReadAllText(outputPath));
            var root = document.RootElement;
            Assert.Equal("H1", root.GetProperty("detectedTimeframe").GetString());
            Assert.Equal("input.csv", root.GetProperty("sourceFile").GetString());
        }
    }
}
