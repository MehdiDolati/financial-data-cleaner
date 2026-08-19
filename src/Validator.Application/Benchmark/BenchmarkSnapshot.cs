using System;
using System.Collections.Generic;
using Validator.Application.Ingestion;
using Validator.Application.Reporting;
using Validator.Application.Scoring;

namespace Validator.Application.Benchmark
{
    /// <summary>
    /// An immutable reference snapshot of a validated dataset, persisted as a JSON file
    /// alongside the source bytes. Contains everything needed to reproduce the benchmark
    /// identity without re-reading the original file.
    /// </summary>
    public sealed record BenchmarkSnapshot
    {
        public string Name { get; init; }
        public DateTimeOffset EstablishedAtUtc { get; init; }
        public SourceIdentity Source { get; init; }
        public ValidationContextSnapshot Context { get; init; }
        public ScanCoverage Coverage { get; init; }
        public IReadOnlyList<CheckExecution> Checks { get; init; }
        public IReadOnlyList<MetricScore> Metrics { get; init; }
        public DatasetScore Dataset { get; init; }
        public ScoreWeighting Weighting { get; init; }

        public BenchmarkSnapshot(
            string name,
            DateTimeOffset establishedAtUtc,
            SourceIdentity source,
            ValidationContextSnapshot context,
            ScanCoverage coverage,
            IReadOnlyList<CheckExecution> checks,
            IReadOnlyList<MetricScore> metrics,
            DatasetScore dataset,
            ScoreWeighting weighting)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name must not be empty.", nameof(name));
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(coverage);
            ArgumentNullException.ThrowIfNull(checks);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(dataset);
            ArgumentNullException.ThrowIfNull(weighting);

            Name = name;
            EstablishedAtUtc = establishedAtUtc;
            Source = source;
            Context = context;
            Coverage = coverage;
            Checks = checks;
            Metrics = metrics;
            Dataset = dataset;
            Weighting = weighting;
        }
    }
}
