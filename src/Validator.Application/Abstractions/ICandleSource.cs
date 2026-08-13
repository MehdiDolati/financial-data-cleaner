using System.Collections.Generic;
using Validator.Domain.Candles;

namespace Validator.Application.Abstractions
{
    public interface ICandleSource
    {
        IAsyncEnumerable<PriceCandle> ReadAllAsync();
    }
}