namespace Validator.Domain.Comparison
{
    /// <summary>
    /// The five OHLCV fields that can be compared between benchmark and candidate datasets.
    /// </summary>
    public enum OhlcvField
    {
        Open = 0,
        High = 1,
        Low = 2,
        Close = 3,
        Volume = 4
    }
}
