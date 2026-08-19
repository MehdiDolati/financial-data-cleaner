using System;
using Validator.Application.Ingestion;

namespace Validator.Application.Comparison
{
    /// <summary>
    /// Identity and validation context of a candidate dataset being compared against a benchmark.
    /// </summary>
    public sealed record CandidateIdentity
    {
        public SourceIdentity Source { get; init; }
        public ValidationContextSnapshot Context { get; init; }

        public CandidateIdentity(SourceIdentity source, ValidationContextSnapshot context)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(context);

            Source = source;
            Context = context;
        }
    }
}
