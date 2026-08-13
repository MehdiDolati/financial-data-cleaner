using System;

namespace Validator.Domain.Findings
{
    public sealed record ValidationFinding
    {
        public FindingCategory Category { get; init; }
        public int CountContribution { get; init; }
        public bool StableSequence { get; init; }
        public string Message { get; init; } = string.Empty;

        public ValidationFinding(FindingCategory category, int countContribution, bool stableSequence, string message)
        {
            Category = category;
            CountContribution = countContribution;
            StableSequence = stableSequence;
            Message = message ?? string.Empty;
        }
    }
}