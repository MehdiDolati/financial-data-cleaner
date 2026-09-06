using System;
using System.Security.Cryptography;
using System.Text;
using Validator.Application.Ingestion;

namespace Validator.Application.Web
{
    /// <summary>
    /// Deterministic identity of one run: exactly 64 lower-case hex
    /// characters (SC-004, Principle IV).
    /// </summary>
    /// <remarks>
    /// Normative derivation:
    /// WebRunId = SHA-256( SourceIdentity.Sha256 | 0x1F | CanonicalOptionsString ) - lower-case hex
    /// Wall-clock time, sequence numbers, randomness, user identity, upload
    /// name, and progress never contribute, which is what makes the
    /// duplicate-submission guard and the parity claim provable.
    /// </remarks>
    public sealed record WebRunId
    {
        public string Value { get; }

        private WebRunId(string value)
        {
            Value = value;
        }

        /// <summary>
        /// Derives the id from the source's content fingerprint and the
        /// resolved options that materially affect the result.
        /// </summary>
        public static WebRunId Derive(SourceIdentity source, WebRunOptions options, WebRunOperation operation)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(options);

            // The fingerprint text is hex of the source hash, so its bytes
            // cannot contain the 0x1F separator; the join is unambiguous.
            var canonical = options.ToCanonicalOptionsString(operation);
            var payload = Encoding.UTF8.GetBytes(source.Sha256 + '\u001F' + canonical);
            var hash = SHA256.HashData(payload);
            return new WebRunId(Convert.ToHexString(hash).ToLowerInvariant());
        }

        /// <summary>Creates an id from its 64 lower-case hex representation.</summary>
        public static WebRunId Parse(string value)
        {
            if (value is null)
            {
                throw new ArgumentException("A run id must be exactly 64 lower-case hexadecimal characters.");
            }

            if (value.Length != 64)
            {
                throw new ArgumentException(
                    $"A run id must be exactly 64 lower-case hexadecimal characters; got {value.Length}.");
            }

            foreach (var character in value)
            {
                var isLowerHex = (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f');
                if (!isLowerHex)
                {
                    throw new ArgumentException(
                        "A run id must be exactly 64 lower-case hexadecimal characters; found an invalid character.");
                }
            }

            return new WebRunId(value);
        }

        public override string ToString() => Value;
    }
}