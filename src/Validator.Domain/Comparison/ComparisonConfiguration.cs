using System;
using System.Collections.Generic;
using System.Linq;

namespace Validator.Domain.Comparison
{
    /// <summary>
    /// The explicitly resolved rules for a comparison run, built from user-supplied options and defaults.
    /// Configuration is validated and fully resolved before any dataset is read (FR-019).
    /// </summary>
    public sealed record ComparisonConfiguration
    {
        public string BenchmarkName { get; init; }
        public IReadOnlyList<ComparedField> Fields { get; init; }
        public TimestampMode TimestampMode { get; init; }

        public ComparisonConfiguration(
            string benchmarkName,
            IReadOnlyList<ComparedField> fields,
            TimestampMode timestampMode)
        {
            if (string.IsNullOrWhiteSpace(benchmarkName))
                throw new ArgumentException("Benchmark name must not be empty.", nameof(benchmarkName));
            if (fields is null || fields.Count == 0)
                throw new ArgumentException("At least one field must be configured.", nameof(fields));

            var fieldValues = fields.Select(f => f.Field).ToList();
            if (fieldValues.Distinct().Count() != fieldValues.Count)
                throw new ArgumentException("Duplicate fields are not allowed.", nameof(fields));

            BenchmarkName = benchmarkName;
            Fields = fields;
            TimestampMode = timestampMode;
        }
    }
}
