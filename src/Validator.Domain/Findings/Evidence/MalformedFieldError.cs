using System;

namespace Validator.Domain.Findings.Evidence
{
    // Stable reason codes for one malformed field. Codes are invariant ASCII
    // and remain stable across validator versions.
    public enum MalformedReasonCode
    {
        INVALID_TIMESTAMP = 0,
        INVALID_DECIMAL = 1,
        INVALID_VALUE = 2,
        MISSING_COLUMN = 3
    }

    // One independently detectable field-level error of a malformed row.
    public sealed record MalformedFieldError
    {
        public string Field { get; }
        public string OriginalValue { get; }
        public MalformedReasonCode ReasonCode { get; }
        public string Reason { get; }

        public MalformedFieldError(string field, string originalValue, MalformedReasonCode reasonCode, string reason)
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                throw new ArgumentException("Field name must be a non-empty value.", nameof(field));
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("Reason must be a non-empty value.", nameof(reason));
            }

            Field = field;
            OriginalValue = originalValue ?? string.Empty;
            ReasonCode = reasonCode;
            Reason = reason;
        }
    }
}