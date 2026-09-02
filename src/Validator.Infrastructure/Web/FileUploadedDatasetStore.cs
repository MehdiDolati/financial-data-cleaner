using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Ingestion;
using Validator.Infrastructure.Csv;

namespace Validator.Infrastructure.Web
{
    /// <summary>
    /// Content-addressed, write-once IUploadedDatasetStore: bytes are stored
    /// under their SHA-256 in a configurable root and never rewritten;
    /// OpenAsync replays the exact stored bytes through the existing
    /// PreparedCsvCandleSource so validation reads what was hashed (SC-008,
    /// FR-006, research R4 interim default).
    /// </summary>
    public sealed class FileUploadedDatasetStore : IUploadedDatasetStore
    {
        private readonly string _root;

        public FileUploadedDatasetStore(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new ArgumentException("The upload store root must not be empty.", nameof(root));
            }

            _root = root;
        }

        public async ValueTask<UploadedDataset> StoreAsync(
            string safeFileName,
            Stream content,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(safeFileName))
            {
                throw new ArgumentException(
                    "The upload's safe file name must be a non-empty value.", nameof(safeFileName));
            }

            if (safeFileName.IndexOfAny(['/', '\\', ':']) >= 0)
            {
                throw new ArgumentException(
                    "The upload's safe file name must be a base name without path components.", nameof(safeFileName));
            }

            ArgumentNullException.ThrowIfNull(content);

            var uploads = Path.Combine(_root, "uploads");
            Directory.CreateDirectory(uploads);

            // One pass: hash and persist to the temporary file, then move into
            // place content-addressed. Write-once: an existing artifact for the
            // same hash is never rewritten or deleted.
            var temporary = Path.Combine(uploads, Guid.NewGuid().ToString("N") + ".tmp");
            string sha256;
            long byteSize;
            try
            {
                using (var sha = SHA256.Create())
                using (var writer = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var hashed = await sha.ComputeHashAsync(new SplittingStream(content, writer), ct)
                        .ConfigureAwait(false);
                    sha256 = Convert.ToHexString(hashed).ToLowerInvariant();
                    byteSize = writer.Length;
                }

                var reference = sha256 + ".csv";
                var destination = Path.Combine(uploads, reference);
                if (!File.Exists(destination))
                {
                    File.Move(temporary, destination, overwrite: false);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }

            var identity = new SourceIdentity(safeFileName, byteSize, sha256);
            return new UploadedDataset(identity, sha256 + ".csv");
        }

        public ValueTask<IPreparedCandleSource> OpenAsync(
            UploadedDataset dataset,
            CsvInputOptions options,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(dataset);

            var path = Path.Combine(_root, "uploads", dataset.ContentReference);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"The stored upload '{dataset.ContentReference}' is not present in the upload store.", path);
            }

            // The existing prepared source replays the exact stored bytes and
            // establishes identity from the same handle in one pass.
            var source = new PreparedCsvCandleSource(path, options);
            return ValueTask.FromResult<IPreparedCandleSource>(source);
        }

        /// <summary>
        /// A read-through tee: every byte read from the upload is hashed and
        /// written to the durable artifact, so the stored bytes are exactly
        /// the bytes that produced the identity.
        /// </summary>
        private sealed class SplittingStream : Stream
        {
            private readonly Stream _source;
            private readonly Stream _destination;

            public SplittingStream(Stream source, Stream destination)
            {
                _source = source;
                _destination = destination;
            }

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override async ValueTask<int> ReadAsync(
                Memory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                var read = await _source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read > 0)
                {
                    await _destination.WriteAsync(buffer[..read], cancellationToken).ConfigureAwait(false);
                }

                return read;
            }

            public override int Read(byte[] buffer, int offset, int count) =>
                ReadAsync(new Memory<byte>(buffer, offset, count)).AsTask().GetAwaiter().GetResult();

            public override void Flush() => _destination.Flush();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}