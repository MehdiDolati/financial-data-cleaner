using System.Collections.Generic;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Domain.Candles;

namespace Validator.Application.Tests.Doubles
{
    public class InMemoryCandleSource : ICandleSource
    {
        private readonly IEnumerable<PriceCandle> _candles;
        public InMemoryCandleSource(IEnumerable<PriceCandle> candles) => _candles = candles;
        public async IAsyncEnumerable<PriceCandle> ReadAllAsync()
        {
            foreach (var c in _candles)
            {
                yield return c;
 await Task.Yield();
            }
        }
    }
}