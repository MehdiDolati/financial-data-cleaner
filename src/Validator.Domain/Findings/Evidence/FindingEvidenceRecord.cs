namespace Validator.Domain.Findings.Evidence
{
    // Closed discriminated union of the normalized evidence records stored for
    // one finding. Header records carry the category evidence; child records
    // stream repeated rows, references, violations, field errors, and skipped
    // checks. Every record has its owning finding reference and a deterministic
    // child ordering key, and no record contains a collection that grows with
    // source size.
    public abstract record FindingEvidenceRecord
    {
        /// <summary>
        /// The published name of this record's shape, used to route it back to
        /// the right evidence when reading a spooled report.
        /// </summary>
        public abstract string Kind { get; }

        /// <summary>Evidence for a candle the timeframe expected but the source omits.</summary>
        public sealed record MissingCandle(
            FindingReference Finding,
            MissingCandleEvidence Evidence,
            long ChildOrder = 0) : FindingEvidenceRecord
        {
            public override string Kind => "MissingCandle";
        }

        /// <summary>The evidence describing one time gap.</summary>
        public sealed record TimeGapHeader(
            FindingReference Finding,
            TimeGapEvidence Evidence,
            long ChildOrder = 0) : FindingEvidenceRecord
        {
            public override string Kind => "TimeGap";
        }

        /// <summary>One missing candle that falls inside a time gap.</summary>
        public sealed record TimeGapMissingReference(
            FindingReference Finding,
            FindingReference TargetReference,
            long ChildOrder) : FindingEvidenceRecord
        {
            public override string Kind => "TimeGapMissingReference";
        }

        /// <summary>The evidence describing one group of duplicate records.</summary>
        public sealed record DuplicateHeader(
            FindingReference Finding,
            DuplicateRecordEvidence Evidence,
            long ChildOrder = 0) : FindingEvidenceRecord
        {
            public override string Kind => "DuplicateRecord";
        }

        /// <summary>One field whose value differs across a duplicate group.</summary>
        public sealed record DuplicateDifferingField(
            FindingReference Finding,
            string Field,
            long ChildOrder) : FindingEvidenceRecord
        {
            public override string Kind => "DuplicateDifferingField";
        }

        /// <summary>One of the repeated source rows in a duplicate group.</summary>
        public sealed record DuplicateRow(
            FindingReference Finding,
            DuplicateRowEvidence Row,
            long ChildOrder) : FindingEvidenceRecord
        {
            public override string Kind => "DuplicateRow";
        }

        /// <summary>The observed values of a record whose OHLC relationships fail.</summary>
        public sealed record InvalidOhlcValues(
            FindingReference Finding,
            OhlcValues Observed,
            long ChildOrder = 0) : FindingEvidenceRecord
        {
            public override string Kind => "InvalidOhlc";
        }

        /// <summary>One specific OHLC relationship the observed values violate.</summary>
        public sealed record InvalidOhlcViolation(
            FindingReference Finding,
            OhlcViolationCode Code,
            long ChildOrder) : FindingEvidenceRecord
        {
            public override string Kind => "InvalidOhlcViolation";
        }

        /// <summary>Evidence for a record timestamped when the market was closed.</summary>
        public sealed record ClosedMarket(
            FindingReference Finding,
            ClosedMarketRecordEvidence Evidence,
            long ChildOrder = 0) : FindingEvidenceRecord
        {
            public override string Kind => "ClosedMarketRecord";
        }

        /// <summary>The evidence describing one unparseable source row.</summary>
        public sealed record MalformedHeader(
            FindingReference Finding,
            MalformedRowEvidence Evidence,
            long ChildOrder = 0) : FindingEvidenceRecord
        {
            public override string Kind => "MalformedRow";
        }

        /// <summary>One field-level parse error within a malformed row.</summary>
        public sealed record MalformedFieldErrorRecord(
            FindingReference Finding,
            MalformedFieldError Error,
            long ChildOrder) : FindingEvidenceRecord
        {
            public override string Kind => "MalformedFieldError";
        }

        /// <summary>One check that could not run because the row was unparseable.</summary>
        public sealed record MalformedSkippedCheck(
            FindingReference Finding,
            CheckName Check,
            long ChildOrder) : FindingEvidenceRecord
        {
            public override string Kind => "MalformedSkippedCheck";
        }
    }
}