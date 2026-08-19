using System;
using System.Globalization;

namespace Validator.Application.Scoring
{
    // Raised when a metric's count exceeds its population, which implies a defect
    // rate above 1. That is an internal inconsistency, not a value to clamp: the
    // run must fail as REPORT_RECONCILIATION_FAILED. The offending count and
    // population are carried so the diagnostic can name the exact disagreement.
    public sealed class ImpossibleDefectRateException : Exception
    {
        public long Count { get; }

        public long Population { get; }

        public ImpossibleDefectRateException(long count, long population)
            : base(string.Format(
                CultureInfo.InvariantCulture,
                "A defect count of {0} exceeds its population of {1}, implying a rate above 1. This is an internal inconsistency, not a clampable value.",
                count,
                population))
        {
            Count = count;
            Population = population;
        }
    }
}
