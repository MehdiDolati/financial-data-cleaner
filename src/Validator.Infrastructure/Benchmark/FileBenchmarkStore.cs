using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Benchmark;

namespace Validator.Infrastructure.Benchmark
{
    /// <summary>
    /// File-based implementation of IBenchmarkStore.
    /// Stores benchmarks as directories containing benchmark.json and source.csv.
    /// </summary>
    public sealed class FileBenchmarkStore : IBenchmarkStore
    {
        private readonly string _benchmarksDirectory;

        public FileBenchmarkStore(string benchmarksDirectory)
        {
            if (string.IsNullOrWhiteSpace(benchmarksDirectory))
                throw new ArgumentException("Benchmarks directory must not be empty.", nameof(benchmarksDirectory));

            _benchmarksDirectory = benchmarksDirectory;
        }

        public async ValueTask SaveAsync(BenchmarkSnapshot snapshot, string sourceFilePath, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (string.IsNullOrWhiteSpace(sourceFilePath))
                throw new ArgumentException("Source file path must not be empty.", nameof(sourceFilePath));
            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException($"Source file not found: {sourceFilePath}", sourceFilePath);

            var benchmarkDir = GetBenchmarkDirectory(new BenchmarkName(snapshot.Name));

            // Check for name collision (FR-003)
            if (Directory.Exists(benchmarkDir))
                throw new InvalidOperationException(
                    $"Benchmark '{snapshot.Name}' already exists at {benchmarkDir}.");

            Directory.CreateDirectory(benchmarkDir);

            try
            {
                // Write benchmark.json
                var jsonPath = Path.Combine(benchmarkDir, "benchmark.json");
                await BenchmarkSnapshotJsonSerializer.WriteToFileAsync(jsonPath, snapshot, cancellationToken)
                    .ConfigureAwait(false);

                // Copy source.csv atomically
                var sourcePath = Path.Combine(benchmarkDir, "source.csv");
                var tempSource = sourcePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.Copy(sourceFilePath, tempSource, overwrite: false);
                    File.Move(tempSource, sourcePath, overwrite: false);
                }
                finally
                {
                    if (File.Exists(tempSource))
                        File.Delete(tempSource);
                }
            }
            catch
            {
                // Clean up partial artifacts on failure (FR-004)
                if (Directory.Exists(benchmarkDir))
                    Directory.Delete(benchmarkDir, recursive: true);
                throw;
            }
        }

        public async ValueTask<BenchmarkSnapshot> LoadAsync(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Benchmark name must not be empty.", nameof(name));

            var benchmarkName = new BenchmarkName(name);
            var benchmarkDir = GetBenchmarkDirectory(benchmarkName);
            var jsonPath = Path.Combine(benchmarkDir, "benchmark.json");

            if (!File.Exists(jsonPath))
                throw new FileNotFoundException($"Benchmark '{name}' not found.", jsonPath);

            var snapshot = await BenchmarkSnapshotJsonSerializer.ReadFromFileAsync(jsonPath, cancellationToken)
                .ConfigureAwait(false);

            // Verify source integrity (FR-005)
            var sourcePath = Path.Combine(benchmarkDir, "source.csv");
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"Benchmark source file missing: {sourcePath}", sourcePath);

            var actualHash = await ComputeSha256Async(sourcePath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualHash, snapshot.Source.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Benchmark source integrity check failed for '{name}': " +
                    $"expected SHA-256 {snapshot.Source.Sha256}, got {actualHash}.");

            return snapshot;
        }

        public ValueTask<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Benchmark name must not be empty.", nameof(name));

            var benchmarkName = new BenchmarkName(name);
            var benchmarkDir = GetBenchmarkDirectory(benchmarkName);

            if (!Directory.Exists(benchmarkDir))
                return new ValueTask<bool>(false);

            Directory.Delete(benchmarkDir, recursive: true);
            return new ValueTask<bool>(true);
        }

        public ValueTask<bool> ExistsAsync(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Benchmark name must not be empty.", nameof(name));

            var benchmarkName = new BenchmarkName(name);
            var benchmarkDir = GetBenchmarkDirectory(benchmarkName);
            var jsonPath = Path.Combine(benchmarkDir, "benchmark.json");

            return new ValueTask<bool>(File.Exists(jsonPath));
        }

        public ValueTask<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(_benchmarksDirectory))
                return new ValueTask<IReadOnlyList<string>>(Array.Empty<string>());

            var benchmarks = Directory.GetDirectories(_benchmarksDirectory)
                .Select(dir => Path.GetFileName(dir))
                .Where(name => name is not null)
                .Cast<string>()
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new ValueTask<IReadOnlyList<string>>(benchmarks);
        }

        private string GetBenchmarkDirectory(BenchmarkName name) =>
            Path.Combine(_benchmarksDirectory, name.Safe);

        private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
        {
            using var sha256 = SHA256.Create();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
