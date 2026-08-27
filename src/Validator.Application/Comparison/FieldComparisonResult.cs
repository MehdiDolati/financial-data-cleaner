using System;
using Validator.Domain.Comparison;

namespace Validator.Application.Comparison
{
    /// <summary>
    /// Represents the result of comparing a single field at a single timestamp.
    /// </summary>
    public sealed record FieldComparisonResult
    {
        public DateTimeOffset TimestampUtc { get; init; }
        public OhlcvField Field { get; init; }
        public decimal BenchmarkValue { get; init; }
        public decimal CandidateValue { get; init; }
        public required ToleranceDecision Decision { get; init; }
    }
}
