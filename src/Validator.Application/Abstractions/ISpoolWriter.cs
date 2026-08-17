using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Validator.Application.Abstractions
{
    // Append-only temporary spool storing normalized one-record-per-line runs.
    // Spools are owned through Application ports and deleted on success, fatal
    // failure, or cancellation. The path and byte count let Application-backed
    // catalogs index blocks and reopen completed runs through reader factories.
    public interface ISpoolWriter : IAsyncDisposable
    {
        string Path { get; }

        long BytesWritten { get; }

        ValueTask AppendLineAsync(string line, CancellationToken cancellationToken = default);

        ValueTask CompleteAsync(CancellationToken cancellationToken = default);
    }

    // Replayable sequential reader over a completed spool run. Each enumeration
    // restarts from the first line.
    public interface ISpoolReader
    {
        IAsyncEnumerable<string> ReadLinesAsync(CancellationToken cancellationToken = default);
    }

    // Seekable reader over a completed spool run. Blocks are byte-aligned line
    // ranges recorded by the writer, so consumers replay one finding's child
    // lines without scanning the whole run.
    public interface ISpoolSeekableReader : ISpoolReader
    {
        IAsyncEnumerable<string> ReadRangeAsync(
            long startByte,
            long endByte,
            CancellationToken cancellationToken = default);
    }
}