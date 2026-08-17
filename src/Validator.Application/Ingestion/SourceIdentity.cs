using System;
using System.Linq;

namespace Validator.Application.Ingestion
{
    // Stable identification of the exact dataset bytes: a safe base name, the
    // byte length, and a SHA-256 fingerprint. Absolute paths are never exposed.
    public sealed record SourceIdentity
    {
        public string FileName { get; }
        public long ByteSize { get; }
        public string Sha256 { get; }

        public SourceIdentity(string fileName, long byteSize, string sha256)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("File name must be a non-empty value.", nameof(fileName));
            }

            if (fileName.IndexOfAny(['/', '\\', ':']) >= 0)
            {
                throw new ArgumentException("File name must be a safe base name without path components.", nameof(fileName));
            }

            if (byteSize < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteSize), "Byte size must be non-negative.");
            }

            if (sha256 is null || sha256.Length != 64 || sha256.Any(character => !IsLowerHex(character)))
            {
                throw new ArgumentException("SHA-256 must be exactly 64 lower-case hexadecimal characters.", nameof(sha256));
            }

            FileName = fileName;
            ByteSize = byteSize;
            Sha256 = sha256;
        }

        internal static bool IsLowerHex(char character) =>
            (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f');
    }
}