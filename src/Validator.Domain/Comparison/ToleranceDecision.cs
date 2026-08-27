namespace Validator.Domain.Comparison
{
    /// <summary>
    /// Classifies how a field difference was evaluated against its resolved tolerances.
    /// A difference is accepted when it falls within either the absolute or relative tolerance (OR logic).
    /// </summary>
    public abstract record ToleranceDecision
    {
        /// <summary>The difference was within the resolved absolute tolerance.</summary>
        public sealed record AcceptedByAbsolute : ToleranceDecision;

        /// <summary>The difference was within the resolved relative tolerance.</summary>
        public sealed record AcceptedByRelative : ToleranceDecision;

        /// <summary>The difference exceeds both the absolute and relative tolerances.</summary>
        public sealed record MaterialDifference : ToleranceDecision;
    }
}
