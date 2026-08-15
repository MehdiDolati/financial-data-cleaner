using System.Collections.Generic;
using Validator.Domain.Candles;

namespace Validator.Application.Abstractions
{
    public sealed record PreparedCandleData(IEnumerable<PriceCandle> Candles);
}