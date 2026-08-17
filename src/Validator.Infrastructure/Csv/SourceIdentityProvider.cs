using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Application.Ingestion;

namespace Validator.Infrastructure.Csv
{
    // SHA-256 fingerprint over the exact source bytes of the same readable
    // handle used to prepare validation data. The safe base name is taken from
    // the supplied file name; absolute paths are never exposed.
    public sealed class SourceIdentityProvider : ISourceIdentityProvider
    {
        public async ValueTask<SourceIdentity> ComputeAsync(
            Stream sourceBytes,
            string fileName,
            CancellationToken cancellationToken = default)
        {
            if (sourceBytes is null)
            {
                throw new ArgumentNullException(nameof(sourceBytes));
            }

            var safeName = SafeBaseName(fileName);
            long byteSize;
            string sha256;

            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                int read;
                long total = 0;
                while ((read = await sourceBytes.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    hasher.AppendData(buffer, 0, read);
                    total += read;
                }

                byteSize = total;
                sha256 = Convert.ToHexStringLower(hasher.GetHashAndReset());
            }

            return new SourceIdentity(safeName, byteSize, sha256);
        }

        private static string SafeBaseName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("File name must be a non-empty value.", nameof(fileName));
            }

            var normalized = fileName.Replace('\\', '/');
            var lastSeparator = normalized.LastIndexOf('/');
            var baseName = lastSeparator >= 0 ? normalized.Substring(lastSeparator + 1) : normalized;
            if (string.IsNullOrWhiteSpace(baseName))
            {
                throw new ArgumentException("File name must contain a safe base name.", nameof(fileName));
            }

            return baseName;
        }
    }
}