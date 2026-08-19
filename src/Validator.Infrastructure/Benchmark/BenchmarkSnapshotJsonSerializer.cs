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
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Serializes a BenchmarkSnapshot to a JSON string.
        /// </summary>
        public static string Serialize(BenchmarkSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            return JsonSerializer.Serialize(snapshot, Options);
        }

        /// <summary>
        /// Deserializes a BenchmarkSnapshot from a JSON string.
        /// </summary>
        public static BenchmarkSnapshot Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("JSON must not be empty.", nameof(json));

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
