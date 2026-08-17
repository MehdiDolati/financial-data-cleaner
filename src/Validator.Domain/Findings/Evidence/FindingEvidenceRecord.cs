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
        public abstract string Kind { get; }

        public sealed record MissingCandle(
            FindingReference Finding,
            MissingCandleEvidence Evidence,
            long ChildOrder = 0) : FindingEvidenceRecord
        {
            public override string Kind => "MissingCandle";
        }

        public sealed record TimeGapHeader(
            FindingReference Finding,
            TimeGapEvidence Evidence,
            long ChildOrder = 0) : FindingEvidenceRecord
        {
            public override string Kind => "TimeGap";
        }

        public sealed record TimeGapMissingReference(
            FindingReference Finding,
            FindingReference TargetReference,
            long ChildOrder) : FindingEvidenceRecord
        {
            public override string Kind => "TimeGapMissingReference";
        }

        public sealed record DuplicateHeader(
            FindingReference Finding,
            DuplicateRecordEvidence Evidence,
            long ChildOrder = 0) : FindingEvidenceRecord
        {
            public override string Kind => "DuplicateRecord";
        }

        public sealed record DuplicateDifferingField(
            FindingReference Finding,
            string Field,
            long ChildOrder) : FindingEvidenceRecord
        {
            public override string Kind => "DuplicateDifferingField";
        }

        public sealed record DuplicateRow(
            FindingReference Finding,
            DuplicateRowEvidence Row,
            long ChildOrder) : FindingEvidenceRecord
        {
            public override string Kind => "DuplicateRow";
        }

        public sealed record InvalidOhlcValues(
            FindingReference Finding,
            OhlcValues Observed,
            long ChildOrder = 0) : FindingEvidenceRecord
        {
            public override string Kind => "InvalidOhlc";
        }

        public sealed record InvalidOhlcViolation(
            FindingReference Finding,
            OhlcViolationCode Code,
            long ChildOrder) : FindingEvidenceRecord
        {
            public override string Kind => "InvalidOhlcViolation";
        }

        public sealed record ClosedMarket(
            FindingReference Finding,
            ClosedMarketRecordEvidence Evidence,
            long ChildOrder = 0) : FindingEvidenceRecord
        {
            public override string Kind => "ClosedMarketRecord";
        }

        public sealed record MalformedHeader(
            FindingReference Finding,
            MalformedRowEvidence Evidence,
            long ChildOrder = 0) : FindingEvidenceRecord
        {
            public override string Kind => "MalformedRow";
        }

        public sealed record MalformedFieldErrorRecord(
            FindingReference Finding,
            MalformedFieldError Error,
            long ChildOrder) : FindingEvidenceRecord
        {
            public override string Kind => "MalformedFieldError";
        }

        public sealed record MalformedSkippedCheck(
            FindingReference Finding,
            CheckName Check,
            long ChildOrder) : FindingEvidenceRecord
        {
            public override string Kind => "MalformedSkippedCheck";
        }
    }
}