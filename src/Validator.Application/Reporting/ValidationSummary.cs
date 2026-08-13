namespace Validator.Application.Reporting
{
    public sealed record ValidationSummary
    {
        public int TotalFindings { get; init; }
        public int MalformedRows { get; init; }
        public int ValidRows { get; init; }
        public int MissingCandles { get; init; }
        public int DuplicateRecords { get; init; }
        public int InvalidOhlc { get; init; }
        public int ClosedMarketRecords { get; init; }
        public int TimeGaps { get; init; }

        public bool IsClean => TotalFindings == 0 && MalformedRows == 0 && MissingCandles == 0 && DuplicateRecords == 0 && InvalidOhlc == 0 && ClosedMarketRecords == 0 && TimeGaps == 0;

        public ValidationSummary(int TotalFindings, int MalformedRows, int ValidRows)
            : this(TotalFindings, MalformedRows, ValidRows, TotalFindings, 0, 0, 0, 0)
        {
        }

        public ValidationSummary(int TotalFindings, int MalformedRows, int ValidRows, int MissingCandles, int DuplicateRecords, int InvalidOhlc, int ClosedMarketRecords, int TimeGaps)
        {
            this.TotalFindings = TotalFindings;
            this.MalformedRows = MalformedRows;
            this.ValidRows = ValidRows;
            this.MissingCandles = MissingCandles;
            this.DuplicateRecords = DuplicateRecords;
            this.InvalidOhlc = InvalidOhlc;
            this.ClosedMarketRecords = ClosedMarketRecords;
            this.TimeGaps = TimeGaps;
        }
    }
}