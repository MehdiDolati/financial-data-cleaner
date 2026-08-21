using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Validator.Application.Benchmark
{
    /// <summary>
    /// Value object that derives a safe, filesystem-compatible directory name from user input.
    /// Rules: lowercase, spaces to hyphens, remove non-alphanumeric (except hyphens), no path separators.
    /// </summary>
    public readonly struct BenchmarkName : IEquatable<BenchmarkName>
    {
        public string Raw { get; }
        public string Safe { get; }

        public BenchmarkName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new ArgumentException("Benchmark name must not be empty.", nameof(raw));
            if (raw.Contains('/') || raw.Contains('\\') || raw.Contains(':'))
                throw new ArgumentException("Benchmark name must not contain path separators.", nameof(raw));

            Raw = raw.Trim();
            Safe = DeriveSafeName(Raw);
        }

        private static string DeriveSafeName(string input)
        {
            // Lowercase, replace spaces with hyphens, remove non-alphanumeric except hyphens
            var lowered = input.ToLowerInvariant();
            var withHyphens = lowered.Replace(' ', '-');
            var cleaned = Regex.Replace(withHyphens, @"[^a-z0-9\-]", "");
            // Collapse multiple hyphens
            var collapsed = Regex.Replace(cleaned, @"-{2,}", "-");
            // Trim leading/trailing hyphens
            var trimmed = collapsed.Trim('-');

            if (string.IsNullOrWhiteSpace(trimmed))
                throw new ArgumentException("Benchmark name produces an empty safe name after sanitization.", nameof(input));

            return trimmed;
        }

        public bool Equals(BenchmarkName other) => Safe == other.Safe;
        public override bool Equals(object? obj) => obj is BenchmarkName other && Equals(other);
        public override int GetHashCode() => Safe.GetHashCode();
        public override string ToString() => Safe;

        public static implicit operator string(BenchmarkName name) => name.Safe;
    }
}
