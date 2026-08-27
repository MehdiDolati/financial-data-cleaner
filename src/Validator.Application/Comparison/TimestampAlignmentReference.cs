using System;

namespace Validator.Application.Comparison;

/// <summary>
/// An aligned timestamp that could not be matched, retaining the source row
/// reference available for the side that supplied the record.
/// </summary>
public sealed record TimestampAlignmentReference(
    DateTimeOffset TimestampUtc,
    long? BenchmarkSourceLine = null,
    long? CandidateSourceLine = null);