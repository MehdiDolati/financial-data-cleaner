using System;
using Validator.Application.Ingestion;

namespace Validator.Application.Comparison
{
    /// <summary>
    /// Identity and validation context of a candidate dataset being compared against a benchmark.
    /// </summary>
    public sealed record CandidateIdentity
    {
        public string Instrument { get; init; }
        public SourceIdentity Source { get; init; }
        public ValidationContextSnapshot Context { get; init; }

        public CandidateIdentity(SourceIdentity source, ValidationContextSnapshot context, string instrument = "UNKNOWN")
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(context);
            if (string.IsNullOrWhiteSpace(instrument) || instrument.Contains('/') || instrument.Contains('\\'))
                throw new ArgumentException("Instrument must be a non-empty identity without path separators.", nameof(instrument));

            Instrument = instrument.Trim();
            Source = source;
            Context = context;
        }
    }
}
