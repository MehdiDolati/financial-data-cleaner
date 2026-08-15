using System;

namespace Validator.Domain.Findings
{
    public sealed record ValidationFinding
    {
        public FindingCategory Category { get; init; }
        public int CountContribution { get; init; }
        public bool StableSequence { get; init; }
        public string Message { get; init; } = string.Empty;
        public DateTimeOffset? Timestamp { get; init; }
        public int? Line { get; init; }
        public IReadOnlyList<long> SourceLines { get; init; } = Array.Empty<long>();

        public ValidationFinding(FindingCategory category, int countContribution, bool stableSequence, string message)
        {
            Category = category;
            if (countContribution <= 0)
                throw new ArgumentOutOfRangeException(nameof(countContribution));

            CountContribution = countContribution;
            StableSequence = stableSequence;
            Message = !string.IsNullOrWhiteSpace(message)
                ? message
                : throw new ArgumentException("Finding message must not be empty.", nameof(message));
        }
    }
}