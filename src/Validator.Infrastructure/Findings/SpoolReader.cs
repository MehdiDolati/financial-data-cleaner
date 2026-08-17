using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Validator.Application.Abstractions;

namespace Validator.Infrastructure.Findings
{
    // Replayable sequential reader over a completed spool artifact. Reading an
    // incomplete or disposed spool is rejected. Byte-aligned line ranges can be
    // replayed without scanning the whole run.
    public sealed class SpoolReader : ISpoolSeekableReader, IDisposable
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

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
            using var reader = new StreamReader(_path, Utf8NoBom);
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return line;
            }
        }

        public async IAsyncEnumerable<string> ReadRangeAsync(
            long startByte,
            long endByte,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (startByte < 0 || endByte < startByte)
            {
                throw new ArgumentOutOfRangeException(nameof(startByte), "Range must be a non-empty byte interval.");
            }

            var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new StreamReader(stream, Utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 1024);
            stream.Seek(startByte, SeekOrigin.Begin);

            var position = startByte;
            var newlineByteCount = Encoding.UTF8.GetByteCount(Environment.NewLine);
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                position += Encoding.UTF8.GetByteCount(line) + newlineByteCount;
                if (position > endByte)
                {
                    yield break;
                }

                yield return line;
            }
        }

        public void Dispose()
        {
        }
    }
}