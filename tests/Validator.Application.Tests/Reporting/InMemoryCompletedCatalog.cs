using Validator.Application.Abstractions;
using Validator.Application.Reporting;
using Validator.Domain.Findings;

namespace Validator.Application.Tests.Reporting;

internal sealed class InMemoryCompletedCatalog : ICompletedFindingCatalog
{
    public FindingCatalogStatistics Statistics { get; }

    public InMemoryCompletedCatalog(FindingCatalogStatistics? statistics = null)
    {
        var zero = new CategoryStatistics(0, 0);
        Statistics = statistics ?? new FindingCatalogStatistics(zero, zero, zero, zero, zero, zero);
    }

    public IAsyncEnumerable<IDetailedFindingCursor> ReadCanonicalAsync(CancellationToken cancellationToken = default) =>
        Array.Empty<IDetailedFindingCursor>().ToAsyncEnumerable();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal static class AsyncEnumerableExtensions
{
    public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        return EnumerateAsync();
        async IAsyncEnumerable<T> EnumerateAsync()
        {
            foreach (var item in source)
            {
                await Task.Yield();
                yield return item;
            }
        }
    }
}