using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Validator.Application.Benchmark;

namespace Validator.Infrastructure.Benchmark
{
    /// <summary>
    /// Serializes and deserializes BenchmarkSnapshot to/from JSON.
    /// Uses System.Text.Json with camelCase naming and decimal handling for deterministic output.
    /// </summary>
    public static class BenchmarkSnapshotJsonSerializer
    {
        private const int SupportedContractVersion = 1;

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Serializes a BenchmarkSnapshot to a JSON string.
        /// Always writes the contractVersion field.
        /// </summary>
        public static string Serialize(BenchmarkSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            return JsonSerializer.Serialize(snapshot, Options);
        }

        /// <summary>
        /// Deserializes a BenchmarkSnapshot from a JSON string.
        /// Validates the contractVersion is supported (FR-001).
        /// </summary>
        public static BenchmarkSnapshot Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("JSON must not be empty.", nameof(json));

            // First peek at contractVersion before full deserialization
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("contractVersion", out var versionProp))
            {
                var version = versionProp.GetInt32();
                if (version != SupportedContractVersion)
                    throw new InvalidDataException(
                        $"Incompatible benchmark contract version {version}. " +
                        $"This application supports version {SupportedContractVersion}. " +
                        $"Re-establish the benchmark with the current version.");
            }
            // Missing contractVersion is acceptable for backwards compatibility with v0 snapshots

            return JsonSerializer.Deserialize<BenchmarkSnapshot>(json, Options)
                ?? throw new InvalidDataException("Failed to deserialize benchmark snapshot: result was null.");
        }

        /// <summary>
        /// Writes a benchmark snapshot atomically to a file.
        /// </summary>
        public static async Task WriteToFileAsync(string filePath, BenchmarkSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var json = Serialize(snapshot);
                await File.WriteAllTextAsync(tempPath, json, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
                File.Move(tempPath, filePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        /// <summary>
        /// Reads a benchmark snapshot from a file.
        /// </summary>
        public static async Task<BenchmarkSnapshot> ReadFromFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Benchmark file not found: {filePath}", filePath);

            var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            return Deserialize(json);
        }
    }
}
