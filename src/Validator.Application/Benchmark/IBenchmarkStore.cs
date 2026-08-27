using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Validator.Application.Benchmark
{
    /// <summary>
    /// Interface for benchmark snapshot persistence. Implementations handle storage details
    /// (file-based, database, etc.) while the Application layer owns the business logic.
    /// </summary>
    public interface IBenchmarkStore
    {
        /// <summary>
        /// Saves a benchmark snapshot and copies the source file into the benchmark directory.
        /// Fails if a benchmark with the same name already exists.
        /// </summary>
        ValueTask SaveAsync(BenchmarkSnapshot snapshot, string sourceFilePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Loads a benchmark snapshot by name. Throws if not found.
        /// </summary>
        ValueTask<BenchmarkSnapshot> LoadAsync(string name, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a benchmark by name. Returns true if deleted, false if not found.
        /// </summary>
        ValueTask<bool> DeleteAsync(string name, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a benchmark with the given name exists.
        /// </summary>
        ValueTask<bool> ExistsAsync(string name, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists all benchmark names.
        /// </summary>
        ValueTask<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);
    }
}
