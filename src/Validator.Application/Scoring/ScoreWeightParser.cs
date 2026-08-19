using System;
using System.Collections.Generic;
using System.Globalization;
using Validator.Domain.Findings;

namespace Validator.Application.Scoring
{
    // Raised when a --score-weights value cannot be turned into a complete, valid
    // six-metric weighting. The message states both the specific problem and the
    // accepted form, so a caller can correct it without guessing. It derives from
    // ArgumentException so a front end's existing argument-error handling maps it
    // to INVALID_ARGUMENT without a new catch clause.
    public sealed class ScoreWeightFormatException : ArgumentException
    {
        public ScoreWeightFormatException(string problem)
            : base(problem + " " + ScoreWeightParser.AcceptedForm)
        {
        }
    }


    // Parses the caller's weight override into a resolved CallerSupplied
    // weighting. All six metrics must be present exactly once, weights are
    // non-negative invariant decimals, and an all-zero set is rejected. Parsing
    // is a pure function of the input and runs before any dataset work begins.
    public static class ScoreWeightParser
    {
        public const string AcceptedForm =
            "Supply all six metrics as a comma-separated list of name=weight pairs " +
            "(missingCandles, duplicateRecords, invalidOhlc, closedMarketRecords, timeGaps, malformedRows), " +
            "each weight a non-negative decimal such as '2' or '1.5', with at least one weight above zero.";

        private static readonly IReadOnlyDictionary<string, FindingCategory> NamesToCategories =
            new Dictionary<string, FindingCategory>(StringComparer.Ordinal)
            {
                ["missingCandles"] = FindingCategory.MissingCandle,
                ["duplicateRecords"] = FindingCategory.DuplicateRecord,
                ["invalidOhlc"] = FindingCategory.InvalidOhlc,
                ["closedMarketRecords"] = FindingCategory.ClosedMarketRecord,
                ["timeGaps"] = FindingCategory.TimeGap,
                ["malformedRows"] = FindingCategory.MalformedRow
            };

        public static ScoreWeighting Parse(string value)
        {
            if (value is null || string.IsNullOrWhiteSpace(value))
            {
                throw new ScoreWeightFormatException("No weights were supplied.");
            }

            var pairs = value.Split(',');
            var byCategory = new Dictionary<FindingCategory, decimal>();

            foreach (var rawPair in pairs)
            {
                var pair = rawPair.Trim();
                if (pair.Length == 0)
                {
                    throw new ScoreWeightFormatException("An empty weight entry was supplied.");
                }

                var separator = pair.IndexOf('=');
                if (separator <= 0 || separator != pair.LastIndexOf('='))
                {
                    throw new ScoreWeightFormatException($"The weight entry '{pair}' is not a single name=value pair.");
                }

                var name = pair[..separator].Trim();
                var rawWeight = pair[(separator + 1)..].Trim();

                if (!NamesToCategories.TryGetValue(name, out var category))
                {
                    throw new ScoreWeightFormatException($"'{name}' is not a known metric name.");
                }

                if (byCategory.ContainsKey(category))
                {
                    throw new ScoreWeightFormatException($"The metric '{name}' was supplied more than once.");
                }

                if (!TryParseWeight(rawWeight, out var weight))
                {
                    throw new ScoreWeightFormatException($"'{rawWeight}' is not a valid non-negative decimal weight for '{name}'.");
                }

                byCategory[category] = weight;
            }

            if (byCategory.Count != NamesToCategories.Count)
            {
                throw new ScoreWeightFormatException("Not every metric was supplied.");
            }

            var total = 0m;
            var weights = new List<MetricWeight>(6);
            foreach (var categoryInOrder in MetricPopulationMap.CanonicalOrder)
            {
                var weight = byCategory[categoryInOrder];
                total += weight;
                weights.Add(new MetricWeight(categoryInOrder, weight));
            }

            if (total <= 0m)
            {
                throw new ScoreWeightFormatException("Every supplied weight was zero.");
            }

            return new ScoreWeighting(ScoreWeightingSource.CallerSupplied, weights);
        }

        // Accepts only a plain non-negative decimal in invariant form: no leading
        // '+', no exponent, and no thousands separators, so the accepted form
        // stays narrow and its diagnostic precise.
        private static bool TryParseWeight(string text, out decimal weight)
        {
            weight = 0m;
            if (text.Length == 0 || text[0] == '+' || text[0] == '-')
            {
                return false;
            }

            const NumberStyles style = NumberStyles.AllowDecimalPoint;
            if (!decimal.TryParse(text, style, CultureInfo.InvariantCulture, out var parsed) || parsed < 0m)
            {
                return false;
            }

            weight = parsed;
            return true;
        }
    }
}
