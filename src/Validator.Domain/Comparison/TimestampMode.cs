namespace Validator.Domain.Comparison
{
    /// <summary>
    /// The strategy used to match timestamps between benchmark and candidate datasets.
    /// </summary>
    public enum TimestampMode
    {
        /// <summary>Match by exact timestamp equality (DateTimeOffset equality).</summary>
        Exact = 0
    }
}
