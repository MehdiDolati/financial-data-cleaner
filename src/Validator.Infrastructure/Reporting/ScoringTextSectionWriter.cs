using System;
using System.Globalization;
using System.Text;
using Validator.Application.Scoring;
using Validator.Domain.Findings;

namespace Validator.Infrastructure.Reporting
{
    // Renders the human-readable scoring section that follows the six summary
    // lines. Every metric appears in the established order with exactly one
    // state; each scored line states its count, population, population kind,
    // resolved weight, and normalised share so it can be checked by hand; and the
    // average states its coverage or its explicit unavailability with a reason.
    public static class ScoringTextSectionWriter
    {
        public const string Heading = "Quality scores (0-100, higher is better):";

        // Appends the scoring section to the buffer, each line terminated with
        // '\n'. Line endings match the detailed text writer so the two never
        // disagree within one report.
        public static void Append(StringBuilder buffer, DatasetScoreReport score)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentNullException.ThrowIfNull(score);

            buffer.Append(Heading).Append('\n');
            foreach (var metric in score.Metrics)
            {
                buffer.Append("- ").Append(SummaryLabels.LabelFor(metric.Category)).Append(": ");
                if (metric.State == MetricScoreState.Scored)
                {
                    AppendScoredLine(buffer, metric, score.Weighting.For(metric.Category));
                }
                else
                {
                    AppendUnscoredLine(buffer, metric);
                }

                buffer.Append('\n');
            }

            AppendAverageLine(buffer, score.Dataset);
            buffer.Append('\n');
        }

        private static void AppendScoredLine(StringBuilder buffer, MetricScore metric, MetricWeight weight)
        {
            buffer.Append(metric.Score!.Value.Format())
                .Append(" (count=").Append(Number(metric.Count))
                .Append("; population=").Append(Number(metric.Population!.Value))
                .Append(' ').Append(DescribePopulationKind(metric.PopulationKind))
                .Append("; weight=").Append(Weight(weight.Weight));

            if (weight.NormalisedShare is { } share)
            {
                buffer.Append("; share=").Append(TwoDecimals(share));
            }

            buffer.Append(')');
        }

        private static void AppendUnscoredLine(StringBuilder buffer, MetricScore metric)
        {
            var label = metric.State == MetricScoreState.NotApplicable ? "not applicable" : "not scored";
            buffer.Append(label).Append(" (reason: ").Append(metric.Reason).Append(')');
        }

        private static void AppendAverageLine(StringBuilder buffer, DatasetScore dataset)
        {
            buffer.Append("Dataset average: ");
            if (dataset.Average is null)
            {
                buffer.Append("not available (reason: ").Append(dataset.UnavailableReason).Append(')').Append('\n');
                return;
            }

            buffer.Append(dataset.Average.Value.Format())
                .Append(" (covers ").Append(Number(dataset.MetricsCovered)).Append(" of 6 metrics");

            if (dataset.ExcludedCategories.Count > 0)
            {
                buffer.Append("; excluded: ");
                for (var index = 0; index < dataset.ExcludedCategories.Count; index++)
                {
                    if (index > 0)
                    {
                        buffer.Append(", ");
                    }

                    buffer.Append(SummaryLabels.LabelFor(dataset.ExcludedCategories[index].Category));
                }
            }

            buffer.Append(')').Append('\n');
        }

        private static string DescribePopulationKind(MetricPopulationKind kind) => kind switch
        {
            MetricPopulationKind.ExpectedCandles => "expected candles",
            MetricPopulationKind.AcceptedRows => "accepted rows",
            MetricPopulationKind.ExaminedRows => "examined rows",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

        // A weight is shown in its shortest invariant form, so an integer weight
        // prints as '1' rather than '1.0' while a fractional weight keeps its
        // significant digits.
        private static string Weight(decimal value)
        {
            var text = value.ToString(CultureInfo.InvariantCulture);
            if (text.Contains('.', StringComparison.Ordinal))
            {
                text = text.TrimEnd('0').TrimEnd('.');
            }

            return text.Length == 0 ? "0" : text;
        }


        private static string TwoDecimals(decimal value) =>
            value.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
