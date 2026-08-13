using System;
using System.Linq;

namespace Validator.Infrastructure.Csv
{
    public static class DelimiterDetector
    {
        private static readonly char[] Candidates = [',', ';', '\t'];

        public static char Detect(string sample)
        {
            if (string.IsNullOrWhiteSpace(sample))
            {
                throw new InvalidOperationException("Delimiter detection requires a sample row.");
            }

            var counts = Candidates
                .Select(candidate => new { Candidate = candidate, Count = CountOccurrences(sample, candidate) })
                .Where(item => item.Count > 0)
                .ToList();

            if (counts.Count == 0)
            {
                throw new InvalidOperationException("No supported delimiter was found in the sample row.");
            }

            var max = counts.Max(item => item.Count);
            var winners = counts.Where(item => item.Count == max).ToList();
            if (winners.Count != 1)
            {
                throw new InvalidOperationException("Delimiter detection is ambiguous; supply a delimiter explicitly.");
            }

            return winners[0].Candidate;
        }

        private static int CountOccurrences(string text, char candidate)
        {
            return text.Count(ch => ch == candidate);
        }
    }
}
