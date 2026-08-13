using System.Collections.Generic;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Domain.Findings;

namespace Validator.Application.Tests.Doubles
{
    public class InMemoryFindingSink : IFindingSink, IFindingReader
    {
        private readonly List<ValidationFinding> _store = new();
        public Task AppendAsync(ValidationFinding finding)
        {
            _store.Add(finding);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<ValidationFinding> ReadAllAsync()
        {
            foreach (var f in _store)
            {
                yield return f;
 await Task.Yield();
            }
        }
    }
}