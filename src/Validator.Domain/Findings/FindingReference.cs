using System;
using System.Linq;

namespace Validator.Domain.Findings
{
    // Deterministic public identity of one detailed finding within a report.
    // Values are stable invariant ASCII strings that never contain random data.
    public sealed record FindingReference : IComparable<FindingReference>
    {
        public string Value { get; }

        public FindingReference(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Finding reference must be a non-empty value.", nameof(value));
            }

            if (value.Any(character => character >= 128))
            {
                throw new ArgumentException("Finding reference must be invariant ASCII.", nameof(value));
            }

            Value = value;
        }

        public int CompareTo(FindingReference? other) =>
            string.CompareOrdinal(Value, other?.Value);

        public override string ToString() => Value;
    }
}