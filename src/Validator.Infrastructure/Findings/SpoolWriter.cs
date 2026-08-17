using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Infrastructure.Sorting;

namespace Validator.Infrastructure.Findings
{
    // Append-only normalized one-record-per-line spool. The artifact is a
    // temporary file owned by this writer and deleted on dispose, whether the
    // run completed, failed, or was cancelled. Completion closes the artifact
    // and stamps a sidecar marker so readers accept only completed spools.
    public sealed class SpoolWriter : ISpoolWriter
    {
        private readonly object _syncRoot = new();
        private StreamWriter? _writer;
        private bool _completed;
        private bool _disposed;

        public string Path { get; }

        public string CompletionMarkerPath => Path + ".complete";

        public SpoolWriter(ITempStorage? tempStorage = null)
        {
            if (tempStorage is null)
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"validator-spool-{Guid.NewGuid():N}.txt");
                var directory = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
            else
            {
                Path = tempStorage.CreateTempFile("spool", ".txt");
            }

            _writer = new StreamWriter(new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true
            };
        }

        public ValueTask AppendLineAsync(string line, CancellationToken cancellationToken = default)
        {
            if (line is null)
            {
                throw new ArgumentNullException(nameof(line));
            }

            lock (_syncRoot)
            {
                if (_completed)
                {
                    throw new InvalidOperationException("A completed spool cannot accept further lines.");
                }

                if (_writer is null)
                {
                    throw new ObjectDisposedException(nameof(SpoolWriter));
                }

                cancellationToken.ThrowIfCancellationRequested();
                _writer.WriteLine(line);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(CancellationToken cancellationToken = default)
        {
            lock (_syncRoot)
            {
                if (_completed)
                {
                    return ValueTask.CompletedTask;
                }

                if (_writer is null)
                {
                    throw new ObjectDisposedException(nameof(SpoolWriter));
                }

                cancellationToken.ThrowIfCancellationRequested();
                _writer.Flush();
                _writer.Dispose();
                _writer = null;
                File.WriteAllText(CompletionMarkerPath, string.Empty);
                _completed = true;
            }

            return ValueTask.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _writer?.Dispose();
                _writer = null;
                _disposed = true;
            }

            DeleteIfExists(Path);
            DeleteIfExists(CompletionMarkerPath);

            await ValueTask.CompletedTask;
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // Best-effort cleanup; a later owner may complete it.
                }
            }
        }
    }
}