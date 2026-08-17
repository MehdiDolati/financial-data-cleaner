using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Validator.Application.Abstractions
{
    // Append-only temporary spool storing normalized one-record-per-line runs.
    // Spools are owned through Application ports and deleted on success, fatal
    // failure, or cancellation.
    public interface ISpoolWriter : IAsyncDisposable
    {
        ValueTask AppendLineAsync(string line, CancellationToken cancellationToken = default);

        ValueTask CompleteAsync(CancellationToken cancellationToken = default);
    }

    // Replayable sequential reader over a completed spool run.
    public interface ISpoolReader
    {
        IAsyncEnumerable<string> ReadLinesAsync(CancellationToken cancellationToken = default);
    }
}