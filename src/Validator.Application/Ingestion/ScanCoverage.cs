using System;

namespace Validator.Application.Ingestion
{
    // Row-level scan coverage. The equality examined == accepted + malformed
    // is mandatory and validated at reconciliation; a failure to establish it
    // is fatal and no successful report may be rendered.
    public sealed record ScanCoverage
    {
        public long PhysicalRowsExamined { get; }
        public long AcceptedRows { get; }
        public long MalformedRows { get; }

        public bool IsReconciled => PhysicalRowsExamined == AcceptedRows + MalformedRows;

        public ScanCoverage(long physicalRowsExamined, long acceptedRows, long malformedRows)
        {
            if (physicalRowsExamined < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(physicalRowsExamined), "Examined rows must be non-negative.");
            }

            if (acceptedRows < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(acceptedRows), "Accepted rows must be non-negative.");
            }

            if (malformedRows < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(malformedRows), "Malformed rows must be non-negative.");
            }

            PhysicalRowsExamined = physicalRowsExamined;
            AcceptedRows = acceptedRows;
            MalformedRows = malformedRows;
        }
    }
}