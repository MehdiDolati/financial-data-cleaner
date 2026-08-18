using System.Threading;
using System.Threading.Tasks;

namespace Validator.Application.Abstractions
{
    // Canonically orders a completed spool run's lines by their owning finding
    // reference (the prefix before the first '|'), preserving append order
    // within one reference. Implementations use bounded external merge runs.
    public interface ISpoolCanonicalSorter
    {
        Task<ISpoolReplayableRun> PrepareAsync(
            ISpoolReader source,
            CancellationToken cancellationToken = default);
    }

    // Replayable sorted run. The underlying temporary artifacts are deleted on
    // dispose, whether the consumer replayed the whole run or stopped early.
    public interface ISpoolReplayableRun : IAsyncDisposable
    {
        IAsyncEnumerable<string> ReplayAsync(CancellationToken cancellationToken = default);
    }
}