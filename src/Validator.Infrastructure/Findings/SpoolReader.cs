using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Validator.Application.Abstractions;

namespace Validator.Infrastructure.Findings
{
    // Replayable sequential reader over a completed spool artifact. Reading an
    // incomplete or disposed spool is rejected.
    public sealed class SpoolReader : ISpoolReader, IDisposable
    {
        private readonly string _path;

        public SpoolReader(string path, string completionMarkerPath)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Spool path must be a non-empty value.", nameof(path));
            }

            if (string.IsNullOrWhiteSpace(completionMarkerPath))
            {
                throw new ArgumentException("Completion marker path must be a non-empty value.", nameof(completionMarkerPath));
            }

            if (!File.Exists(path) || !File.Exists(completionMarkerPath))
            {
                throw new InvalidOperationException("Only completed spool artifacts can be read.");
            }

            _path = path;
        }

        public async IAsyncEnumerable<string> ReadLinesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var reader = new StreamReader(_path);
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return line;
            }
        }

        public void Dispose()
        {
        }
    }
}